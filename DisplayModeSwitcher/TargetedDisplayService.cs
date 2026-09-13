namespace DisplayModeSwitcher;

public enum NativeDisplayChangeStatus
{
    Successful,
    Restart,
    Failed,
    BadMode,
    NotUpdated,
    BadFlags,
    BadParam,
    BadDualView,
    Unknown
}

public enum TargetedDisplayStage
{
    Preflight,
    Apply,
    Verify,
    Rollback,
    Restore
}

public enum TargetedDisplayErrorCode
{
    InvalidRequest,
    TopologyReadFailed,
    MonitorMissing,
    MonitorAmbiguous,
    MonitorUnpersistable,
    CloneGroupUnsupported,
    SourceUnavailable,
    DuplicateTarget,
    DuplicateSource,
    CurrentModeReadFailed,
    ModeListReadFailed,
    ModeUnavailable,
    ModeAmbiguous,
    TopologyChanged,
    NativeChangeRejected,
    ModeNotConfirmed
}

public sealed record TargetedDisplayError(
    int? TargetIndex,
    string? MonitorDevicePath,
    TargetedDisplayStage Stage,
    TargetedDisplayErrorCode Code,
    string Message,
    int? NativeReturnCode = null,
    NativeDisplayChangeStatus? NativeStatus = null);

/// <summary>
/// Persistierbarer Beleg einer gezielten Änderung. Der Gerätepfad bindet auch
/// eine ursprüngliche Primärmonitorwahl dauerhaft an den damals aufgelösten
/// physischen Monitor. Der GDI-Name ist ausschließlich Diagnoseinformation.
/// </summary>
public sealed record TargetedDisplayReceipt(
    int TargetIndex,
    string MonitorDevicePath,
    string GdiSourceName,
    EndpointDisplayMode OriginalMode,
    EndpointDisplayMode TargetMode,
    bool ToolChanged);

public sealed record DisplayRestoreDebt(TargetedDisplayReceipt Receipt, TargetedDisplayError Error);

public sealed class TargetedDisplayApplyResult
{
    public TargetedDisplayApplyResult(
        bool success,
        IEnumerable<TargetedDisplayReceipt> receipts,
        IEnumerable<TargetedDisplayError> errors,
        IEnumerable<DisplayRestoreDebt> restoreDebts)
    {
        Success = success;
        Receipts = Array.AsReadOnly(receipts.ToArray());
        Errors = Array.AsReadOnly(errors.ToArray());
        RestoreDebts = Array.AsReadOnly(restoreDebts.ToArray());
    }

    public bool Success { get; }
    public IReadOnlyList<TargetedDisplayReceipt> Receipts { get; }
    public IReadOnlyList<TargetedDisplayError> Errors { get; }
    public IReadOnlyList<DisplayRestoreDebt> RestoreDebts { get; }
}

public sealed class TargetedDisplayRestoreResult
{
    public TargetedDisplayRestoreResult(
        IEnumerable<TargetedDisplayError> errors,
        IEnumerable<DisplayRestoreDebt> remainingDebts)
    {
        Errors = Array.AsReadOnly(errors.ToArray());
        RemainingDebts = Array.AsReadOnly(remainingDebts.ToArray());
    }

    public bool Success => RemainingDebts.Count == 0;
    public IReadOnlyList<TargetedDisplayError> Errors { get; }
    public IReadOnlyList<DisplayRestoreDebt> RemainingDebts { get; }
}

public interface ITargetedDisplayService
{
    TargetedDisplayApplyResult Apply(IReadOnlyList<DisplayProfileTarget> targets);
    TargetedDisplayRestoreResult Restore(IReadOnlyList<TargetedDisplayReceipt> receipts);
}

/// <summary>
/// Führt ausschließlich explizit aufgelöste, quellbezogene Modusänderungen aus.
/// Die Profil-Laufzeit nutzt diese Schicht; das manuelle Tray-Schalten bleibt
/// bis zur späteren Monitorwahl-Oberfläche getrennt.
/// </summary>
public sealed class TargetedDisplayService : ITargetedDisplayService
{
    private sealed record PlannedTarget(
        int TargetIndex,
        DisplayProfileTarget Request,
        string MonitorDevicePath,
        DisplaySourceIdentity SourceIdentity,
        string GdiSourceName,
        EndpointDisplayMode OriginalMode,
        EndpointDisplayMode TargetMode);

    private readonly IDisplayTopologyService _topology;
    private readonly IWindowsDisplayApi _windows;

    public TargetedDisplayService(IDisplayTopologyService topology, IWindowsDisplayApi windows)
    {
        _topology = topology ?? throw new ArgumentNullException(nameof(topology));
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
    }

    public TargetedDisplayApplyResult Apply(IReadOnlyList<DisplayProfileTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var preflight = Preflight(targets);
        if (preflight.Errors.Count != 0)
            return new TargetedDisplayApplyResult(false, [], preflight.Errors, []);

        var plans = preflight.Plans
            .OrderBy(plan => plan.MonitorDevicePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(plan => plan.TargetIndex)
            .ToArray();
        var receipts = plans.ToDictionary(
            plan => plan.TargetIndex,
            plan => ToReceipt(plan, toolChanged: false));
        var changed = new List<TargetedDisplayReceipt>();

        foreach (var plan in plans)
        {
            var stabilityError = ValidateAllBindings(plans, plan.TargetIndex, out var stableSnapshot);
            if (stabilityError is not null)
                return FailAndRollback(receipts, changed, stabilityError);

            var currentEndpoint = ResolveConcretePath(stableSnapshot, plan.MonitorDevicePath, out var pathError);
            if (pathError is not null || currentEndpoint is null)
                return FailAndRollback(receipts, changed, WithTarget(pathError!, plan, TargetedDisplayStage.Apply));

            var current = _topology.GetCurrentMode(currentEndpoint);
            if (!current.Success || current.Value is null)
            {
                var error = ReadError(plan, TargetedDisplayStage.Apply, TargetedDisplayErrorCode.CurrentModeReadFailed,
                    current.Error, "Der unmittelbar vor dem Setzen aktive Modus konnte nicht gelesen werden.");
                return FailAndRollback(receipts, changed, error);
            }

            if (AreEquivalent(current.Value, plan.TargetMode))
                continue;

            var nativeCode = _windows.ChangeDisplaySettings(
                plan.GdiSourceName,
                plan.TargetMode,
                WindowsDisplayChangeKind.ApplyTemporary);
            var nativeStatus = ClassifyNativeReturnCode(nativeCode);
            if (nativeStatus != NativeDisplayChangeStatus.Successful)
            {
                var error = NativeError(plan, TargetedDisplayStage.Apply, nativeCode,
                    "Windows hat die temporäre Modusänderung abgelehnt.");
                return FailAndRollback(receipts, changed, error);
            }

            var changedReceipt = ToReceipt(plan, toolChanged: true);
            receipts[plan.TargetIndex] = changedReceipt;
            changed.Add(changedReceipt);

            var verifyError = VerifyAppliedTarget(plan);
            if (verifyError is not null)
                return FailAndRollback(receipts, changed, verifyError);
        }

        var orderedReceipts = receipts.Values.OrderBy(receipt => receipt.TargetIndex).ToArray();
        return new TargetedDisplayApplyResult(true, orderedReceipts, [], []);
    }

    public TargetedDisplayRestoreResult Restore(IReadOnlyList<TargetedDisplayReceipt> receipts)
    {
        ArgumentNullException.ThrowIfNull(receipts);
        var errors = new List<TargetedDisplayError>();
        var debts = new List<DisplayRestoreDebt>();
        var changedReceipts = receipts.Where(receipt => receipt.ToolChanged).ToArray();
        var duplicatePaths = changedReceipts
            .GroupBy(receipt => receipt.MonitorDevicePath, StringComparer.OrdinalIgnoreCase)
            .Where(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1)
            .SelectMany(group => group)
            .ToHashSet();

        foreach (var receipt in changedReceipts.Reverse())
        {
            TargetedDisplayError? error;
            if (duplicatePaths.Contains(receipt))
            {
                error = new TargetedDisplayError(
                    receipt.TargetIndex,
                    receipt.MonitorDevicePath,
                    TargetedDisplayStage.Restore,
                    TargetedDisplayErrorCode.DuplicateTarget,
                    "Die Wiederherstellungsbelege enthalten keinen eindeutigen Monitor-Gerätepfad.");
            }
            else
            {
                error = RestoreOne(receipt, TargetedDisplayStage.Restore);
            }

            if (error is null)
                continue;
            errors.Add(error);
            debts.Add(new DisplayRestoreDebt(receipt, error));
        }

        return new TargetedDisplayRestoreResult(errors, debts);
    }

    public static NativeDisplayChangeStatus ClassifyNativeReturnCode(int code) => code switch
    {
        0 => NativeDisplayChangeStatus.Successful,
        1 => NativeDisplayChangeStatus.Restart,
        -1 => NativeDisplayChangeStatus.Failed,
        -2 => NativeDisplayChangeStatus.BadMode,
        -3 => NativeDisplayChangeStatus.NotUpdated,
        -4 => NativeDisplayChangeStatus.BadFlags,
        -5 => NativeDisplayChangeStatus.BadParam,
        -6 => NativeDisplayChangeStatus.BadDualView,
        _ => NativeDisplayChangeStatus.Unknown
    };

    private (IReadOnlyList<PlannedTarget> Plans, IReadOnlyList<TargetedDisplayError> Errors) Preflight(
        IReadOnlyList<DisplayProfileTarget> targets)
    {
        var errors = new List<TargetedDisplayError>();
        if (targets.Count == 0)
        {
            errors.Add(new TargetedDisplayError(null, null, TargetedDisplayStage.Preflight,
                TargetedDisplayErrorCode.InvalidRequest, "Es wurde kein Monitorziel angegeben."));
            return ([], errors);
        }

        var snapshotResult = _topology.GetSnapshot();
        if (!snapshotResult.Success || snapshotResult.Value is null)
        {
            errors.Add(new TargetedDisplayError(null, null, TargetedDisplayStage.Preflight,
                TargetedDisplayErrorCode.TopologyReadFailed,
                snapshotResult.Error?.Message ?? "Die Anzeigetopologie konnte nicht gelesen werden.",
                snapshotResult.Error?.NativeErrorCode));
            return ([], errors);
        }

        var plans = new List<PlannedTarget>(targets.Count);
        for (var index = 0; index < targets.Count; index++)
        {
            var request = targets[index];
            if (request is null || request.MonitorSelector is null || request.Mode is null ||
                request.Mode.Width == 0 || request.Mode.Height == 0 || request.Mode.Frequency == 0)
            {
                errors.Add(new TargetedDisplayError(index, null, TargetedDisplayStage.Preflight,
                    TargetedDisplayErrorCode.InvalidRequest, "Das Monitorziel oder sein Modus ist ungültig."));
                continue;
            }

            var match = MonitorSelectorMatcher.Resolve(snapshotResult.Value, request.MonitorSelector);
            if (!match.Success || match.Endpoint is null)
            {
                errors.Add(MatchError(index, match));
                continue;
            }

            var endpoint = match.Endpoint;
            if (!endpoint.IsPersistable)
            {
                errors.Add(new TargetedDisplayError(index, endpoint.MonitorDevicePath,
                    TargetedDisplayStage.Preflight, TargetedDisplayErrorCode.MonitorUnpersistable,
                    "Der gewählte Monitor besitzt keinen persistierbaren Gerätepfad."));
                continue;
            }
            if (snapshotResult.Value.Endpoints.Count(candidate => candidate.IsPersistable && string.Equals(
                    candidate.MonitorDevicePath, endpoint.MonitorDevicePath, StringComparison.OrdinalIgnoreCase)) != 1)
            {
                errors.Add(new TargetedDisplayError(index, endpoint.MonitorDevicePath,
                    TargetedDisplayStage.Preflight, TargetedDisplayErrorCode.MonitorAmbiguous,
                    "Der gewählte Monitor-Gerätepfad ist in der Topologie nicht eindeutig."));
                continue;
            }
            if (endpoint.IsCloneSource)
            {
                errors.Add(new TargetedDisplayError(index, endpoint.MonitorDevicePath,
                    TargetedDisplayStage.Preflight, TargetedDisplayErrorCode.CloneGroupUnsupported,
                    "Der gewählte Monitor gehört zu einer Klon-Gruppe."));
                continue;
            }
            if (string.IsNullOrWhiteSpace(endpoint.GdiSourceName))
            {
                errors.Add(new TargetedDisplayError(index, endpoint.MonitorDevicePath,
                    TargetedDisplayStage.Preflight, TargetedDisplayErrorCode.SourceUnavailable,
                    "Der gewählte Monitor besitzt keine explizite GDI-Anzeigequelle."));
                continue;
            }
            if (snapshotResult.Value.Endpoints.Count(candidate => candidate.SourceIdentity == endpoint.SourceIdentity) != 1 ||
                snapshotResult.Value.Endpoints.Count(candidate => !string.IsNullOrWhiteSpace(candidate.GdiSourceName) &&
                    string.Equals(candidate.GdiSourceName, endpoint.GdiSourceName, StringComparison.OrdinalIgnoreCase)) != 1)
            {
                errors.Add(new TargetedDisplayError(index, endpoint.MonitorDevicePath,
                    TargetedDisplayStage.Preflight, TargetedDisplayErrorCode.DuplicateSource,
                    "Die Anzeigequelle des gewählten Monitors ist nicht eindeutig einem physischen Ziel zugeordnet."));
                continue;
            }

            var currentResult = _topology.GetCurrentMode(endpoint);
            if (!currentResult.Success || currentResult.Value is null)
            {
                errors.Add(ReadError(index, endpoint.MonitorDevicePath, TargetedDisplayStage.Preflight,
                    TargetedDisplayErrorCode.CurrentModeReadFailed, currentResult.Error,
                    "Der Originalmodus des gewählten Monitors konnte nicht gelesen werden."));
                continue;
            }
            if (!IsNativeModeValid(currentResult.Value))
            {
                errors.Add(new TargetedDisplayError(index, endpoint.MonitorDevicePath,
                    TargetedDisplayStage.Preflight, TargetedDisplayErrorCode.CurrentModeReadFailed,
                    "Der Originalmodus ist unvollständig und kann deshalb nicht sicher wiederhergestellt werden."));
                continue;
            }

            var modesResult = _topology.GetAvailableModes(endpoint);
            if (!modesResult.Success || modesResult.Value is null)
            {
                errors.Add(ReadError(index, endpoint.MonitorDevicePath, TargetedDisplayStage.Preflight,
                    TargetedDisplayErrorCode.ModeListReadFailed, modesResult.Error,
                    "Die Modi des gewählten Monitors konnten nicht gelesen werden."));
                continue;
            }

            var selection = SelectCandidate(request.Mode, currentResult.Value, modesResult.Value);
            if (selection.Candidate is null)
            {
                errors.Add(new TargetedDisplayError(index, endpoint.MonitorDevicePath,
                    TargetedDisplayStage.Preflight, selection.ErrorCode!.Value, selection.Message));
                continue;
            }

            plans.Add(new PlannedTarget(
                index,
                request,
                endpoint.MonitorDevicePath,
                endpoint.SourceIdentity,
                endpoint.GdiSourceName,
                currentResult.Value,
                selection.Candidate));
        }

        foreach (var duplicate in plans.GroupBy(plan => plan.MonitorDevicePath, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            foreach (var plan in duplicate)
                errors.Add(PlanError(plan, TargetedDisplayStage.Preflight, TargetedDisplayErrorCode.DuplicateTarget,
                    "Mehrere Monitorwahlen lösen auf denselben physischen Monitor auf."));
        }
        foreach (var duplicate in plans.GroupBy(plan => plan.SourceIdentity).Where(group => group.Count() > 1))
        {
            foreach (var plan in duplicate)
                errors.Add(PlanError(plan, TargetedDisplayStage.Preflight, TargetedDisplayErrorCode.DuplicateSource,
                    "Mehrere Monitorwahlen lösen auf dieselbe Anzeigequelle auf."));
        }
        foreach (var duplicate in plans.GroupBy(plan => plan.GdiSourceName, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            foreach (var plan in duplicate)
                errors.Add(PlanError(plan, TargetedDisplayStage.Preflight, TargetedDisplayErrorCode.DuplicateSource,
                    "Mehrere Monitorwahlen lösen auf denselben GDI-Anzeigenamen auf."));
        }

        if (errors.Count != 0)
            return (plans, errors);

        foreach (var plan in plans.OrderBy(plan => plan.MonitorDevicePath, StringComparer.OrdinalIgnoreCase).ThenBy(plan => plan.TargetIndex))
        {
            var nativeCode = _windows.ChangeDisplaySettings(
                plan.GdiSourceName,
                plan.TargetMode,
                WindowsDisplayChangeKind.Test);
            if (ClassifyNativeReturnCode(nativeCode) != NativeDisplayChangeStatus.Successful)
                errors.Add(NativeError(plan, TargetedDisplayStage.Preflight, nativeCode,
                    "Windows hat den Zielmodus beim vollständigen Preflight abgelehnt."));
        }

        return (plans, errors);
    }

    private TargetedDisplayApplyResult FailAndRollback(
        IDictionary<int, TargetedDisplayReceipt> receipts,
        IReadOnlyList<TargetedDisplayReceipt> changed,
        TargetedDisplayError applyError)
    {
        var errors = new List<TargetedDisplayError> { applyError };
        var debts = new List<DisplayRestoreDebt>();
        foreach (var receipt in changed.Reverse())
        {
            var rollbackError = RestoreOne(receipt, TargetedDisplayStage.Rollback);
            if (rollbackError is null)
            {
                receipts[receipt.TargetIndex] = receipt with { ToolChanged = false };
                continue;
            }
            errors.Add(rollbackError);
            debts.Add(new DisplayRestoreDebt(receipt, rollbackError));
        }

        return new TargetedDisplayApplyResult(
            false,
            receipts.Values.OrderBy(receipt => receipt.TargetIndex),
            errors,
            debts);
    }

    private TargetedDisplayError? ValidateAllBindings(
        IReadOnlyList<PlannedTarget> plans,
        int activeTargetIndex,
        out DisplayTopologySnapshot? stableSnapshot)
    {
        var snapshotResult = _topology.GetSnapshot();
        if (!snapshotResult.Success || snapshotResult.Value is null)
        {
            stableSnapshot = null;
            return new TargetedDisplayError(activeTargetIndex, null, TargetedDisplayStage.Apply,
                TargetedDisplayErrorCode.TopologyReadFailed,
                snapshotResult.Error?.Message ?? "Die Anzeigetopologie konnte vor dem Setzen nicht erneut gelesen werden.",
                snapshotResult.Error?.NativeErrorCode);
        }

        stableSnapshot = snapshotResult.Value;

        foreach (var plan in plans)
        {
            var match = MonitorSelectorMatcher.Resolve(stableSnapshot, plan.Request.MonitorSelector);
            if (!match.Success || match.Endpoint is null ||
                !string.Equals(match.Endpoint.MonitorDevicePath, plan.MonitorDevicePath, StringComparison.OrdinalIgnoreCase) ||
                match.Endpoint.SourceIdentity != plan.SourceIdentity ||
                !string.Equals(match.Endpoint.GdiSourceName, plan.GdiSourceName, StringComparison.OrdinalIgnoreCase) ||
                stableSnapshot.Endpoints.Count(endpoint => endpoint.IsPersistable && string.Equals(
                    endpoint.MonitorDevicePath, plan.MonitorDevicePath, StringComparison.OrdinalIgnoreCase)) != 1 ||
                stableSnapshot.Endpoints.Count(endpoint => endpoint.SourceIdentity == plan.SourceIdentity) != 1 ||
                stableSnapshot.Endpoints.Count(endpoint => string.Equals(
                    endpoint.GdiSourceName, plan.GdiSourceName, StringComparison.OrdinalIgnoreCase)) != 1)
            {
                return PlanError(plan, TargetedDisplayStage.Apply, TargetedDisplayErrorCode.TopologyChanged,
                    "Die Monitor- oder Quellenzuordnung hat sich seit dem Preflight geändert.");
            }
        }

        return null;
    }

    private TargetedDisplayError? VerifyAppliedTarget(PlannedTarget plan)
    {
        var snapshotResult = _topology.GetSnapshot();
        if (!snapshotResult.Success || snapshotResult.Value is null)
            return ReadError(plan, TargetedDisplayStage.Verify, TargetedDisplayErrorCode.TopologyReadFailed,
                snapshotResult.Error, "Die Topologie konnte nach dem Setzen nicht bestätigt werden.");

        var match = MonitorSelectorMatcher.Resolve(snapshotResult.Value, plan.Request.MonitorSelector);
        if (!match.Success || match.Endpoint is null ||
            !string.Equals(match.Endpoint.MonitorDevicePath, plan.MonitorDevicePath, StringComparison.OrdinalIgnoreCase) ||
            match.Endpoint.SourceIdentity != plan.SourceIdentity ||
            !string.Equals(match.Endpoint.GdiSourceName, plan.GdiSourceName, StringComparison.OrdinalIgnoreCase) ||
            snapshotResult.Value.Endpoints.Count(endpoint => endpoint.IsPersistable && string.Equals(
                endpoint.MonitorDevicePath, plan.MonitorDevicePath, StringComparison.OrdinalIgnoreCase)) != 1 ||
            snapshotResult.Value.Endpoints.Count(endpoint => endpoint.SourceIdentity == plan.SourceIdentity) != 1 ||
            snapshotResult.Value.Endpoints.Count(endpoint => string.Equals(
                endpoint.GdiSourceName, plan.GdiSourceName, StringComparison.OrdinalIgnoreCase)) != 1)
        {
            return PlanError(plan, TargetedDisplayStage.Verify, TargetedDisplayErrorCode.TopologyChanged,
                "Das gesetzte physische Monitorziel konnte danach nicht stabil bestätigt werden.");
        }

        var current = _topology.GetCurrentMode(match.Endpoint);
        if (!current.Success || current.Value is null)
            return ReadError(plan, TargetedDisplayStage.Verify, TargetedDisplayErrorCode.CurrentModeReadFailed,
                current.Error, "Der gesetzte Modus konnte danach nicht gelesen werden.");
        return AreEquivalent(current.Value, plan.TargetMode)
            ? null
            : PlanError(plan, TargetedDisplayStage.Verify, TargetedDisplayErrorCode.ModeNotConfirmed,
                "Windows meldet nach dem Setzen nicht den erwarteten Zielmodus.");
    }

    private TargetedDisplayError? RestoreOne(TargetedDisplayReceipt receipt, TargetedDisplayStage stage)
    {
        if (!IsNativeModeValid(receipt.OriginalMode))
        {
            return new TargetedDisplayError(receipt.TargetIndex, receipt.MonitorDevicePath, stage,
                TargetedDisplayErrorCode.InvalidRequest,
                "Der Wiederherstellungsbeleg enthält keinen vollständigen nativen Originalmodus.");
        }

        var snapshot = GetSnapshotOrNull(out var snapshotError);
        if (snapshotError is not null)
            return WithReceipt(snapshotError, receipt, stage);

        var endpoint = ResolveConcretePath(snapshot, receipt.MonitorDevicePath, out var pathError);
        if (pathError is not null || endpoint is null)
            return WithReceipt(pathError!, receipt, stage);

        var current = _topology.GetCurrentMode(endpoint);
        if (!current.Success || current.Value is null)
            return ReadError(receipt, stage, TargetedDisplayErrorCode.CurrentModeReadFailed, current.Error,
                "Der aktuelle Modus konnte vor der Wiederherstellung nicht gelesen werden.");
        if (AreEquivalent(current.Value, receipt.OriginalMode))
            return null;

        var testCode = _windows.ChangeDisplaySettings(endpoint.GdiSourceName, receipt.OriginalMode, WindowsDisplayChangeKind.Test);
        if (ClassifyNativeReturnCode(testCode) != NativeDisplayChangeStatus.Successful)
            return NativeError(receipt, stage, testCode, "Windows hat den Originalmodus beim Wiederherstellungstest abgelehnt.");

        var stableSnapshot = GetSnapshotOrNull(out snapshotError);
        if (snapshotError is not null)
            return WithReceipt(snapshotError, receipt, stage);
        var stableEndpoint = ResolveConcretePath(stableSnapshot, receipt.MonitorDevicePath, out pathError);
        if (pathError is not null || stableEndpoint is null)
            return WithReceipt(pathError!, receipt, stage);
        if (stableEndpoint.SourceIdentity != endpoint.SourceIdentity ||
            !string.Equals(stableEndpoint.GdiSourceName, endpoint.GdiSourceName, StringComparison.OrdinalIgnoreCase))
        {
            return new TargetedDisplayError(receipt.TargetIndex, receipt.MonitorDevicePath, stage,
                TargetedDisplayErrorCode.TopologyChanged,
                "Die Anzeigequelle hat sich zwischen Wiederherstellungstest und Apply geändert.");
        }

        var applyCode = _windows.ChangeDisplaySettings(stableEndpoint.GdiSourceName, receipt.OriginalMode, WindowsDisplayChangeKind.ApplyTemporary);
        if (ClassifyNativeReturnCode(applyCode) != NativeDisplayChangeStatus.Successful)
            return NativeError(receipt, stage, applyCode, "Windows hat die temporäre Wiederherstellung abgelehnt.");

        var afterSnapshot = GetSnapshotOrNull(out snapshotError);
        if (snapshotError is not null)
            return WithReceipt(snapshotError, receipt, stage);
        var afterEndpoint = ResolveConcretePath(afterSnapshot, receipt.MonitorDevicePath, out pathError);
        if (pathError is not null || afterEndpoint is null)
            return WithReceipt(pathError!, receipt, stage);
        var confirmed = _topology.GetCurrentMode(afterEndpoint);
        if (!confirmed.Success || confirmed.Value is null)
            return ReadError(receipt, stage, TargetedDisplayErrorCode.CurrentModeReadFailed, confirmed.Error,
                "Der wiederhergestellte Modus konnte nicht bestätigt werden.");
        return AreEquivalent(confirmed.Value, receipt.OriginalMode)
            ? null
            : new TargetedDisplayError(receipt.TargetIndex, receipt.MonitorDevicePath, stage,
                TargetedDisplayErrorCode.ModeNotConfirmed,
                "Windows meldet nach der Wiederherstellung nicht den erwarteten Originalmodus.");
    }

    private DisplayTopologySnapshot? GetSnapshotOrNull(out TargetedDisplayError? error)
    {
        var result = _topology.GetSnapshot();
        if (result.Success && result.Value is not null)
        {
            error = null;
            return result.Value;
        }

        error = new TargetedDisplayError(null, null, TargetedDisplayStage.Restore,
            TargetedDisplayErrorCode.TopologyReadFailed,
            result.Error?.Message ?? "Die Anzeigetopologie konnte nicht gelesen werden.",
            result.Error?.NativeErrorCode);
        return null;
    }

    private static DisplayEndpoint? ResolveConcretePath(
        DisplayTopologySnapshot? snapshot,
        string monitorDevicePath,
        out TargetedDisplayError? error)
    {
        if (snapshot is null)
        {
            error = new TargetedDisplayError(null, monitorDevicePath, TargetedDisplayStage.Restore,
                TargetedDisplayErrorCode.TopologyReadFailed, "Die Anzeigetopologie fehlt.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(monitorDevicePath))
        {
            error = new TargetedDisplayError(null, monitorDevicePath, TargetedDisplayStage.Restore,
                TargetedDisplayErrorCode.MonitorUnpersistable, "Der Monitor-Beleg enthält keinen Gerätepfad.");
            return null;
        }

        var matches = snapshot.Endpoints.Where(endpoint => endpoint.IsPersistable && string.Equals(
            endpoint.MonitorDevicePath, monitorDevicePath, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0)
        {
            error = new TargetedDisplayError(null, monitorDevicePath, TargetedDisplayStage.Restore,
                TargetedDisplayErrorCode.MonitorMissing, "Der konkrete Monitor ist derzeit nicht verbunden.");
            return null;
        }
        if (matches.Length != 1)
        {
            error = new TargetedDisplayError(null, monitorDevicePath, TargetedDisplayStage.Restore,
                TargetedDisplayErrorCode.MonitorAmbiguous, "Der konkrete Monitor-Gerätepfad ist nicht eindeutig.");
            return null;
        }
        if (matches[0].IsCloneSource)
        {
            error = new TargetedDisplayError(null, monitorDevicePath, TargetedDisplayStage.Restore,
                TargetedDisplayErrorCode.CloneGroupUnsupported, "Der konkrete Monitor gehört zu einer Klon-Gruppe.");
            return null;
        }
        if (string.IsNullOrWhiteSpace(matches[0].GdiSourceName))
        {
            error = new TargetedDisplayError(null, monitorDevicePath, TargetedDisplayStage.Restore,
                TargetedDisplayErrorCode.SourceUnavailable, "Der konkrete Monitor besitzt derzeit keine GDI-Anzeigequelle.");
            return null;
        }
        if (snapshot.Endpoints.Count(endpoint => endpoint.SourceIdentity == matches[0].SourceIdentity) != 1 ||
            snapshot.Endpoints.Count(endpoint => string.Equals(
                endpoint.GdiSourceName, matches[0].GdiSourceName, StringComparison.OrdinalIgnoreCase)) != 1)
        {
            error = new TargetedDisplayError(null, monitorDevicePath, TargetedDisplayStage.Restore,
                TargetedDisplayErrorCode.DuplicateSource,
                "Die aktuelle Anzeigequelle des konkreten Monitors ist nicht eindeutig.");
            return null;
        }

        error = null;
        return matches[0];
    }

    private static (EndpointDisplayMode? Candidate, TargetedDisplayErrorCode? ErrorCode, string Message) SelectCandidate(
        DisplayMode requested,
        EndpointDisplayMode current,
        IReadOnlyList<EndpointDisplayMode> available)
    {
        var candidates = available
            .Where(candidate => candidate.Width != 0 && candidate.Height != 0 && candidate.Frequency != 0 && candidate.BitsPerPixel != 0)
            .Where(candidate => candidate.Width == requested.Width &&
                candidate.Height == requested.Height &&
                candidate.Frequency == requested.Frequency &&
                candidate.Orientation == current.Orientation)
            .Distinct()
            .ToArray();
        if (candidates.Length == 0)
        {
            return (null, TargetedDisplayErrorCode.ModeUnavailable,
                "Der profilierte Modus ist auf genau dieser Anzeigequelle nicht verfügbar.");
        }

        var selected = EndpointDisplayModeCandidateSelector.Select(candidates, current);
        return selected is not null
            ? (selected, null, string.Empty)
            : (null, TargetedDisplayErrorCode.ModeUnavailable,
                "Der profilierte Modus konnte keinem nativen Moduskandidaten zugeordnet werden.");
    }

    private static bool AreEquivalent(EndpointDisplayMode first, EndpointDisplayMode second) =>
        first.Width == second.Width && first.Height == second.Height &&
        Math.Abs((long)first.Frequency - second.Frequency) <= 1;

    private static bool IsNativeModeValid(EndpointDisplayMode mode) =>
        mode.Width != 0 && mode.Height != 0 && mode.Frequency != 0 && mode.BitsPerPixel != 0;

    private static TargetedDisplayReceipt ToReceipt(PlannedTarget plan, bool toolChanged) => new(
        plan.TargetIndex,
        plan.MonitorDevicePath,
        plan.GdiSourceName,
        plan.OriginalMode,
        plan.TargetMode,
        toolChanged);

    private static TargetedDisplayError MatchError(int targetIndex, MonitorMatchResult match) => new(
        targetIndex,
        match.Endpoint?.MonitorDevicePath,
        TargetedDisplayStage.Preflight,
        match.Status switch
        {
            MonitorMatchStatus.Ambiguous => TargetedDisplayErrorCode.MonitorAmbiguous,
            MonitorMatchStatus.Unpersistable => TargetedDisplayErrorCode.MonitorUnpersistable,
            MonitorMatchStatus.CloneGroupUnsupported => TargetedDisplayErrorCode.CloneGroupUnsupported,
            _ => TargetedDisplayErrorCode.MonitorMissing
        },
        match.Message);

    private static TargetedDisplayError NativeError(
        PlannedTarget plan,
        TargetedDisplayStage stage,
        int code,
        string message) => new(plan.TargetIndex, plan.MonitorDevicePath, stage,
            TargetedDisplayErrorCode.NativeChangeRejected, message, code, ClassifyNativeReturnCode(code));

    private static TargetedDisplayError NativeError(
        TargetedDisplayReceipt receipt,
        TargetedDisplayStage stage,
        int code,
        string message) => new(receipt.TargetIndex, receipt.MonitorDevicePath, stage,
            TargetedDisplayErrorCode.NativeChangeRejected, message, code, ClassifyNativeReturnCode(code));

    private static TargetedDisplayError PlanError(
        PlannedTarget plan,
        TargetedDisplayStage stage,
        TargetedDisplayErrorCode code,
        string message) => new(plan.TargetIndex, plan.MonitorDevicePath, stage, code, message);

    private static TargetedDisplayError ReadError(
        PlannedTarget plan,
        TargetedDisplayStage stage,
        TargetedDisplayErrorCode code,
        DisplayReadError? readError,
        string message) => ReadError(plan.TargetIndex, plan.MonitorDevicePath, stage, code, readError, message);

    private static TargetedDisplayError ReadError(
        TargetedDisplayReceipt receipt,
        TargetedDisplayStage stage,
        TargetedDisplayErrorCode code,
        DisplayReadError? readError,
        string message) => ReadError(receipt.TargetIndex, receipt.MonitorDevicePath, stage, code, readError, message);

    private static TargetedDisplayError ReadError(
        int targetIndex,
        string? path,
        TargetedDisplayStage stage,
        TargetedDisplayErrorCode code,
        DisplayReadError? readError,
        string message) => new(targetIndex, path, stage, code,
            readError is null ? message : $"{message} {readError.Message}", readError?.NativeErrorCode);

    private static TargetedDisplayError WithTarget(TargetedDisplayError error, PlannedTarget plan, TargetedDisplayStage stage) =>
        error with { TargetIndex = plan.TargetIndex, MonitorDevicePath = plan.MonitorDevicePath, Stage = stage };

    private static TargetedDisplayError WithReceipt(TargetedDisplayError error, TargetedDisplayReceipt receipt, TargetedDisplayStage stage) =>
        error with { TargetIndex = receipt.TargetIndex, MonitorDevicePath = receipt.MonitorDevicePath, Stage = stage };
}
