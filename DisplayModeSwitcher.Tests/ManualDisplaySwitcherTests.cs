using DisplayModeSwitcher;

public static class ManualDisplaySwitcherTests
{
    private const string PathA = @"\\?\DISPLAY#A";
    private const string PathB = @"\\?\DISPLAY#B";

    public static void AppliesOnlyExplicitSelectedPathAndKeepsMonitorsSeparate()
    {
        var topology = new FakeTopology(Endpoint(PathA, "Alpha", 1), Endpoint(PathB, "Beta", 2));
        var display = new FakeDisplay();
        var sut = new ManualDisplaySwitcher(topology, display, () => ProfileMonitorStatus.Idle);
        var menu = sut.RefreshMenu();
        Expect(menu.Success && menu.Monitors.Count == 2);
        Expect(sut.Switch(PathB, Mode(1280, 720, 60)).Success);
        Expect(sut.Switch(PathA, Mode(1920, 1080, 120)).Success);
        Expect(display.Applies.Count == 2);
        Expect(display.Applies[0].Count == 1 && ((SpecificMonitor)display.Applies[0][0].MonitorSelector).MonitorDevicePath == PathB);
        Expect(display.Applies[1].Count == 1 && ((SpecificMonitor)display.Applies[1][0].MonitorSelector).MonitorDevicePath == PathA);
    }

    public static void UnsafeEndpointsAndActiveProfilesAreBlocked()
    {
        var empty = Endpoint("", "Ohne Pfad", 1);
        var clone = Endpoint(PathA, "Clone", 2) with { IsCloneSource = true };
        var duplicate1 = Endpoint(PathB, "Doppelt 1", 3);
        var duplicate2 = Endpoint(PathB, "Doppelt 2", 4);
        var topology = new FakeTopology(empty, clone, duplicate1, duplicate2);
        var display = new FakeDisplay();
        var status = ProfileMonitorStatus.Idle;
        var sut = new ManualDisplaySwitcher(topology, display, () => status);
        var menu = sut.RefreshMenu();
        Expect(menu.Monitors.All(monitor => !monitor.Enabled && !string.IsNullOrWhiteSpace(monitor.DisabledReason)));
        Expect(topology.ModeReads.Count == 0);

        foreach (var state in new[] { ProfileMonitorState.Launching, ProfileMonitorState.Active, ProfileMonitorState.Restoring })
        {
            status = new ProfileMonitorStatus(state, "Profil besitzt den Displaywechsel");
            Expect(!sut.Switch(PathA, Mode(800, 600, 60)).Success && display.Applies.Count == 0);
        }
    }

    public static void LogicalModeDuplicatesAreCollapsed()
    {
        var endpoint = Endpoint(PathA, "Alpha", 1);
        var topology = new FakeTopology(endpoint);
        topology.Modes[1] = [Native(1920, 1080, 120), Native(1920, 1080, 120) with { BitsPerPixel = 24 }, Native(1920, 1080, 60)];
        var menu = new ManualDisplaySwitcher(topology, new FakeDisplay(), () => ProfileMonitorStatus.Idle).RefreshMenu();
        Expect(menu.Monitors.Single().Modes.Count == 2);
    }

    public static void ApplyErrorsArePropagated()
    {
        var display = new FakeDisplay { Error = "Der gewählte Monitor ist nicht mehr aktiv.", ErrorCode = TargetedDisplayErrorCode.MonitorMissing };
        var sut = new ManualDisplaySwitcher(new FakeTopology(Endpoint(PathA, "Alpha", 1)), display, () => ProfileMonitorStatus.Idle);
        var result = sut.Switch(PathA, Mode(640, 480, 60));
        Expect(!result.Success && result.Error!.Contains("nicht mehr aktiv"));
    }

    public static void ManualChangesProduceUsefulDiagnostics()
    {
        var events = new List<ProfileMonitorDiagnosticEvent>();
        var display = new FakeDisplay();
        var sut = new ManualDisplaySwitcher(
            new FakeTopology(Endpoint(PathA, "Alpha", 1)),
            display,
            () => ProfileMonitorStatus.Idle,
            (timestamp, message) => events.Add(new(timestamp, message)));

        Expect(sut.Switch(PathA, Mode(1280, 720, 60), "Alpha (Primär)").Success);

        var message = events.Single().Message;
        Expect(message.Contains("Alpha (Primär)", StringComparison.Ordinal));
        Expect(message.Contains("DISPLAY#A", StringComparison.Ordinal));
        Expect(message.Contains("Ausgang 1920x1080 @ 60Hz", StringComparison.Ordinal));
        Expect(message.Contains("Ziel 1280x720 @ 60Hz", StringComparison.Ordinal));
        Expect(message.Contains("Ergebnis angewendet", StringComparison.Ordinal));

        var failing = new ManualDisplaySwitcher(
            new FakeTopology(Endpoint(PathA, "Alpha", 1)),
            new FakeDisplay { Error = "Monitor getrennt" },
            () => ProfileMonitorStatus.Idle,
            (timestamp, diagnostic) => events.Add(new(timestamp, diagnostic)));
        Expect(!failing.Switch(PathA, Mode(800, 600, 60), "Alpha").Success);
        Expect(events.Last().Message.Contains("fehlgeschlagen: Monitor getrennt", StringComparison.Ordinal));
    }

    public static void WindowSelectionDefaultsToSafePrimaryMonitor()
    {
        var topology = new FakeTopology(Endpoint(PathA, "Alpha", 1), Endpoint(PathB, "Beta", 2));
        var menu = new ManualDisplaySwitcher(topology, new FakeDisplay(), () => ProfileMonitorStatus.Idle).RefreshMenu();
        var selection = new ManualDisplaySelection();

        selection.Load(menu);

        Expect(selection.SelectedMonitor?.MonitorDevicePath == PathA);
        Expect(selection.CanApply);
    }

    public static void UnsafeMissingOrAmbiguousPrimaryMonitorIsNotPreselected()
    {
        var cases = new[]
        {
            new FakeTopology(Endpoint("", "Unsicher", 1), Endpoint(PathB, "Beta", 2)),
            new FakeTopology(Endpoint(PathA, "Alpha", 1, isPrimary: false), Endpoint(PathB, "Beta", 2, isPrimary: false)),
            new FakeTopology(Endpoint(PathA, "Alpha", 1), Endpoint(PathB, "Beta", 2, isPrimary: true)),
            new FakeTopology(Endpoint("", "Unsicher", 1), Endpoint(PathB, "Beta", 2, isPrimary: true))
        };

        foreach (var topology in cases)
        {
            var menu = new ManualDisplaySwitcher(topology, new FakeDisplay(), () => ProfileMonitorStatus.Idle).RefreshMenu();
            var selection = new ManualDisplaySelection();
            selection.Load(menu);
            Expect(selection.SelectedMonitor is null);
            Expect(!selection.CanApply && selection.ApplyBlockReason!.Contains("Monitor"));
        }
    }

    public static void MonitorSelectionDefaultsToItsCurrentResolution()
    {
        var topology = new FakeTopology(Endpoint(PathA, "Alpha", 1), Endpoint(PathB, "Beta", 2, current: Native(1280, 720, 60)));
        var menu = new ManualDisplaySwitcher(topology, new FakeDisplay(), () => ProfileMonitorStatus.Idle).RefreshMenu();
        var selection = new ManualDisplaySelection();
        selection.Load(menu);

        selection.SelectMonitor(1);

        Expect(selection.SelectedResolution == new ManualResolution(1280, 720));
        Expect(selection.SelectedMode is not null && selection.SelectedMode.Width == 1280 && selection.SelectedMode.Height == 720);

        var unavailableCurrent = new FakeTopology(Endpoint(PathA, "Alpha", 1, current: Native(1024, 768, 60)));
        selection.Load(new ManualDisplaySwitcher(unavailableCurrent, new FakeDisplay(), () => ProfileMonitorStatus.Idle).RefreshMenu());
        Expect(selection.SelectedMonitor is not null && selection.SelectedResolution is null && selection.SelectedMode is null);
    }

    public static void ResolutionSelectionDefaultsToHighestFrequency()
    {
        var topology = new FakeTopology(Endpoint(PathA, "Alpha", 1));
        topology.Modes[1] = [Native(1920, 1080, 60), Native(1920, 1080, 100), Native(1920, 1080, 144)];
        var menu = new ManualDisplaySwitcher(topology, new FakeDisplay(), () => ProfileMonitorStatus.Idle).RefreshMenu();
        var selection = new ManualDisplaySelection();

        selection.Load(menu);

        Expect(selection.SelectedMode?.Frequency == 144);
        Expect(selection.CanApply);
    }

    public static void WindowSelectionTracksProfileBlockingWhileOpen()
    {
        var menu = new ManualDisplaySwitcher(new FakeTopology(Endpoint(PathA, "Alpha", 1)), new FakeDisplay(), () => ProfileMonitorStatus.Idle).RefreshMenu();
        var selection = new ManualDisplaySelection();
        selection.Load(menu);
        selection.SelectMonitor(0);
        selection.SelectResolution(0);
        selection.SelectFrequency(0);
        Expect(selection.CanApply);

        selection.UpdateProfileBlockReason(ManualDisplaySwitcher.ProfileBlockReason(new ProfileMonitorStatus(ProfileMonitorState.Active, "aktiv")));
        Expect(!selection.CanApply && selection.ApplyBlockReason!.Contains("Aktiv"));
        selection.UpdateProfileBlockReason(null);
        Expect(selection.CanApply);
    }

    public static void FreshTopologyClearsDisconnectedSelection()
    {
        var firstMenu = new ManualDisplaySwitcher(new FakeTopology(Endpoint(PathA, "Alpha", 1)), new FakeDisplay(), () => ProfileMonitorStatus.Idle).RefreshMenu();
        var emptyMenu = new ManualDisplaySwitcher(new FakeTopology(), new FakeDisplay(), () => ProfileMonitorStatus.Idle).RefreshMenu();
        var selection = new ManualDisplaySelection();
        selection.Load(firstMenu);
        selection.SelectMonitor(0);
        selection.SelectResolution(0);
        selection.SelectFrequency(0);
        var previousMode = selection.SelectedMode;

        selection.Load(emptyMenu, PathA, previousMode);

        Expect(selection.SelectedMonitor is null && !selection.CanApply);
        Expect(selection.ApplyBlockReason!.Contains("keinen aktiven Monitor"));
    }

    public static void RefreshCanRestoreOnlyAnExactExplicitSelection()
    {
        var topology = new FakeTopology(Endpoint(PathA, "Alpha", 1), Endpoint(PathB, "Beta", 2));
        topology.Modes[2] = [Native(1920, 1080, 60), Native(1280, 720, 60), Native(1280, 720, 75)];
        var menu = new ManualDisplaySwitcher(topology, new FakeDisplay(), () => ProfileMonitorStatus.Idle).RefreshMenu();
        var selection = new ManualDisplaySelection();
        var preferred = Mode(1280, 720, 60);

        selection.Load(menu, PathB.ToLowerInvariant(), preferred);

        Expect(selection.SelectedMonitor?.MonitorDevicePath == PathB);
        Expect(selection.SelectedMode is not null && selection.SelectedMode.Width == 1280 && selection.SelectedMode.Height == 720 && selection.SelectedMode.Frequency == 60);
        selection.Load(menu, PathB, Mode(1234, 567, 89));
        Expect(selection.SelectedMonitor?.MonitorDevicePath == PathB);
        Expect(selection.SelectedMode is not null && selection.SelectedMode.Width == 1920 && selection.SelectedMode.Height == 1080);
        Expect(selection.CanApply);
    }

    private static DisplayEndpoint Endpoint(string path, string name, uint source, bool? isPrimary = null, EndpointDisplayMode? current = null) => new(
        new DisplayTargetIdentity(new DisplayAdapterId(1), source), new DisplaySourceIdentity(new DisplayAdapterId(1), source),
        path, name, 0, 0, DisplayOutputTechnology.Hdmi, source, $@"\\.\DISPLAY{source}", isPrimary ?? source == 1,
        new DisplayPoint((int)(source - 1) * 1920, 0), current ?? Native(1920, 1080, 60), false);
    private static EndpointDisplayMode Native(uint width, uint height, uint hz) => new(width, height, hz, 32, 0, DisplayOrientation.Default, 0);
    private static DisplayMode Mode(uint width, uint height, uint hz) => new() { Width = width, Height = height, Frequency = hz, Label = $"{width}x{height} @ {hz}Hz" };
    private static void Expect(bool condition) { if (!condition) throw new InvalidOperationException("Erwartung nicht erfüllt."); }

    private sealed class FakeTopology : IDisplayTopologyService
    {
        private readonly DisplayTopologySnapshot _snapshot;
        public FakeTopology(params DisplayEndpoint[] endpoints)
        {
            _snapshot = new DisplayTopologySnapshot(endpoints);
            foreach (var endpoint in endpoints) Modes[endpoint.SourceIdentity.SourceId] = [Native(1920, 1080, 60), Native(1280, 720, 60)];
        }
        public Dictionary<uint, IReadOnlyList<EndpointDisplayMode>> Modes { get; } = [];
        public List<uint> ModeReads { get; } = [];
        public DisplayReadResult<DisplayTopologySnapshot> GetSnapshot() => DisplayReadResult<DisplayTopologySnapshot>.Ok(_snapshot);
        public DisplayReadResult<EndpointDisplayMode> GetCurrentMode(DisplayEndpoint endpoint) => DisplayReadResult<EndpointDisplayMode>.Ok(endpoint.CurrentMode!);
        public DisplayReadResult<IReadOnlyList<EndpointDisplayMode>> GetAvailableModes(DisplayEndpoint endpoint)
        {
            ModeReads.Add(endpoint.SourceIdentity.SourceId);
            return DisplayReadResult<IReadOnlyList<EndpointDisplayMode>>.Ok(Modes[endpoint.SourceIdentity.SourceId]);
        }
    }

    private sealed class FakeDisplay : ITargetedDisplayService
    {
        public List<IReadOnlyList<DisplayProfileTarget>> Applies { get; } = [];
        public string? Error { get; init; }
        public TargetedDisplayErrorCode ErrorCode { get; init; } = TargetedDisplayErrorCode.NativeChangeRejected;
        public TargetedDisplayApplyResult Apply(IReadOnlyList<DisplayProfileTarget> targets)
        {
            Applies.Add(targets);
            if (Error is not null)
                return new TargetedDisplayApplyResult(false, [], [new TargetedDisplayError(0, PathA, TargetedDisplayStage.Apply, ErrorCode, Error)], []);

            var target = targets.Single();
            var path = ((SpecificMonitor)target.MonitorSelector).MonitorDevicePath;
            return new TargetedDisplayApplyResult(true,
                [new TargetedDisplayReceipt(0, path, @"\\.\DISPLAY1", Native(1920, 1080, 60), Native(target.Mode.Width, target.Mode.Height, target.Mode.Frequency), true)],
                [], []);
        }
        public TargetedDisplayRestoreResult Restore(IReadOnlyList<TargetedDisplayReceipt> receipts) => throw new InvalidOperationException("Manuelles Schalten darf keinen Restore anfordern.");
    }
}
