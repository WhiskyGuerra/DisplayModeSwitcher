using DisplayModeSwitcher;

public static class ProfileTargetEditorTests
{
    private const string PathA = @"\\?\DISPLAY#ACME0001#A#{00000000-0000-0000-0000-000000000000}";
    private const string PathB = @"\\?\DISPLAY#ACME0002#B#{00000000-0000-0000-0000-000000000000}";

    public static void NewDraftHasNoImplicitTarget()
    {
        var fake = Fake(Endpoint(PathA, "Alpha", true));
        var editor = new ProfileTargetEditor(fake);
        Expect(editor.RefreshTopology().Success);
        Expect(editor.Choices.Count == 2 && editor.Choices[0].Selector is PrimaryMonitor);
        Expect(editor.Targets.Count == 0 && !editor.CanSave && fake.ModeReads.Count == 0);
    }

    public static void AddsSpecificAndMultipleTargetsWithSnapshots()
    {
        var fake = Fake(Endpoint(PathA, "Alpha", true, manufacturer: 0x1234, product: 0xABCD), Endpoint(PathB, "Beta", false, source: 2));
        var editor = Ready(fake);
        Expect(editor.Add(Specific(editor, PathA), Mode(120)).Success);
        Expect(editor.Add(Specific(editor, PathB), Mode(144)).Success);
        var selectors = editor.Targets.Select(target => target.MonitorSelector).Cast<SpecificMonitor>().ToArray();
        Expect(selectors[0].MonitorDevicePath == PathA && selectors[0].FriendlyNameSnapshot == "Alpha");
        Expect(selectors[0].EdidManufacturerId == "1234" && selectors[0].EdidProductCodeId == "ABCD");
        Expect(editor.CanSave && editor.Targets.Count == 2);
    }

    public static void DuplicateSelectorsAndPhysicalTargetsAreBlocked()
    {
        var fake = Fake(Endpoint(PathA, "Alpha", true));
        var editor = Ready(fake);
        Expect(editor.Add(editor.Choices[0], Mode(120)).Success);
        Expect(!editor.Add(editor.Choices[0], Mode(120)).Success);
        var result = editor.Add(Specific(editor, PathA), Mode(120));
        Expect(!result.Success && result.Error!.Contains("physischen", StringComparison.OrdinalIgnoreCase));
    }

    public static void ModesComeOnlyFromSelectedSourceAndAreDeduplicated()
    {
        var a = Endpoint(PathA, "Alpha", true, source: 1);
        var b = Endpoint(PathB, "Beta", false, source: 2);
        var fake = Fake(a, b);
        fake.Modes[2] = [EndpointMode(144), EndpointMode(144) with { BitsPerPixel = 24 }, EndpointMode(120)];
        var editor = Ready(fake);
        var result = editor.GetModes(Specific(editor, PathB));
        Expect(result.Success && result.Modes.Count == 2);
        Expect(fake.ModeReads.SequenceEqual([2u]));
        Expect(fake.CurrentModeReads.SequenceEqual([2u]));
    }

    public static void LoadingAndReadingNeverMutatesStoredTargets()
    {
        var profile = Profile(new SpecificMonitor(PathA, "Alter Name", "1111", "2222"), Mode(77));
        var editor = Ready(Fake(Endpoint(PathA, "Neuer Windows-Name", true)));
        editor.Load(profile);
        _ = editor.GetTargetStates();
        _ = editor.GetModes(Specific(editor, PathA), profile.Targets[0].Mode);
        var stored = (SpecificMonitor)profile.Targets[0].MonitorSelector;
        Expect(stored.FriendlyNameSnapshot == "Alter Name" && stored.EdidManufacturerId == "1111");
        Expect(!editor.IsDirty);
    }

    public static void MissingAndAmbiguousTargetsArePreservedFailSafe()
    {
        var missing = new ProfileTargetEditor(Fake()); missing.RefreshTopology(); missing.Load(Profile(new SpecificMonitor(PathA, "Alpha"), Mode(120)));
        Expect(missing.Targets.Count == 1 && missing.CanSave && missing.HasBlockingResolution && missing.GetTargetStates()[0].Label.Contains("nicht verbunden"));

        var duplicate = Fake(Endpoint(PathA, "Alpha 1", true, source: 1), Endpoint(PathA, "Alpha 2", false, source: 2));
        var ambiguous = Ready(duplicate); ambiguous.Load(Profile(new SpecificMonitor(PathA, "Alpha"), Mode(120)));
        Expect(ambiguous.Targets.Count == 1 && ambiguous.HasBlockingResolution && !ambiguous.CanSave);
    }

    public static void RebindRequiresConfirmationAndDoesNotAutoMatch()
    {
        var fake = Fake(Endpoint(PathB, "Neuer Monitor", true));
        var editor = Ready(fake); editor.Load(Profile(new SpecificMonitor(PathA, "Gleicher Name"), Mode(120)));
        var choice = Specific(editor, PathB);
        Expect(!editor.Update(0, choice, Mode(120)).Success);
        Expect(((SpecificMonitor)editor.Targets[0].MonitorSelector).MonitorDevicePath == PathA);
        var cancelled = editor.Rebind(0, choice, false);
        Expect(cancelled.Cancelled && ((SpecificMonitor)editor.Targets[0].MonitorSelector).MonitorDevicePath == PathA);
        var rebound = editor.Rebind(0, choice, true);
        Expect(rebound.Success && ((SpecificMonitor)editor.Targets[0].MonitorSelector).MonitorDevicePath == PathB);
    }

    public static void RebindRequiresAValidNewMode()
    {
        var endpoint = Endpoint(PathB, "Beta", true);
        var fake = Fake(endpoint); fake.Modes[endpoint.SourceIdentity.SourceId] = [EndpointMode(144)];
        var editor = Ready(fake); editor.Load(Profile(new SpecificMonitor(PathA, "Alpha"), Mode(120)));
        var choice = Specific(editor, PathB);
        var required = editor.Rebind(0, choice, true);
        Expect(required.ModeRequired && ((SpecificMonitor)editor.Targets[0].MonitorSelector).MonitorDevicePath == PathA);
        Expect(editor.Rebind(0, choice, true, Mode(144)).Success);
    }

    public static void UnavailableStoredModeRemainsVisible()
    {
        var fake = Fake(Endpoint(PathA, "Alpha", true)); fake.Modes[1] = [EndpointMode(144)];
        var editor = Ready(fake);
        var result = editor.GetModes(Specific(editor, PathA), Mode(75));
        Expect(result.Modes.Any(mode => mode.Frequency == 75 && mode.Label.Contains("nicht verfügbar")));
    }

    public static void GdiReorderDoesNotChangeSpecificChoice()
    {
        var fake = Fake(Endpoint(PathA, "Alpha", true, source: 1, gdi: @"\\.\DISPLAY1"));
        var editor = Ready(fake); var oldChoice = Specific(editor, PathA);
        fake.Snapshot = new DisplayTopologySnapshot([Endpoint(PathA, "Alpha", false, source: 7, gdi: @"\\.\DISPLAY9")]);
        Expect(editor.RefreshTopology().Success);
        var newChoice = editor.FindChoice(oldChoice.Selector);
        Expect(newChoice?.Endpoint?.MonitorDevicePath == PathA && newChoice.Endpoint.GdiSourceName == @"\\.\DISPLAY9");
    }

    public static void CloneAndUnpersistableEndpointsCannotBeChosen()
    {
        var clone = Endpoint(PathA, "Clone", true) with { IsCloneSource = true };
        var unpersistable = Endpoint("", "No path", false, source: 2);
        var editor = Ready(Fake(clone, unpersistable));
        Expect(editor.Choices.Count == 1 && editor.Choices[0].Selector is PrimaryMonitor);
        Expect(!editor.GetModes(editor.Choices[0]).Success);
    }

    public static void UnsafeSourcesAndAmbiguousNativeModesCannotBeChosen()
    {
        var duplicateGdi = Fake(
            Endpoint(PathA, "Alpha", true, source: 1, gdi: @"\\.\DISPLAY1"),
            Endpoint(PathB, "Beta", false, source: 2, gdi: @"\\.\DISPLAY1"));
        var unsafeEditor = Ready(duplicateGdi);
        Expect(unsafeEditor.Choices.Count == 1);
        Expect(!unsafeEditor.GetModes(unsafeEditor.Choices[0]).Success);

        var endpoint = Endpoint(PathA, "Alpha", true);
        var ambiguous = Fake(endpoint);
        ambiguous.Modes[1] = [EndpointMode(144), EndpointMode(144) with { FixedOutput = 1 }];
        var modeEditor = Ready(ambiguous);
        var result = modeEditor.GetModes(Specific(modeEditor, PathA));
        Expect(result.Success && result.Modes.All(mode => mode.Frequency != 144));
        Expect(!modeEditor.Add(Specific(modeEditor, PathA), Mode(144)).Success);
    }

    public static void DirtyStateTracksDeepTargetChanges()
    {
        var editor = Ready(Fake(Endpoint(PathA, "Alpha", true)));
        editor.Load(Profile(new SpecificMonitor(PathA, "Alpha"), Mode(120)));
        Expect(!editor.IsDirty);
        Expect(editor.Update(0, Specific(editor, PathA), Mode(144)).Success && editor.IsDirty);
    }

    private static ProfileTargetEditor Ready(FakeTopology fake) { var editor = new ProfileTargetEditor(fake); Expect(editor.RefreshTopology().Success); return editor; }
    private static MonitorChoice Specific(ProfileTargetEditor editor, string path) => editor.Choices.Single(choice => choice.Endpoint is not null && choice.Endpoint.MonitorDevicePath == path);
    private static DisplayProfile Profile(MonitorSelector selector, DisplayMode mode) => new(ProfileRetentionPolicy.Startup, [new DisplayProfileTarget(selector, mode)]);
    private static DisplayMode Mode(uint hz) => new() { Width = 2560, Height = 1440, Frequency = hz, Label = $"2560x1440 @ {hz}Hz" };
    private static EndpointDisplayMode EndpointMode(uint hz) => new(2560, 1440, hz, 32, 0, DisplayOrientation.Default, 0);
    private static DisplayEndpoint Endpoint(string path, string name, bool primary, uint source = 1, ushort manufacturer = 0, ushort product = 0, string? gdi = null) => new(
        new(new DisplayAdapterId(1), source), new(new DisplayAdapterId(1), source), path, name, manufacturer, product,
        DisplayOutputTechnology.DisplayPortExternal, source, gdi ?? $@"\\.\DISPLAY{source}", primary, new(0, 0), EndpointMode(120), false);
    private static FakeTopology Fake(params DisplayEndpoint[] endpoints) => new(new DisplayTopologySnapshot(endpoints));
    private static void Expect(bool condition) { if (!condition) throw new InvalidOperationException("Erwartung nicht erfüllt."); }

    private sealed class FakeTopology(DisplayTopologySnapshot snapshot) : IDisplayTopologyService
    {
        public DisplayTopologySnapshot Snapshot { get; set; } = snapshot;
        public Dictionary<uint, IReadOnlyList<EndpointDisplayMode>> Modes { get; } = new();
        public List<uint> ModeReads { get; } = [];
        public List<uint> CurrentModeReads { get; } = [];
        public DisplayReadResult<DisplayTopologySnapshot> GetSnapshot() => DisplayReadResult<DisplayTopologySnapshot>.Ok(Snapshot);
        public DisplayReadResult<EndpointDisplayMode> GetCurrentMode(DisplayEndpoint endpoint)
        {
            CurrentModeReads.Add(endpoint.SourceIdentity.SourceId);
            return DisplayReadResult<EndpointDisplayMode>.Ok(endpoint.CurrentMode!);
        }
        public DisplayReadResult<IReadOnlyList<EndpointDisplayMode>> GetAvailableModes(DisplayEndpoint endpoint)
        {
            ModeReads.Add(endpoint.SourceIdentity.SourceId);
            return DisplayReadResult<IReadOnlyList<EndpointDisplayMode>>.Ok(Modes.TryGetValue(endpoint.SourceIdentity.SourceId, out var modes) ? modes : [EndpointMode(120), EndpointMode(144)]);
        }
    }
}
