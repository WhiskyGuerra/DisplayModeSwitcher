namespace DisplayModeSwitcher;

/// <summary>Gemeinsame, UI-unabhängige Aufbereitung der Modi genau einer Anzeigequelle.</summary>
public static class DisplayModeCatalog
{
    public static ModeListResult Read(IDisplayTopologyService topology, DisplayEndpoint endpoint, DisplayMode? preservedMode = null)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(endpoint);
        try
        {
            var current = topology.GetCurrentMode(endpoint);
            if (!current.Success || current.Value is null || !IsValid(current.Value))
                return ModeListResult.Fail(current.Error?.Message ?? "Der aktuelle Anzeigemodus konnte nicht sicher gelesen werden.", preservedMode);
            var available = topology.GetAvailableModes(endpoint);
            if (!available.Success || available.Value is null)
                return ModeListResult.Fail(available.Error?.Message ?? "Die Anzeigemodi konnten nicht gelesen werden.", preservedMode);

            var modes = available.Value.Where(IsValid)
                .Where(mode => mode.Orientation == current.Value.Orientation).Distinct()
                .GroupBy(mode => (mode.Width, mode.Height, mode.Frequency))
                .Select(group => EndpointDisplayModeCandidateSelector.Select(group, current.Value))
                .OfType<EndpointDisplayMode>()
                .OrderByDescending(mode => mode.Width).ThenByDescending(mode => mode.Height).ThenByDescending(mode => mode.Frequency)
                .Select(ToDisplayMode).ToList();
            AddPreserved(modes, preservedMode);
            return ModeListResult.Ok(modes);
        }
        catch (Exception ex) { return ModeListResult.Fail($"Die Anzeigemodi konnten nicht gelesen werden: {ex.Message}", preservedMode); }
    }

    private static bool IsValid(EndpointDisplayMode mode) => mode.Width > 0 && mode.Height > 0 && mode.Frequency > 0 && mode.BitsPerPixel > 0;
    private static DisplayMode ToDisplayMode(EndpointDisplayMode mode) => new() { Width = mode.Width, Height = mode.Height, Frequency = mode.Frequency, Label = $"{mode.Width}x{mode.Height} @ {mode.Frequency}Hz" };
    private static void AddPreserved(List<DisplayMode> modes, DisplayMode? preserved)
    {
        if (preserved is null || modes.Any(candidate => candidate.Width == preserved.Width && candidate.Height == preserved.Height && candidate.Frequency == preserved.Frequency)) return;
        modes.Add(new DisplayMode { Width = preserved.Width, Height = preserved.Height, Frequency = preserved.Frequency, Label = $"{preserved.Width}x{preserved.Height} @ {preserved.Frequency}Hz (derzeit nicht verfügbar)" });
    }
}
