namespace DisplayModeSwitcher;

public sealed record ManualMonitorMenu(
    string Label,
    string? MonitorDevicePath,
    bool IsPrimary,
    bool Enabled,
    string? DisabledReason,
    DisplayMode? CurrentMode,
    IReadOnlyList<DisplayMode> Modes);
public sealed record ManualDisplayMenuResult(
    bool Success,
    string? Error,
    string? ProfileBlockReason,
    IReadOnlyList<ManualMonitorMenu> Monitors);
public sealed record ManualDisplaySwitchResult(bool Success, string? Error);

/// <summary>UI-unabhängige, ausschließlich explizit pfadgebundene manuelle Monitorwahl.</summary>
public sealed class ManualDisplaySwitcher
{
    private readonly IDisplayTopologyService _topology;
    private readonly ITargetedDisplayService _display;
    private readonly Func<ProfileMonitorStatus> _profileStatus;
    private readonly Action<DateTime, string>? _diagnosticEvent;

    public ManualDisplaySwitcher(
        IDisplayTopologyService topology,
        ITargetedDisplayService display,
        Func<ProfileMonitorStatus> profileStatus,
        Action<DateTime, string>? diagnosticEvent = null)
    {
        _topology = topology ?? throw new ArgumentNullException(nameof(topology));
        _display = display ?? throw new ArgumentNullException(nameof(display));
        _profileStatus = profileStatus ?? throw new ArgumentNullException(nameof(profileStatus));
        _diagnosticEvent = diagnosticEvent;
    }

    public ManualDisplayMenuResult RefreshMenu()
    {
        var blocked = CurrentProfileBlockReason();
        DisplayReadResult<DisplayTopologySnapshot> result;
        try { result = _topology.GetSnapshot(); }
        catch (Exception ex) { return new(false, $"Die Monitor-Topologie konnte nicht gelesen werden: {ex.Message}", blocked, []); }
        if (!result.Success || result.Value is null) return new(false, result.Error?.Message ?? "Die Monitor-Topologie konnte nicht gelesen werden.", blocked, []);

        var snapshot = result.Value;
        var monitors = snapshot.Endpoints.OrderBy(DisplayEndpointLabel.Format, StringComparer.CurrentCultureIgnoreCase)
            .Select(endpoint => CreateMonitor(snapshot, endpoint)).ToArray();
        return new(true, monitors.Length == 0 ? "Windows meldet derzeit keinen aktiven Monitor." : null, blocked, monitors);
    }

    public string? CurrentProfileBlockReason() => ProfileBlockReason(_profileStatus());

    public ManualDisplaySwitchResult Switch(string monitorDevicePath, DisplayMode mode, string? monitorLabel = null)
    {
        ArgumentNullException.ThrowIfNull(mode);
        var blocked = ProfileBlockReason(_profileStatus());
        if (blocked is not null) return new(false, blocked);
        if (string.IsNullOrWhiteSpace(monitorDevicePath)) return new(false, "Der gewählte Monitor besitzt keinen stabilen Gerätepfad.");
        TargetedDisplayApplyResult result;
        try
        {
            result = _display.Apply([new DisplayProfileTarget(
                new SpecificMonitor(monitorDevicePath, "Manuelle Tray-Auswahl"),
                new DisplayMode { Width = mode.Width, Height = mode.Height, Frequency = mode.Frequency, Label = mode.Label })]);
        }
        catch (Exception ex)
        {
            var error = $"Der Anzeigemodus konnte nicht geändert werden: {ex.Message}";
            RecordSwitch(monitorLabel, monitorDevicePath, null, mode, false, error);
            return new(false, error);
        }

        var receipt = result.Receipts.SingleOrDefault();
        var resultError = result.Errors.FirstOrDefault()?.Message ?? "Der Anzeigemodus konnte nicht geändert werden.";
        RecordSwitch(
            monitorLabel,
            monitorDevicePath,
            receipt is null ? null : ToDisplayMode(receipt.OriginalMode),
            mode,
            result.Success,
            result.Success ? (receipt?.ToolChanged == true ? "angewendet" : "bereits aktiv") : resultError);
        return result.Success ? new(true, null) : new(false, resultError);
    }

    private void RecordSwitch(
        string? monitorLabel,
        string monitorDevicePath,
        DisplayMode? original,
        DisplayMode target,
        bool success,
        string result)
    {
        if (_diagnosticEvent is null) return;
        var monitor = string.IsNullOrWhiteSpace(monitorLabel) ? "Unbenannter Monitor" : monitorLabel.Trim();
        var originalText = original is null ? "nicht verfügbar" : DiagnosticReportFormatter.FormatMode(original);
        var outcome = success ? result : $"fehlgeschlagen: {result}";
        _diagnosticEvent(DateTime.UtcNow,
            $"Manueller Displaywechsel: {monitor}; Pfad {DiagnosticReportFormatter.FormatDevicePathShort(monitorDevicePath)}; Ausgang {originalText}; Ziel {DiagnosticReportFormatter.FormatMode(target)}; Ergebnis {outcome}.");
    }

    internal static string? ProfileBlockReason(ProfileMonitorStatus status) => status.State == ProfileMonitorState.Idle
        ? null
        : $"Manuelles Umschalten ist deaktiviert, solange die Profilüberwachung den Zustand „{DiagnosticReportFormatter.DisplayState(status.State)}“ hat.";

    private ManualMonitorMenu CreateMonitor(DisplayTopologySnapshot snapshot, DisplayEndpoint endpoint)
    {
        var currentMode = ToDisplayMode(endpoint.CurrentMode);
        var reason = EndpointBlockReason(snapshot, endpoint);
        if (reason is not null) return new(DisplayEndpointLabel.Format(endpoint), endpoint.IsPersistable ? endpoint.MonitorDevicePath : null, endpoint.IsPrimary, false, reason, currentMode, []);
        var modes = DisplayModeCatalog.Read(_topology, endpoint);
        if (!modes.Success) return new(DisplayEndpointLabel.Format(endpoint), endpoint.MonitorDevicePath, endpoint.IsPrimary, false, modes.Error, currentMode, []);
        return new(DisplayEndpointLabel.Format(endpoint), endpoint.MonitorDevicePath, endpoint.IsPrimary, modes.Modes.Count > 0,
            modes.Modes.Count == 0 ? "Für diesen Monitor wurden keine sicheren Modi gefunden." : null, currentMode, modes.Modes);
    }

    private static DisplayMode? ToDisplayMode(EndpointDisplayMode? mode) => mode is null ? null : new DisplayMode
    {
        Width = mode.Width,
        Height = mode.Height,
        Frequency = mode.Frequency,
        Label = $"{mode.Width}x{mode.Height} @ {mode.Frequency}Hz"
    };

    private static string? EndpointBlockReason(DisplayTopologySnapshot snapshot, DisplayEndpoint endpoint)
    {
        if (!endpoint.IsPersistable) return "Windows liefert für diesen Monitor keinen stabilen Gerätepfad.";
        if (endpoint.IsCloneSource) return "Klon-Gruppen werden aus Sicherheitsgründen nicht manuell geschaltet.";
        if (snapshot.Endpoints.Count(candidate => candidate.IsPersistable && string.Equals(candidate.MonitorDevicePath, endpoint.MonitorDevicePath, StringComparison.OrdinalIgnoreCase)) != 1) return "Der Monitor-Gerätepfad ist nicht eindeutig.";
        if (string.IsNullOrWhiteSpace(endpoint.GdiSourceName)) return "Der Monitor besitzt derzeit keine lesbare Anzeigequelle.";
        if (snapshot.Endpoints.Count(candidate => candidate.SourceIdentity == endpoint.SourceIdentity) != 1 || snapshot.Endpoints.Count(candidate => string.Equals(candidate.GdiSourceName, endpoint.GdiSourceName, StringComparison.OrdinalIgnoreCase)) != 1) return "Die Anzeigequelle dieses Monitors ist nicht eindeutig.";
        return null;
    }
}
