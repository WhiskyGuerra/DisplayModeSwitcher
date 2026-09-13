using System.Reflection;

namespace DisplayModeSwitcher;

public sealed record ProfileMonitorDiagnosticEvent(DateTime TimestampUtc, string Message);

/// <summary>Ein kleiner, begrenzter Speicher für Diagnoseereignisse.</summary>
public sealed class DiagnosticEventBuffer
{
    private readonly int _capacity;
    private readonly Queue<ProfileMonitorDiagnosticEvent> _events = new();
    private readonly object _sync = new();

    public DiagnosticEventBuffer(int capacity = 50)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public void Add(DateTime timestampUtc, string message)
    {
        lock (_sync)
        {
            while (_events.Count >= _capacity) _events.Dequeue();
            _events.Enqueue(new ProfileMonitorDiagnosticEvent(timestampUtc, message));
        }
    }

    public IReadOnlyList<ProfileMonitorDiagnosticEvent> Snapshot()
    {
        lock (_sync) return _events.ToArray();
    }
}

public sealed record DiagnosticReportData(
    DateTime CreatedUtc,
    string AppVersion,
    string WindowsVersion,
    string DotNetVersion,
    string Architecture,
    string CurrentDisplayMode,
    ProfileMonitorStatus MonitorStatus,
    IReadOnlyList<ProfileMonitorDiagnosticEvent> Events);

public static class DiagnosticReportFormatter
{
    public static string Format(DiagnosticReportData data)
    {
        var lines = new List<string>
        {
            "Display Mode Switcher – Diagnose",
            $"Erstellt (UTC): {data.CreatedUtc.ToUniversalTime():O}",
            $"App-Version: {data.AppVersion}",
            $"Windows: {data.WindowsVersion}",
            $".NET: {data.DotNetVersion}",
            $"Architektur: {data.Architecture}",
            $"Aktueller Anzeigemodus: {data.CurrentDisplayMode}",
            "",
            "Monitorstatus:",
            $"Zustand: {DisplayState(data.MonitorStatus.State)}",
            $"Meldung: {data.MonitorStatus.Message}",
            $"Profil: {data.MonitorStatus.ProfileProcess ?? "–"}",
            $"Startart: {FormatLaunchStrategy(data.MonitorStatus.LaunchStrategy, data.MonitorStatus.ExternallyDetected)}",
            $"Zielmodus: {FormatMode(data.MonitorStatus.TargetMode)}",
            $"Richtlinie: {FormatPolicy(data.MonitorStatus.Policy)}",
            $"Nachsetzungen: {FormatReapplyCount(data.MonitorStatus)}",
            $"Letzte Änderung (UTC): {FormatTime(data.MonitorStatus.LastChangedUtc)}",
            $"Letztes Ergebnis: {data.MonitorStatus.LastResult ?? "–"}",
            $"Wiederholungen: {data.MonitorStatus.RetryCount}",
            "",
            "Monitorziele (gezielte Profil-Engine):"
        };
        var targets = data.MonitorStatus.Targets;
        if (targets is null || targets.Count == 0)
            lines.Add("–");
        else
        {
            foreach (var target in targets)
            {
                lines.Add($"{target.Selector}: {FormatMode(target.Target)}; Pfad: {target.MonitorDevicePath ?? "noch nicht aufgelöst"}; Original: {FormatMode(target.Original)}; Tool geändert: {(target.ToolChanged ? "ja" : "nein")}");
                if (!string.IsNullOrWhiteSpace(target.ApplyError)) lines.Add($"  Apply-Fehler: {target.ApplyError}");
                if (!string.IsNullOrWhiteSpace(target.RestoreError)) lines.Add($"  Restore-Schuld: {target.RestoreError}");
            }
        }
        lines.Add("");
        lines.Add("Ereignisse (UTC):");
        if (data.Events.Count == 0) lines.Add("–");
        else lines.AddRange(data.Events.Select(item => $"{item.TimestampUtc:O}  {item.Message}"));
        return string.Join(Environment.NewLine, lines);
    }

    public static DiagnosticReportData Create(ProfileMonitorStatus status, IReadOnlyList<ProfileMonitorDiagnosticEvent> events, DisplayMode? currentMode)
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        return new DiagnosticReportData(
            DateTime.UtcNow,
            assembly.GetName().Version?.ToString() ?? "unbekannt",
            Environment.OSVersion.VersionString,
            Environment.Version.ToString(),
            System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            FormatMode(currentMode), status, events);
    }

    public static string FormatMode(DisplayMode? mode) => mode is null ? "Nicht verfügbar" : $"{mode.Width}x{mode.Height} @ {mode.Frequency}Hz";
    public static string DisplayState(ProfileMonitorState state) => state switch
    {
        ProfileMonitorState.Idle => "Wartet",
        ProfileMonitorState.Launching => "Start wird vorbereitet",
        ProfileMonitorState.PendingLaunch => "Wartet auf gestartete Anwendung",
        ProfileMonitorState.Active => "Aktiv",
        ProfileMonitorState.RetryPending => "Wiederholung ausstehend",
        ProfileMonitorState.Restoring => "Wiederherstellung",
        ProfileMonitorState.Ambiguous => "Mehrdeutig",
        ProfileMonitorState.Error => "Fehler",
        ProfileMonitorState.Stopped => "Gestoppt",
        _ => state.ToString()
    };

    public static string FormatLaunchStrategy(LaunchStrategy? strategy, bool externallyDetected = false) => externallyDetected
        ? "Extern erkannt"
        : strategy switch
    {
        LaunchStrategy.Steam => "Steam",
        LaunchStrategy.Direct => "Direkt",
        _ => "–"
    };

    public static string FormatPolicy(ProfileRetentionPolicy? policy) => policy is null
        ? "–"
        : ProfileRetentionPolicyText.ToDisplayName(policy.Value);

    private static string FormatReapplyCount(ProfileMonitorStatus status) => status.Policy switch
    {
        _ when status.State == ProfileMonitorState.PendingLaunch => $"{status.ReapplyCount}/unbegrenzt (Steam-Wartephase)",
        ProfileRetentionPolicy.Once => $"{status.ReapplyCount}/0",
        ProfileRetentionPolicy.Startup => $"{status.ReapplyCount}/3",
        ProfileRetentionPolicy.Continuous => $"{status.ReapplyCount}/unbegrenzt",
        _ => "–"
    };

    private static string FormatTime(DateTime? value) => value is null ? "–" : value.Value.ToUniversalTime().ToString("O");
}
