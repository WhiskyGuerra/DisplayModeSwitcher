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
            $"Zielmodus: {FormatMode(data.MonitorStatus.TargetMode)}",
            $"Letzte Änderung (UTC): {FormatTime(data.MonitorStatus.LastChangedUtc)}",
            $"Letztes Ergebnis: {data.MonitorStatus.LastResult ?? "–"}",
            $"Wiederholungen: {data.MonitorStatus.RetryCount}",
            "",
            "Ereignisse (UTC):"
        };
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
        ProfileMonitorState.Active => "Aktiv",
        ProfileMonitorState.RetryPending => "Wiederholung ausstehend",
        ProfileMonitorState.Restoring => "Wiederherstellung",
        ProfileMonitorState.Ambiguous => "Mehrdeutig",
        ProfileMonitorState.Error => "Fehler",
        ProfileMonitorState.Stopped => "Gestoppt",
        _ => state.ToString()
    };

    private static string FormatTime(DateTime? value) => value is null ? "–" : value.Value.ToUniversalTime().ToString("O");
}
