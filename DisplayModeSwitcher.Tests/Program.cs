using System.Collections.Concurrent;
using DisplayModeSwitcher;

var tests = new (string Name, Action Run)[]
{
    ("Fehlgeschlagener Profilversuch wird rate-limited wiederholt", FailedAttemptRetries),
    ("Spätere Modusabweichung wird erneut korrigiert", LaterDeviationReapplies),
    ("Aktiver Zielmodus wird nicht unnötig erneut gesetzt", ActiveTargetIsNotReapplied),
    ("Stop stellt einen vom Tool geänderten Originalmodus wieder her", StopRestoresOriginal),
    ("Stop verändert ohne erfolgreichen Tool-Wechsel keinen Modus", StopWithoutToolChangeDoesNotRestore),
    ("Pfad- und Legacy-Profile werden eindeutig zugeordnet", ProcessMatchingIsUnambiguous),
    ("Mehrdeutige Legacy-Profile schalten nicht", AmbiguousLegacyDoesNotSwitch),
    ("Wiederverwendete PID beendet die ursprüngliche Profilinstanz", ReusedPidRestoresOriginal),
    ("Manager-Lifecycle ist abbrechbar und idempotent", ManagerLifecycleIsCancellable),
    ("Autostart formatiert und verifiziert den Befehl", AutostartUsesVerifiedQuotedCommand),
    ("Profile speichern, laden und schützen beschädigte Daten", ProfilePersistenceHandlesErrors),
    ("Legacy-Profile werden ohne Löschen migriert", LegacyProfilesMigrateSafely),
    ("Prozessnamen verwenden verständliche Fallbacks", ProcessNamesUseFriendlyFallbacks),
    ("Prozessliste filtert, dedupliziert und sortiert", ProcessListFiltersDeduplicatesAndSorts),
    ("Generische Metadaten fallen auf Fenstertitel zurück", GenericMetadataFallsBackToWindowTitle),
    ("Steam-Manifeste liefern Spielnamen robust", SteamManifestNamesAreResolved),
    ("Installationsordner ist der letzte Namensfallback", InstallationFolderIsFallback),
    ("Windows-Hosts und eigene Instanzen werden gefiltert", WindowsHostsAndToolAreFiltered)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception ex) { failures.Add($"FAIL {test.Name}: {ex.Message}"); }
}

foreach (var failure in failures) Console.Error.WriteLine(failure);
Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} Tests erfolgreich.");
return failures.Count == 0 ? 0 : 1;

static void FailedAttemptRetries()
{
    var fixture = new MonitorFixture();
    fixture.Display.Results.Enqueue(OperationResult.Fail("simulierter Fehler"));
    fixture.Display.Results.Enqueue(OperationResult.Ok());

    fixture.Monitor.Poll(fixture.Now);
    Equal(1, fixture.Display.SetCalls.Count);
    Equal(ProfileMonitorState.RetryPending, fixture.Monitor.Status.State);
    fixture.Monitor.Poll(fixture.Now.AddSeconds(4));
    Equal(1, fixture.Display.SetCalls.Count);
    fixture.Monitor.Poll(fixture.Now.AddSeconds(5));
    Equal(2, fixture.Display.SetCalls.Count);
    Equal(ProfileMonitorState.Active, fixture.Monitor.Status.State);
}

static void LaterDeviationReapplies()
{
    var fixture = new MonitorFixture();
    fixture.Monitor.Poll(fixture.Now);
    Equal(1, fixture.Display.SetCalls.Count);
    fixture.Display.Current = fixture.Original;
    fixture.Monitor.Poll(fixture.Now.AddSeconds(4));
    Equal(1, fixture.Display.SetCalls.Count);
    fixture.Monitor.Poll(fixture.Now.AddSeconds(5));
    Equal(2, fixture.Display.SetCalls.Count);
    Equal(fixture.Target, fixture.Display.Current);
}

static void ActiveTargetIsNotReapplied()
{
    var fixture = new MonitorFixture();
    fixture.Monitor.Poll(fixture.Now);
    fixture.Monitor.Poll(fixture.Now.AddSeconds(1));
    fixture.Monitor.Poll(fixture.Now.AddSeconds(5));
    fixture.Monitor.Poll(fixture.Now.AddSeconds(10));
    Equal(1, fixture.Display.SetCalls.Count);
}

static void StopRestoresOriginal()
{
    var fixture = new MonitorFixture();
    fixture.Monitor.Poll(fixture.Now);
    var result = fixture.Monitor.Stop();
    True(result.Success, result.Error);
    Equal(2, fixture.Display.SetCalls.Count);
    Equal(fixture.Original, fixture.Display.Current);
}

static void StopWithoutToolChangeDoesNotRestore()
{
    var alreadyActive = new MonitorFixture();
    alreadyActive.Display.Current = alreadyActive.Target;
    alreadyActive.Monitor.Poll(alreadyActive.Now);
    True(alreadyActive.Monitor.Stop().Success);
    Equal(0, alreadyActive.Display.SetCalls.Count);

    var failed = new MonitorFixture();
    failed.Display.Results.Enqueue(OperationResult.Fail("simulierter Fehler"));
    failed.Monitor.Poll(failed.Now);
    True(failed.Monitor.Stop().Success);
    Equal(1, failed.Display.SetCalls.Count);
    Equal(failed.Original, failed.Display.Current);
}

static void ProcessMatchingIsUnambiguous()
{
    var process = new ProcessIdentity(42, DateTime.UnixEpoch, @"C:\Games\Helldivers 2\helldivers2.exe");
    Equal(1, ProcessMatcher.FindMatches(@"c:\games\HELLDIVERS 2\helldivers2.exe", [process]).Count);
    Equal(1, ProcessMatcher.FindMatches("helldivers2.exe", [process]).Count);
    Equal(0, ProcessMatcher.FindMatches("other.exe", [process]).Count);

    var sameNameElsewhere = new ProcessIdentity(43, DateTime.UnixEpoch, @"D:\Other\helldivers2.exe");
    var processes = new[] { process, sameNameElsewhere };
    Equal(1, ProcessMatcher.FindMatches(@"C:\Games\Helldivers 2\helldivers2.exe", processes).Count);
    Equal(2, ProcessMatcher.FindMatches("helldivers2.exe", processes).Count);
}

static void AmbiguousLegacyDoesNotSwitch()
{
    var display = new FakeDisplay { Current = Mode(1920, 1080, 60) };
    var processes = new FakeProcesses
    {
        Items =
        [
            new(1, DateTime.UnixEpoch, @"C:\One\game.exe"),
            new(2, DateTime.UnixEpoch.AddSeconds(1), @"D:\Two\game.exe")
        ]
    };
    var profiles = new ConcurrentDictionary<string, DisplayMode>(StringComparer.OrdinalIgnoreCase)
    {
        ["game.exe"] = Mode(1280, 720, 60)
    };
    var monitor = new ProfileMonitor(profiles, display, processes);
    monitor.Poll(DateTime.UtcNow);
    Equal(ProfileMonitorState.Ambiguous, monitor.Status.State);
    Equal(0, display.SetCalls.Count);
}

static void ReusedPidRestoresOriginal()
{
    var fixture = new MonitorFixture();
    fixture.Monitor.Poll(fixture.Now);
    fixture.Processes.Items =
    [
        new ProcessIdentity(10, DateTime.UnixEpoch.AddSeconds(1), ProcessMatcher.CanonicalizePath(@"C:\Games\game.exe"))
    ];

    fixture.Monitor.Poll(fixture.Now.AddSeconds(5));
    Equal(2, fixture.Display.SetCalls.Count);
    Equal(fixture.Original, fixture.Display.Current);
    Equal(ProfileMonitorState.Idle, fixture.Monitor.Status.State);
}

static void ManagerLifecycleIsCancellable()
{
    var profiles = new ConcurrentDictionary<string, DisplayMode>(StringComparer.OrdinalIgnoreCase);
    var manager = new ProfileManager(profiles, new FakeDisplay(), new FakeProcesses());
    manager.Start();
    manager.Start();
    manager.Dispose();
    manager.Dispose();
}

static void AutostartUsesVerifiedQuotedCommand()
{
    var registry = new FakeAutostartRegistry();
    var service = new AutostartService(@"C:\Program Files\DisplayModeSwitcher\DisplayModeSwitcher.exe", registry);
    True(service.Enable().Success);
    Equal("\"C:\\Program Files\\DisplayModeSwitcher\\DisplayModeSwitcher.exe\"", registry.Value);
    True(service.GetStatus().IsEnabled);
    registry.Value = "  \"c:\\program files\\DISPLAYMODESWITCHER\\DisplayModeSwitcher.exe\"  ";
    True(service.GetStatus().IsEnabled);
    registry.Value = "\"C:\\Program Files\\DisplayModeSwitcher\\DisplayModeSwitcher.exe\" --unexpected";
    True(!service.GetStatus().IsEnabled);
    registry.Value = @"C:\Program Files\DisplayModeSwitcher\DisplayModeSwitcher.exe";
    True(!service.GetStatus().IsEnabled);
    registry.Value = service.ExpectedCommand;
    True(service.Disable().Success);
    True(!service.GetStatus().IsEnabled);
}

static void ProfilePersistenceHandlesErrors()
{
    WithTemporaryDirectory(directory =>
    {
        var path = Path.Combine(directory, "profiles.json");
        var store = new ProfileStore(path);
        store.Profiles[@"C:\Games\game.exe"] = Mode(2560, 1440, 144);
        True(store.Save().Success);

        var loaded = new ProfileStore(path);
        Equal(1, loaded.Profiles.Count);
        File.WriteAllText(path, "not-json");
        True(!loaded.Load().Success);
        Equal(1, loaded.Profiles.Count);
        True(!loaded.Save().Success);
        Equal("not-json", File.ReadAllText(path));
    });

    WithTemporaryDirectory(directory =>
    {
        var path = Path.Combine(directory, "profiles.json");
        const string invalidMode = "[{\"Process\":\"game.exe\",\"Width\":0,\"Height\":1080,\"Frequency\":60}]";
        File.WriteAllText(path, invalidMode);
        var store = new ProfileStore(path);
        True(store.LastError is not null);
        Equal(0, store.Profiles.Count);
        True(!store.Save().Success);
        Equal(invalidMode, File.ReadAllText(path));
    });
}

static void LegacyProfilesMigrateSafely()
{
    WithTemporaryDirectory(directory =>
    {
        var legacy = Path.Combine(directory, "legacy.json");
        var target = Path.Combine(directory, "new", "profiles.json");
        File.WriteAllText(legacy, "[{\"Process\":\"game.exe\",\"Width\":1920,\"Height\":1080,\"Frequency\":60}]");
        var store = new ProfileStore(target, legacy);
        Equal(1, store.Profiles.Count);
        True(File.Exists(target));
        True(File.Exists(legacy));
        True(File.ReadAllText(target).Contains("\"Process\": \"game.exe\"", StringComparison.Ordinal));
    });

    WithTemporaryDirectory(directory =>
    {
        var legacy = Path.Combine(directory, "legacy.json");
        var target = Path.Combine(directory, "new", "profiles.json");
        File.WriteAllText(legacy, "not-json");
        var store = new ProfileStore(target, legacy);
        True(store.LastError is not null);
        True(!File.Exists(target));
        Equal("not-json", File.ReadAllText(legacy));
        True(!store.Save().Success);
    });
}

static void ProcessNamesUseFriendlyFallbacks()
{
    Equal("RV There Yet?", ProcessPresentation.GetFriendlyName("Ride.exe", "RV There Yet?", "Ride", "Fenster"));
    Equal("Beschreibung", ProcessPresentation.GetFriendlyName("Ride.exe", "Ride", "Beschreibung", "Fenster"));
    Equal("Mein Spiel", ProcessPresentation.GetFriendlyName("Ride.exe", null, null, "  Mein   Spiel  "));
    Equal("Ride.exe", ProcessPresentation.GetFriendlyName("Ride.exe", "Ride", "Ride.exe", "  "));

    var profile = ProcessPresentation.CreateProfileItem("game.exe");
    Equal("game.exe", profile.ToString());
}

static void ProcessListFiltersDeduplicatesAndSorts()
{
    var tool = ProcessMatcher.CanonicalizePath(@"C:\Tools\DisplayModeSwitcher.exe");
    var game = ProcessMatcher.CanonicalizePath(@"C:\Games\Ride.exe");
    var browser = ProcessMatcher.CanonicalizePath(@"C:\Apps\Browser.exe");
    var background = ProcessMatcher.CanonicalizePath(@"C:\Apps\Helper.exe");
    var processes = new[]
    {
        new ProcessDisplayInfo(new(4, DateTime.UnixEpoch, tool), null, null, "Switcher", 1),
        new ProcessDisplayInfo(new(3, DateTime.UnixEpoch, game), "RV There Yet?", null, "Ride", 1),
        new ProcessDisplayInfo(new(2, DateTime.UnixEpoch, game), "RV There Yet?", null, "Ride", 1),
        new ProcessDisplayInfo(new(5, DateTime.UnixEpoch, browser), null, "Acme Browser", "Browser", 1),
        new ProcessDisplayInfo(new(6, DateTime.UnixEpoch, background), "Helper", null, null, 0)
    };

    var defaultItems = ProcessPresentation.CreateItems(processes, tool, includeBackgroundProcesses: false);
    Equal(2, defaultItems.Count);
    Equal("Acme Browser", defaultItems[0].DisplayName);
    Equal("RV There Yet?", defaultItems[1].DisplayName);
    Equal(game, defaultItems[1].Path);

    var allItems = ProcessPresentation.CreateItems(processes, tool, includeBackgroundProcesses: true);
    Equal(3, allItems.Count);
    True(allItems.Any(item => item.Path == background));
}

static void GenericMetadataFallsBackToWindowTitle()
{
    Equal("RV There Yet?", ProcessPresentation.GetFriendlyName("Ride.exe", "BootstrapPackagedGame", ".NET", "RV There Yet?"));
}

static void SteamManifestNamesAreResolved()
{
    WithTemporaryDirectory(directory =>
    {
        var steamApps = Path.Combine(directory, "steamapps");
        var gameDirectory = Path.Combine(steamApps, "common", "RideFolder", "Binaries", "Win64");
        Directory.CreateDirectory(gameDirectory);
        var executable = Path.Combine(gameDirectory, "Ride.exe");
        File.WriteAllText(Path.Combine(steamApps, "appmanifest_123.acf"), "\"AppState\" { \"installdir\" \"ridefolder\" \"name\" \"RV There Yet?\" }");
        Equal("RV There Yet?", ProcessPresentation.FindSteamGameName(executable));
        File.WriteAllText(Path.Combine(steamApps, "appmanifest_456.acf"), "unlesbar \"name\"");
        Equal("RV There Yet?", ProcessPresentation.FindSteamGameName(executable));
    });
}

static void InstallationFolderIsFallback()
{
    Equal("My Game", ProcessPresentation.GetFriendlyName("game.exe", "BootstrapPackagedGame", ".NET", null, null, "My Game"));
    Equal("game.exe", ProcessPresentation.GetFriendlyName("game.exe", null, null, null, null, "bin"));
}

static void WindowsHostsAndToolAreFiltered()
{
    var root = Path.Combine(Path.GetTempPath(), "dms-process-filter");
    var windows = Path.Combine(root, "Windows");
    var app = Path.Combine(root, "Apps", "game.exe");
    var toolElsewhere = Path.Combine(root, "Other", "DisplayModeSwitcher.exe");
    var host = Path.Combine(windows, "System32", "host.exe");
    var items = new[]
    {
        new ProcessDisplayInfo(new(1, DateTime.UnixEpoch, host), null, null, "Windows Host", 1),
        new ProcessDisplayInfo(new(2, DateTime.UnixEpoch, toolElsewhere), null, null, "Tool", 1),
        new ProcessDisplayInfo(new(3, DateTime.UnixEpoch, app), "Game", null, "Game", 1)
    };
    var normal = ProcessPresentation.CreateItems(items, Path.Combine(root, "DisplayModeSwitcher.exe"), false, windows);
    Equal(1, normal.Count);
    var all = ProcessPresentation.CreateItems(items, Path.Combine(root, "DisplayModeSwitcher.exe"), true, windows);
    Equal(2, all.Count);
    True(all.Any(item => item.Path == host));
    True(!all.Any(item => item.Path == toolElsewhere));
}

static DisplayMode Mode(uint width, uint height, uint frequency) => new()
{
    Width = width,
    Height = height,
    Frequency = frequency,
    Label = $"{width}x{height} @ {frequency}Hz"
};

static void WithTemporaryDirectory(Action<string> action)
{
    var path = Path.Combine(Path.GetTempPath(), "DisplayModeSwitcher.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    try { action(path); }
    finally { Directory.Delete(path, recursive: true); }
}

static void True(bool condition, string? message = null)
{
    if (!condition) throw new InvalidOperationException(message ?? "Bedingung war nicht erfüllt.");
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Erwartet: {expected}; tatsächlich: {actual}");
}

sealed class MonitorFixture
{
    public DateTime Now { get; } = new(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);
    public DisplayMode Original { get; } = CreateMode(1920, 1080, 60);
    public DisplayMode Target { get; } = CreateMode(1280, 720, 60);
    public FakeDisplay Display { get; }
    public FakeProcesses Processes { get; }
    public ProfileMonitor Monitor { get; }

    public MonitorFixture()
    {
        var path = ProcessMatcher.CanonicalizePath(@"C:\Games\game.exe");
        Display = new FakeDisplay { Current = Original };
        Processes = new FakeProcesses { Items = [new(10, DateTime.UnixEpoch, path)] };
        var profiles = new ConcurrentDictionary<string, DisplayMode>(StringComparer.OrdinalIgnoreCase) { [path] = Target };
        Monitor = new ProfileMonitor(profiles, Display, Processes, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    private static DisplayMode CreateMode(uint width, uint height, uint frequency) => new()
    {
        Width = width,
        Height = height,
        Frequency = frequency,
        Label = $"{width}x{height} @ {frequency}Hz"
    };
}

sealed class FakeDisplay : IDisplayService
{
    public DisplayMode? Current { get; set; }
    public Queue<OperationResult> Results { get; } = new();
    public List<DisplayMode> SetCalls { get; } = new();
    public DisplayMode? GetCurrentDisplayMode() => Current;
    public OperationResult SetDisplayMode(DisplayMode mode)
    {
        SetCalls.Add(mode);
        var result = Results.Count > 0 ? Results.Dequeue() : OperationResult.Ok();
        if (result.Success) Current = mode;
        return result;
    }
}

sealed class FakeProcesses : IProcessProvider
{
    public IReadOnlyList<ProcessIdentity> Items { get; set; } = Array.Empty<ProcessIdentity>();
    public IReadOnlyList<ProcessIdentity> GetCurrentSessionProcesses() => Items;
}

sealed class FakeAutostartRegistry : IAutostartRegistry
{
    public string? Value { get; set; }
    public OperationResult Read(out string? value) { value = Value; return OperationResult.Ok(); }
    public OperationResult Write(string value) { Value = value; return OperationResult.Ok(); }
    public OperationResult Delete() { Value = null; return OperationResult.Ok(); }
}
