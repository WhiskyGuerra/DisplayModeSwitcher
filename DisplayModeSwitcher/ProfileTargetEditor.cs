using System.Globalization;

namespace DisplayModeSwitcher;

/// <summary>
/// UI-unabhängiger Entwurf für die explizite Monitorziel-Auswahl. Der Entwurf
/// liest ausschließlich Topologiedaten; Display-Schreibzugriffe gehören nicht
/// zu diesem Typ und können deshalb auch von Tests nicht versehentlich ausgelöst werden.
/// </summary>
public sealed class ProfileTargetEditor
{
    private readonly IDisplayTopologyService _topology;
    private readonly List<DisplayProfileTarget> _targets = [];
    private DisplayProfile? _original;
    private DisplayTopologySnapshot? _snapshot;

    public ProfileTargetEditor(IDisplayTopologyService topology)
    {
        _topology = topology ?? throw new ArgumentNullException(nameof(topology));
    }

    public IReadOnlyList<DisplayProfileTarget> Targets => _targets.Select(CloneTarget).ToArray();
    public IReadOnlyList<MonitorChoice> Choices { get; private set; } = [MonitorChoice.Primary()];
    public string? TopologyError { get; private set; }
    public bool IsDirty => _original is null ? _targets.Count > 0 : !_original.Targets.SequenceEqual(_targets);
    public bool CanSave => _targets.Count > 0 && !GetTargetStates().Any(state =>
        state.MatchStatus is MonitorMatchStatus.Ambiguous or MonitorMatchStatus.CloneGroupUnsupported or MonitorMatchStatus.Unpersistable);
    public bool HasBlockingResolution => GetTargetStates().Any(state => state.MatchStatus != MonitorMatchStatus.Exact);

    public void BeginNew()
    {
        _targets.Clear();
        _original = null;
    }

    public void Load(DisplayProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _targets.Clear();
        _targets.AddRange(profile.Targets.Select(CloneTarget));
        _original = profile.DeepCopy();
    }

    public EditorResult RefreshTopology()
    {
        try
        {
            var result = _topology.GetSnapshot();
            if (!result.Success || result.Value is null)
            {
                _snapshot = null;
                TopologyError = result.Error?.Message ?? "Die Monitor-Topologie konnte nicht gelesen werden.";
                Choices = [MonitorChoice.Primary()];
                return EditorResult.Fail(TopologyError);
            }

            _snapshot = result.Value;
            TopologyError = null;
            var choices = new List<MonitorChoice> { MonitorChoice.Primary() };
            foreach (var endpoint in _snapshot.Endpoints
                .Where(endpoint => ResolveSafe(_snapshot, CreateSpecificSelector(endpoint)).Success)
                .OrderBy(DisplayEndpointLabel.Format, StringComparer.CurrentCultureIgnoreCase))
            {
                choices.Add(MonitorChoice.Specific(endpoint));
            }
            Choices = choices;
            return EditorResult.Ok();
        }
        catch (Exception ex)
        {
            _snapshot = null;
            TopologyError = $"Die Monitor-Topologie konnte nicht gelesen werden: {ex.Message}";
            Choices = [MonitorChoice.Primary()];
            return EditorResult.Fail(TopologyError);
        }
    }

    public ModeListResult GetModes(MonitorChoice? choice, DisplayMode? preservedMode = null)
    {
        if (choice is null)
            return ModeListResult.Fail("Bitte zuerst einen Monitor auswählen.", preservedMode);
        if (_snapshot is null)
            return ModeListResult.Fail(TopologyError ?? "Die Monitor-Topologie ist nicht verfügbar.", preservedMode);

        var snapshot = _snapshot;
        var match = ResolveSafe(snapshot, choice.Selector);
        if (!match.Success || match.Endpoint is null)
            return ModeListResult.Fail(match.Message, preservedMode);

        try
        {
            var currentResult = _topology.GetCurrentMode(match.Endpoint);
            if (!currentResult.Success || currentResult.Value is null || !IsNativeModeValid(currentResult.Value))
                return ModeListResult.Fail(currentResult.Error?.Message ?? "Der aktuelle Anzeigemodus konnte nicht sicher gelesen werden.", preservedMode);

            var result = _topology.GetAvailableModes(match.Endpoint);
            if (!result.Success || result.Value is null)
                return ModeListResult.Fail(result.Error?.Message ?? "Die Anzeigemodi konnten nicht gelesen werden.", preservedMode);

            var modes = result.Value
                .Where(IsNativeModeValid)
                .Where(mode => mode.Orientation == currentResult.Value.Orientation)
                .Distinct()
                .GroupBy(mode => (mode.Width, mode.Height, mode.Frequency))
                .Select(group => SelectSafeCandidate(group, currentResult.Value))
                .OfType<EndpointDisplayMode>()
                .OrderByDescending(mode => mode.Width)
                .ThenByDescending(mode => mode.Height)
                .ThenByDescending(mode => mode.Frequency)
                .Select(ToDisplayMode)
                .ToList();
            AddPreservedMode(modes, preservedMode);
            return ModeListResult.Ok(modes);
        }
        catch (Exception ex)
        {
            return ModeListResult.Fail($"Die Anzeigemodi konnten nicht gelesen werden: {ex.Message}", preservedMode);
        }
    }

    public EditorResult Add(MonitorChoice? choice, DisplayMode? mode)
    {
        if (choice is null)
            return EditorResult.Fail("Bitte zuerst einen Monitor auswählen.");
        if (mode is null)
            return EditorResult.Fail("Bitte einen Anzeigemodus für diesen Monitor auswählen.");
        var safe = ValidateNewTarget(choice, mode, null);
        if (!safe.Success)
            return safe;
        _targets.Add(new DisplayProfileTarget(CloneSelector(choice.Selector), CloneMode(mode)));
        return EditorResult.Ok();
    }

    public EditorResult Update(int index, MonitorChoice? choice, DisplayMode? mode)
    {
        if (!IsValidIndex(index))
            return EditorResult.Fail("Bitte zuerst ein konfiguriertes Ziel auswählen.");
        if (choice is null || mode is null)
            return EditorResult.Fail("Bitte Monitor und Anzeigemodus auswählen.");
        if (!MonitorSelectorIdentity.Equals(_targets[index].MonitorSelector, choice.Selector))
            return EditorResult.Fail("Ein gespeichertes Monitorziel darf nur über ‚Ziel neu zuordnen‘ auf einen anderen Monitor gebunden werden.");
        var safe = ValidateNewTarget(choice, mode, index);
        if (!safe.Success)
            return safe;
        _targets[index] = new DisplayProfileTarget(CloneSelector(choice.Selector), CloneMode(mode));
        return EditorResult.Ok();
    }

    public EditorResult Remove(int index)
    {
        if (!IsValidIndex(index))
            return EditorResult.Fail("Bitte zuerst ein konfiguriertes Ziel auswählen.");
        _targets.RemoveAt(index);
        return EditorResult.Ok();
    }

    public RebindResult Rebind(int index, MonitorChoice? newChoice, bool confirmed, DisplayMode? explicitlySelectedMode = null)
    {
        if (!IsValidIndex(index) || _targets[index].MonitorSelector is not SpecificMonitor oldSpecific)
            return RebindResult.Fail("Nur ein gespeichertes spezifisches Monitorziel kann neu zugeordnet werden.");
        if (newChoice?.Endpoint is null)
            return RebindResult.Fail("Bitte einen aktuell verbundenen spezifischen Monitor auswählen.");
        if (!confirmed)
            return RebindResult.CancelledResult();
        if (MonitorSelectorIdentity.Equals(oldSpecific, newChoice.Selector))
            return RebindResult.Fail("Dieses Ziel ist bereits mit dem ausgewählten Monitor verbunden.");

        var modes = GetModes(newChoice, _targets[index].Mode);
        if (!modes.Success)
            return RebindResult.Fail(modes.Error!);
        var oldModeAvailable = modes.Modes.Any(candidate => SameMode(candidate, _targets[index].Mode) && !candidate.Label.Contains("nicht verfügbar", StringComparison.OrdinalIgnoreCase));
        var mode = explicitlySelectedMode ?? (oldModeAvailable ? _targets[index].Mode : null);
        if (mode is null)
            return RebindResult.NeedsMode("Der bisherige Modus ist auf dem neuen Monitor nicht verfügbar. Bitte dort bewusst einen Modus auswählen.");

        var safe = ValidateNewTarget(newChoice, mode, index);
        if (!safe.Success)
            return RebindResult.Fail(safe.Error!);
        _targets[index] = new DisplayProfileTarget(CloneSelector(newChoice.Selector), CloneMode(mode));
        return RebindResult.Ok(oldSpecific, (SpecificMonitor)newChoice.Selector, mode);
    }

    public DisplayProfile BuildProfile(ProfileRetentionPolicy policy)
    {
        if (!CanSave)
            throw new InvalidOperationException(_targets.Count == 0
                ? "Mindestens ein Monitorziel ist erforderlich."
                : "Ein Monitorziel ist in der aktuellen Topologie nicht eindeutig und muss zuerst korrigiert werden.");
        return new DisplayProfile(policy, _targets);
    }

    public IReadOnlyList<TargetDraftState> GetTargetStates()
    {
        return _targets.Select((target, index) =>
        {
            var match = _snapshot is null
                ? new MonitorMatchResult(MonitorMatchStatus.Missing, null, TopologyError ?? "Topologie nicht verfügbar.")
                : ResolveSafe(_snapshot, target.MonitorSelector);
            return new TargetDraftState(index, CloneTarget(target), match.Status, FormatTarget(target, match), match.Message);
        }).ToArray();
    }

    public MonitorChoice? FindChoice(MonitorSelector selector)
    {
        if (selector is PrimaryMonitor)
            return Choices.First(choice => choice.Selector is PrimaryMonitor);
        return Choices.FirstOrDefault(choice => MonitorSelectorIdentity.Equals(choice.Selector, selector));
    }

    public static string ShortPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "kein Gerätepfad";
        return path.Length <= 42 ? path : $"…{path[^41..]}";
    }

    private EditorResult ValidateNewTarget(MonitorChoice choice, DisplayMode mode, int? replacedIndex)
    {
        if (_snapshot is null)
            return EditorResult.Fail(TopologyError ?? "Die Monitor-Topologie ist nicht verfügbar.");
        var match = ResolveSafe(_snapshot, choice.Selector);
        if (!match.Success || match.Endpoint is null)
            return EditorResult.Fail(match.Message);

        var modes = GetModes(choice);
        if (!modes.Success)
            return EditorResult.Fail(modes.Error!);
        if (!modes.Modes.Any(candidate => SameMode(candidate, mode)))
            return EditorResult.Fail("Der gewählte Modus ist für diesen Monitor nicht eindeutig verfügbar.");

        for (var index = 0; index < _targets.Count; index++)
        {
            if (index == replacedIndex) continue;
            var existing = _targets[index];
            if (MonitorSelectorIdentity.Equals(existing.MonitorSelector, choice.Selector))
                return EditorResult.Fail("Dieser Monitor ist bereits als Ziel konfiguriert.");
            var existingMatch = ResolveSafe(_snapshot, existing.MonitorSelector);
            if (existingMatch.Success && existingMatch.Endpoint is not null && string.Equals(
                    existingMatch.Endpoint.MonitorDevicePath, match.Endpoint.MonitorDevicePath, StringComparison.OrdinalIgnoreCase))
                return EditorResult.Fail("Diese Monitorwahlen zeigen aktuell auf denselben physischen Monitor.");
        }
        return EditorResult.Ok();
    }

    private bool IsValidIndex(int index) => index >= 0 && index < _targets.Count;

    private static string FormatTarget(DisplayProfileTarget target, MonitorMatchResult match)
    {
        var mode = $"{target.Mode.Width}x{target.Mode.Height} @ {target.Mode.Frequency}Hz";
        var monitor = target.MonitorSelector switch
        {
            PrimaryMonitor => "Primärmonitor (dynamisch; aus Profil)",
            SpecificMonitor specific when match.Status == MonitorMatchStatus.Exact && match.Endpoint is not null =>
                $"{DisplayEndpointLabel.Format(match.Endpoint)} · gespeichert als {specific.FriendlyNameSnapshot}",
            SpecificMonitor specific when match.Status == MonitorMatchStatus.Missing => $"{specific.FriendlyNameSnapshot} — nicht verbunden",
            SpecificMonitor specific => $"{specific.FriendlyNameSnapshot} — {StatusText(match.Status)}",
            _ => "Unbekanntes Monitorziel"
        };
        return $"{monitor} → {mode}";
    }

    private static string StatusText(MonitorMatchStatus status) => status switch
    {
        MonitorMatchStatus.Ambiguous => "nicht eindeutig",
        MonitorMatchStatus.CloneGroupUnsupported => "Klon-Gruppe nicht unterstützt",
        MonitorMatchStatus.Unpersistable => "nicht persistierbar",
        _ => "nicht verfügbar"
    };

    private static DisplayMode ToDisplayMode(EndpointDisplayMode mode) => new()
    {
        Width = mode.Width,
        Height = mode.Height,
        Frequency = mode.Frequency,
        Label = $"{mode.Width}x{mode.Height} @ {mode.Frequency}Hz"
    };

    private static void AddPreservedMode(List<DisplayMode> modes, DisplayMode? preserved)
    {
        if (preserved is null || modes.Any(candidate => SameMode(candidate, preserved))) return;
        modes.Add(new DisplayMode
        {
            Width = preserved.Width,
            Height = preserved.Height,
            Frequency = preserved.Frequency,
            Label = $"{preserved.Width}x{preserved.Height} @ {preserved.Frequency}Hz (derzeit nicht verfügbar)"
        });
    }

    private static bool SameMode(DisplayMode left, DisplayMode right) =>
        left.Width == right.Width && left.Height == right.Height && left.Frequency == right.Frequency;

    private static EndpointDisplayMode? SelectSafeCandidate(
        IEnumerable<EndpointDisplayMode> candidates,
        EndpointDisplayMode current)
    {
        var exact = candidates.ToArray();
        if (exact.Length == 1)
            return exact[0];

        var preferred = exact
            .Where(candidate => candidate.BitsPerPixel == current.BitsPerPixel &&
                candidate.DisplayFlags == current.DisplayFlags)
            .ToArray();
        return preferred.Length == 1 ? preferred[0] : null;
    }

    private static bool IsNativeModeValid(EndpointDisplayMode mode) =>
        mode.Width > 0 && mode.Height > 0 && mode.Frequency > 0 && mode.BitsPerPixel > 0;

    private static MonitorMatchResult ResolveSafe(DisplayTopologySnapshot snapshot, MonitorSelector selector)
    {
        var match = MonitorSelectorMatcher.Resolve(snapshot, selector);
        if (!match.Success || match.Endpoint is null)
            return match;

        var endpoint = match.Endpoint;
        if (!endpoint.IsPersistable)
            return new MonitorMatchResult(MonitorMatchStatus.Unpersistable, endpoint, "Der gewählte Monitor besitzt keinen persistierbaren Gerätepfad.");
        if (snapshot.Endpoints.Count(candidate => candidate.IsPersistable && string.Equals(
                candidate.MonitorDevicePath, endpoint.MonitorDevicePath, StringComparison.OrdinalIgnoreCase)) != 1)
            return new MonitorMatchResult(MonitorMatchStatus.Ambiguous, null, "Der Monitor-Gerätepfad ist in der aktuellen Topologie nicht eindeutig.");
        if (string.IsNullOrWhiteSpace(endpoint.GdiSourceName))
            return new MonitorMatchResult(MonitorMatchStatus.Missing, null, "Der gewählte Monitor besitzt derzeit keine lesbare Anzeigequelle.");
        if (snapshot.Endpoints.Count(candidate => candidate.SourceIdentity == endpoint.SourceIdentity) != 1 ||
            snapshot.Endpoints.Count(candidate => !string.IsNullOrWhiteSpace(candidate.GdiSourceName) && string.Equals(
                candidate.GdiSourceName, endpoint.GdiSourceName, StringComparison.OrdinalIgnoreCase)) != 1)
            return new MonitorMatchResult(MonitorMatchStatus.Ambiguous, null, "Die Anzeigequelle des gewählten Monitors ist nicht eindeutig.");

        return match;
    }

    private static MonitorSelector CloneSelector(MonitorSelector selector) => selector switch
    {
        PrimaryMonitor => new PrimaryMonitor(),
        SpecificMonitor specific => new SpecificMonitor(specific.MonitorDevicePath, specific.FriendlyNameSnapshot, specific.EdidManufacturerId, specific.EdidProductCodeId),
        _ => throw new ArgumentException("Die Monitorwahl wird nicht unterstützt.", nameof(selector))
    };

    private static DisplayMode CloneMode(DisplayMode mode) => new() { Width = mode.Width, Height = mode.Height, Frequency = mode.Frequency, Label = mode.Label };
    private static DisplayProfileTarget CloneTarget(DisplayProfileTarget target) => new(CloneSelector(target.MonitorSelector), CloneMode(target.Mode));

    internal static SpecificMonitor CreateSpecificSelector(DisplayEndpoint endpoint) => new(
        endpoint.MonitorDevicePath,
        string.IsNullOrWhiteSpace(endpoint.FriendlyName) ? "Unbekannter Monitor" : endpoint.FriendlyName.Trim(),
        endpoint.EdidManufacturerId == 0 ? null : endpoint.EdidManufacturerId.ToString("X4", CultureInfo.InvariantCulture),
        endpoint.EdidProductCodeId == 0 ? null : endpoint.EdidProductCodeId.ToString("X4", CultureInfo.InvariantCulture));
}

public sealed record MonitorChoice(MonitorSelector Selector, DisplayEndpoint? Endpoint, string Label)
{
    public static MonitorChoice Primary() => new(new PrimaryMonitor(), null, "Primärmonitor (dynamisch)");
    public static MonitorChoice Specific(DisplayEndpoint endpoint) => new(ProfileTargetEditor.CreateSpecificSelector(endpoint), endpoint, DisplayEndpointLabel.Format(endpoint));
    public override string ToString() => Label;
}

public sealed record TargetDraftState(int Index, DisplayProfileTarget Target, MonitorMatchStatus MatchStatus, string Label, string Detail)
{
    public override string ToString() => Label;
}

public sealed record EditorResult(bool Success, string? Error = null)
{
    public static EditorResult Ok() => new(true);
    public static EditorResult Fail(string error) => new(false, error);
}

public sealed record ModeListResult(bool Success, IReadOnlyList<DisplayMode> Modes, string? Error = null)
{
    public static ModeListResult Ok(IReadOnlyList<DisplayMode> modes) => new(true, modes);
    public static ModeListResult Fail(string error, DisplayMode? preserved)
    {
        var modes = preserved is null ? Array.Empty<DisplayMode>() : [new DisplayMode
        {
            Width = preserved.Width, Height = preserved.Height, Frequency = preserved.Frequency,
            Label = $"{preserved.Width}x{preserved.Height} @ {preserved.Frequency}Hz (derzeit nicht verfügbar)"
        }];
        return new(false, modes, error);
    }
}

public sealed record RebindResult(bool Success, bool Cancelled, bool ModeRequired, string? Error, SpecificMonitor? OldSelector = null, SpecificMonitor? NewSelector = null, DisplayMode? Mode = null)
{
    public static RebindResult Ok(SpecificMonitor oldSelector, SpecificMonitor newSelector, DisplayMode mode) => new(true, false, false, null, oldSelector, newSelector, mode);
    public static RebindResult Fail(string error) => new(false, false, false, error);
    public static RebindResult CancelledResult() => new(false, true, false, null);
    public static RebindResult NeedsMode(string error) => new(false, false, true, error);
}
