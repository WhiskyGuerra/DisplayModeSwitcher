namespace DisplayModeSwitcher;

public sealed record ManualResolution(uint Width, uint Height)
{
    public string Label => $"{Width}x{Height}";
}

/// <summary>UI-unabhängiger Zustand der ausdrücklich dreistufigen manuellen Auswahl.</summary>
public sealed class ManualDisplaySelection
{
    private IReadOnlyList<ManualMonitorMenu> _monitors = [];
    private ManualMonitorMenu? _monitor;
    private ManualResolution? _resolution;
    private DisplayMode? _mode;

    public IReadOnlyList<ManualMonitorMenu> Monitors => _monitors;
    public ManualMonitorMenu? SelectedMonitor => _monitor;
    public IReadOnlyList<ManualResolution> Resolutions { get; private set; } = [];
    public IReadOnlyList<DisplayMode> Frequencies { get; private set; } = [];
    public ManualResolution? SelectedResolution => _resolution;
    public DisplayMode? SelectedMode => _mode;
    public string? LoadError { get; private set; }
    public string? ProfileBlockReason { get; private set; }
    public bool CanApply => ApplyBlockReason is null;

    public string? ApplyBlockReason
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(LoadError)) return LoadError;
            if (!string.IsNullOrWhiteSpace(ProfileBlockReason)) return ProfileBlockReason;
            if (_monitor is null) return "Bitte zuerst einen Monitor auswählen.";
            if (!_monitor.Enabled || string.IsNullOrWhiteSpace(_monitor.MonitorDevicePath))
                return _monitor.DisabledReason ?? "Dieser Monitor kann nicht sicher umgeschaltet werden.";
            if (_resolution is null) return "Bitte eine Zielauflösung auswählen.";
            if (_mode is null) return "Bitte eine Bildwiederholrate auswählen.";
            return null;
        }
    }

    public void Load(ManualDisplayMenuResult result, string? preferredMonitorPath = null, DisplayMode? preferredMode = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        _monitors = result.Monitors;
        LoadError = result.Success ? result.Error : result.Error ?? "Die Monitordaten konnten nicht geladen werden.";
        ProfileBlockReason = result.ProfileBlockReason;
        ClearSelection();

        var monitorIndex = string.IsNullOrWhiteSpace(preferredMonitorPath)
            ? IndexOfSafePrimaryMonitor()
            : IndexOfMonitor(preferredMonitorPath);
        if (monitorIndex < 0 || !_monitors[monitorIndex].Enabled)
        {
            monitorIndex = IndexOfSafePrimaryMonitor();
            if (monitorIndex < 0) return;
        }

        SelectMonitor(monitorIndex);
        if (preferredMode is null) return;
        var resolutionIndex = Resolutions.ToList().FindIndex(item => item.Width == preferredMode.Width && item.Height == preferredMode.Height);
        if (resolutionIndex < 0) return;
        if (!_monitor!.Modes.Any(item => item.Width == preferredMode.Width && item.Height == preferredMode.Height && item.Frequency == preferredMode.Frequency)) return;
        SelectResolution(resolutionIndex);
        var frequencyIndex = Frequencies.ToList().FindIndex(item => item.Frequency == preferredMode.Frequency);
        if (frequencyIndex >= 0) SelectFrequency(frequencyIndex);
    }

    public void UpdateProfileBlockReason(string? reason) => ProfileBlockReason = reason;

    public void SelectMonitor(int index)
    {
        _monitor = index >= 0 && index < _monitors.Count ? _monitors[index] : null;
        _resolution = null;
        _mode = null;
        Frequencies = [];
        Resolutions = _monitor is { Enabled: true }
            ? _monitor.Modes.GroupBy(mode => (mode.Width, mode.Height))
                .OrderByDescending(group => group.Key.Width).ThenByDescending(group => group.Key.Height)
                .Select(group => new ManualResolution(group.Key.Width, group.Key.Height)).ToArray()
            : [];

        if (_monitor?.CurrentMode is not { } currentMode) return;
        var currentResolutionIndex = Resolutions.ToList().FindIndex(item =>
            item.Width == currentMode.Width && item.Height == currentMode.Height);
        if (currentResolutionIndex >= 0) SelectResolution(currentResolutionIndex);
    }

    public void SelectResolution(int index)
    {
        _resolution = index >= 0 && index < Resolutions.Count ? Resolutions[index] : null;
        _mode = null;
        Frequencies = _monitor is not null && _resolution is not null
            ? _monitor.Modes.Where(mode => mode.Width == _resolution.Width && mode.Height == _resolution.Height)
                .OrderByDescending(mode => mode.Frequency).ToArray()
            : [];
        if (Frequencies.Count > 0) SelectFrequency(0);
    }

    public void SelectFrequency(int index) => _mode = index >= 0 && index < Frequencies.Count ? Frequencies[index] : null;

    private int IndexOfMonitor(string path)
    {
        for (var i = 0; i < _monitors.Count; i++)
            if (string.Equals(_monitors[i].MonitorDevicePath, path, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private int IndexOfSafePrimaryMonitor()
    {
        var matches = _monitors.Select((monitor, index) => (monitor, index))
            .Where(item => item.monitor.IsPrimary)
            .ToArray();
        return matches.Length == 1 && matches[0].monitor.Enabled ? matches[0].index : -1;
    }

    private void ClearSelection()
    {
        _monitor = null;
        _resolution = null;
        _mode = null;
        Resolutions = [];
        Frequencies = [];
    }
}
