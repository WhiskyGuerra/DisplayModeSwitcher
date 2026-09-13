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
    ("Startstrategie erkennt Steam nur mit passendem validen Manifest", SteamStrategyRequiresValidatedManifest),
    ("Verschachtelte oder doppelte Manifestfelder werden nicht vertraut", SteamManifestRejectsAmbiguousFields),
    ("Steam-Startinfo verwendet ausschließlich den offiziellen URI", SteamStartInfoUsesUriWithoutArguments),
    ("Installationsordner ist der letzte Namensfallback", InstallationFolderIsFallback),
    ("Windows-Hosts und eigene Instanzen werden gefiltert", WindowsHostsAndToolAreFiltered),
    ("Profilbearbeitung aktualisiert und verschiebt Profile", ProfileEditingUpdatesAndMovesProfile),
    ("Profilbearbeitung wechselt Schlüssel mit Rückrollschutz", ProfileEditingRollsBackOnSaveFailure),
    ("Profilentfernung stellt bei Speicherfehler vollständig zurück", ProfileRemovalRollsBackOnSaveFailure),
    ("Fehlende EXE und Legacy-Profile werden korrekt unterschieden", MissingExecutableDoesNotFlagLegacyProfile),
    ("Diagnosepuffer ist begrenzt und chronologisch", DiagnosticBufferIsBounded),
    ("Diagnosebericht formatiert Snapshot und Ereignisse", DiagnosticReportFormatsSnapshot),
    ("Profilmodus wird vor dem Anwendungsstart gesetzt", ProfileModeIsSetBeforeLaunch),
    ("Bereits aktiver Profilmodus wird nicht verändert", AlreadyActiveProfileModeIsNotChanged),
    ("Setzfehler verhindert den Anwendungsstart", SetFailurePreventsLaunch),
    ("Startfehler stellt den Originalmodus wieder her", LaunchFailureRestoresOriginal),
    ("Wiederherstellungsfehler nach Startfehler wird berichtet", LaunchFailureReportsRestoreFailure),
    ("Ungültige Prozessidentität startet keine unüberwachte Sitzung", InvalidLaunchedIdentityRestoresOriginal),
    ("Gestartete Prozessinstanz wird bis zum Ende überwacht", LaunchedProcessRestoresOriginalOnExit),
    ("Parallele Startaufrufe starten höchstens einmal", ParallelLaunchStartsOnce),
    ("Aktives Profil lehnt einen weiteren Start ab", ActiveProfileRejectsLaunch),
    ("Legacy- und fehlende Profile starten ohne Seiteneffekt nicht", InvalidLaunchProfilesHaveNoSideEffects),
    ("Steam-Start übernimmt nur den neuen exakten Zielprozess", SteamPendingAdoptsOnlyExactNewProcess),
    ("Steam-Start prüft einen Zielprozess noch an der Timeout-Grenze", SteamPendingChecksBoundaryBeforeTimeout),
    ("Steam-Shellprozess und alte oder falsche Prozesse werden ignoriert", SteamPendingIgnoresShellOldAndWrongProcesses),
    ("Steam-Wartephase hält den Zielmodus rate-limited", SteamPendingKeepsTargetRateLimited),
    ("Steam-Wartefenster ist strikt auf 120 Sekunden begrenzt", SteamPendingTimeoutIsBounded),
    ("Steam-Timeout und Stop stellen den Originalmodus wieder her", SteamTimeoutAndStopRestoreOriginal),
    ("Steam-Startfehler und dauerhafter Displayfehler rollen zurück", SteamFailuresRollBack),
    ("Steam-Restorefehler bleibt zur Wiederholung verwaltet", SteamRestoreFailureRemainsManaged),
    ("Parallele Steam-Starts lösen den URI höchstens einmal aus", ParallelSteamLaunchStartsOnce),
    ("Profilrichtlinien migrieren, speichern und validieren sicher", ProfilePoliciesPersistAndValidate),
    ("Einmalige Richtlinie setzt während der Laufzeit nicht nach", OncePolicyDoesNotReapply),
    ("Startphasenrichtlinie begrenzt Zeit, Anzahl und Diagnose", StartupPolicyIsBoundedAndQuiet),
    ("Dauerhafte Richtlinie setzt weiterhin rate-limitiert nach", ContinuousPolicyKeepsReapplying),
    ("Initiale Aktivierungsfehler werden für alle Richtlinien wiederholt", InitialFailuresRetryForEveryPolicy),
    ("Anzeigemodi verwenden eine explizite Ein-Hertz-Toleranz", DisplayModeEquivalenceUsesOneHertzTolerance),
    ("Wiederherstellung vermeidet tolerierbare Hertz-Wechsel", RestoreUsesFrequencyTolerance),
    ("Abweichungsdiagnose enthält Modus, Richtlinie und Nachsetzung", DeviationDiagnosticsAreComplete),
    ("Steam-Wartephase bleibt aktiv und startet danach die Profilrichtlinie", SteamPendingAndActivePolicyAreSeparated),
    ("Direktstart beginnt die Startphase beim erfolgreichen Start", DirectLaunchStartsPolicyWindowImmediately)
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
    fixture.Display.Current = Mode(fixture.Target.Width, fixture.Target.Height, fixture.Target.Frequency - 1);
    fixture.Monitor.Poll(fixture.Now.AddSeconds(1));
    fixture.Monitor.Poll(fixture.Now.AddSeconds(5));
    fixture.Monitor.Poll(fixture.Now.AddSeconds(10));
    Equal(1, fixture.Display.SetCalls.Count);
}

static void StopRestoresOriginal()
{
    foreach (var policy in Enum.GetValues<ProfileRetentionPolicy>())
    {
        var fixture = new MonitorFixture(policy);
        fixture.Monitor.Poll(fixture.Now);
        var result = fixture.Monitor.Stop();
        True(result.Success, result.Error);
        Equal(2, fixture.Display.SetCalls.Count);
        Equal(fixture.Original, fixture.Display.Current);
    }
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
    var profiles = new ConcurrentDictionary<string, DisplayProfile>(StringComparer.OrdinalIgnoreCase)
    {
        ["game.exe"] = Profile(Mode(1280, 720, 60))
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
    var profiles = new ConcurrentDictionary<string, DisplayProfile>(StringComparer.OrdinalIgnoreCase);
    var manager = new ProfileManager(profiles, new FakeDisplay(), new FakeProcesses());
    manager.Start();
    manager.Start();
    manager.Dispose();
    manager.Dispose();
}

static void DiagnosticBufferIsBounded()
{
    var buffer = new DiagnosticEventBuffer(2);
    var now = DateTime.UnixEpoch;
    buffer.Add(now, "erstes");
    buffer.Add(now.AddSeconds(1), "zweites");
    buffer.Add(now.AddSeconds(2), "drittes");
    var snapshot = buffer.Snapshot();
    Equal(2, snapshot.Count);
    Equal("zweites", snapshot[0].Message);
    Equal("drittes", snapshot[1].Message);
}

static void DiagnosticReportFormatsSnapshot()
{
    var status = new ProfileMonitorStatus(ProfileMonitorState.RetryPending, "Erneuter Versuch folgt.", @"C:\Games\game.exe", Mode(1280, 720, 60), DateTime.UnixEpoch, "Testfehler", 2, LaunchStrategy.Direct, ProfileRetentionPolicy.Startup, 1);
    var report = DiagnosticReportFormatter.Format(new DiagnosticReportData(DateTime.UnixEpoch, "1.2.3", "Windows Test", "8.0", "X64", "1920x1080 @ 60Hz", status,
        [new ProfileMonitorDiagnosticEvent(DateTime.UnixEpoch, "Profilmodus konnte nicht angewendet werden.")]));
    True(report.Contains("App-Version: 1.2.3"));
    True(report.Contains("Zustand: Wiederholung ausstehend"));
    True(report.Contains("Startart: Direkt"));
    True(report.Contains("Richtlinie: Während Startphase stabilisieren"));
    True(report.Contains("Nachsetzungen: 1/3"));
    True(report.Contains("Wiederholungen: 2"));
    True(report.Contains("Profilmodus konnte nicht angewendet werden."));
    Equal("Wartet auf gestartete Anwendung", DiagnosticReportFormatter.DisplayState(ProfileMonitorState.PendingLaunch));
}

static void ProfileModeIsSetBeforeLaunch()
{
    var order = new List<string>();
    var fixture = new LaunchFixture();
    fixture.Display.OnSet = _ => order.Add("set");
    fixture.Launcher.OnLaunch = _ => order.Add("launch");

    var result = fixture.Monitor.LaunchProfileApplication(fixture.Path, fixture.Now);

    True(result.Success, result.Error);
    Equal("set,launch", string.Join(',', order));
    Equal(fixture.Launcher.Identity, result.Process);
    True(result.DisplayModeChanged);
    True(fixture.Launcher.LastHandle?.Disposed == true);
}

static void AlreadyActiveProfileModeIsNotChanged()
{
    var fixture = new LaunchFixture();
    fixture.Display.Current = fixture.Target;

    var result = fixture.Monitor.LaunchProfileApplication(fixture.Path, fixture.Now);
    True(result.Success, result.Error);
    True(!result.DisplayModeChanged);
    Equal(0, fixture.Display.SetCalls.Count);

    fixture.Processes.Items = Array.Empty<ProcessIdentity>();
    fixture.Monitor.Poll(fixture.Now.AddSeconds(5));
    Equal(0, fixture.Display.SetCalls.Count);
}

static void SetFailurePreventsLaunch()
{
    var fixture = new LaunchFixture();
    fixture.Display.Results.Enqueue(OperationResult.Fail("Setzen kaputt"));

    var result = fixture.Monitor.LaunchProfileApplication(fixture.Path, fixture.Now);
    True(!result.Success);
    Equal("Setzen kaputt", result.DisplayError);
    Equal(0, fixture.Launcher.LaunchCount);

    fixture.Display.Results.Enqueue(OperationResult.Ok());
    True(fixture.Monitor.LaunchProfileApplication(fixture.Path, fixture.Now.AddSeconds(1)).Success);
}

static void LaunchFailureRestoresOriginal()
{
    var fixture = new LaunchFixture();
    fixture.Launcher.Error = "Start kaputt";

    var result = fixture.Monitor.LaunchProfileApplication(fixture.Path, fixture.Now);

    True(!result.Success);
    Equal("Start kaputt", result.StartError);
    True(result.RollbackError is null);
    Equal(2, fixture.Display.SetCalls.Count);
    Equal(fixture.Original, fixture.Display.Current);
}

static void LaunchFailureReportsRestoreFailure()
{
    var fixture = new LaunchFixture();
    fixture.Launcher.Error = "Start kaputt";
    fixture.Display.Results.Enqueue(OperationResult.Ok());
    fixture.Display.Results.Enqueue(OperationResult.Fail("Restore kaputt"));

    var result = fixture.Monitor.LaunchProfileApplication(fixture.Path, fixture.Now);

    True(!result.Success);
    Equal("Start kaputt", result.StartError);
    Equal("Restore kaputt", result.RollbackError);
    True(result.Error?.Contains("Restore kaputt", StringComparison.Ordinal) == true);
    Equal(ProfileMonitorState.Restoring, fixture.Monitor.Status.State);

    fixture.Display.Results.Enqueue(OperationResult.Ok());
    fixture.Monitor.Poll(fixture.Now.AddSeconds(5));
    Equal(fixture.Original, fixture.Display.Current);
    Equal(ProfileMonitorState.Idle, fixture.Monitor.Status.State);
}

static void InvalidLaunchedIdentityRestoresOriginal()
{
    var fixture = new LaunchFixture();
    fixture.Launcher.Identity = new ProcessIdentity(0, default, fixture.Path);

    var result = fixture.Monitor.LaunchProfileApplication(fixture.Path, fixture.Now);

    True(!result.Success);
    True(result.StartError?.Contains("eindeutig überwachbare Prozessinstanz", StringComparison.Ordinal) == true);
    Equal(fixture.Original, fixture.Display.Current);
    True(fixture.Launcher.LastHandle?.Disposed == true);
}

static void LaunchedProcessRestoresOriginalOnExit()
{
    var fixture = new LaunchFixture();
    var result = fixture.Monitor.LaunchProfileApplication(fixture.Path, fixture.Now);
    True(result.Success, result.Error);

    fixture.Processes.Items = [fixture.Launcher.Identity];
    fixture.Monitor.Poll(fixture.Now.AddSeconds(5));
    Equal(ProfileMonitorState.Active, fixture.Monitor.Status.State);

    fixture.Processes.Items = Array.Empty<ProcessIdentity>();
    fixture.Monitor.Poll(fixture.Now.AddSeconds(10));

    Equal(fixture.Original, fixture.Display.Current);
    Equal(2, fixture.Display.SetCalls.Count);
    Equal(ProfileMonitorState.Idle, fixture.Monitor.Status.State);
}

static void ParallelLaunchStartsOnce()
{
    var fixture = new LaunchFixture();
    var first = Task.Run(() => fixture.Monitor.LaunchProfileApplication(fixture.Path, fixture.Now));
    var second = Task.Run(() => fixture.Monitor.LaunchProfileApplication(fixture.Path, fixture.Now));
    Task.WaitAll(first, second);

    Equal(1, fixture.Launcher.LaunchCount);
    Equal(1, new[] { first.Result, second.Result }.Count(result => result.Success));
}

static void ActiveProfileRejectsLaunch()
{
    var fixture = new LaunchFixture();
    fixture.Processes.Items = [new ProcessIdentity(99, DateTime.UnixEpoch, fixture.Path)];
    fixture.Monitor.Poll(fixture.Now);

    var result = fixture.Monitor.LaunchProfileApplication(fixture.Path, fixture.Now.AddSeconds(1));
    True(!result.Success);
    Equal(0, fixture.Launcher.LaunchCount);
}

static void InvalidLaunchProfilesHaveNoSideEffects()
{
    var fixture = new LaunchFixture(fileExists: _ => false);
    fixture.Profiles["game.exe"] = Profile(fixture.Target);

    var legacy = fixture.Monitor.LaunchProfileApplication("game.exe", fixture.Now);
    var missing = fixture.Monitor.LaunchProfileApplication(fixture.Path, fixture.Now);

    True(!legacy.Success);
    True(!missing.Success);
    Equal(0, fixture.Display.SetCalls.Count);
    Equal(0, fixture.Launcher.LaunchCount);
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
        store.Profiles[@"C:\Games\game.exe"] = Profile(Mode(2560, 1440, 144));
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

    WithTemporaryDirectory(directory =>
    {
        var legacy = Path.Combine(directory, "legacy.json");
        var targetDirectory = Path.Combine(directory, "target");
        var target = Path.Combine(targetDirectory, "profiles.json");
        Directory.CreateDirectory(targetDirectory);
        File.WriteAllText(target, "[{\"Process\":\"existing.exe\",\"Width\":1920,\"Height\":1080,\"Frequency\":60}]");
        var store = new ProfileStore(target, legacy);
        File.Delete(target);
        Directory.Delete(targetDirectory);
        File.WriteAllText(targetDirectory, "blockiert den Zielordner");
        File.WriteAllText(legacy, "[{\"Process\":\"legacy.exe\",\"Width\":1280,\"Height\":720,\"Frequency\":60}]");

        True(!store.Load().Success);
        Equal(1, store.Profiles.Count);
        True(store.Profiles.ContainsKey("existing.exe"));
        True(!store.Profiles.ContainsKey("legacy.exe"));
        True(!store.Save().Success);
        True(File.Exists(legacy));
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

static void SteamStrategyRequiresValidatedManifest()
{
    WithTemporaryDirectory(directory =>
    {
        var steamApps = Path.Combine(directory, "steamapps");
        var executable = Path.Combine(steamApps, "common", "RideFolder", "Binaries", "Ride.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        var resolver = new SteamAwareLaunchPlanResolver();

        var validManifest = Path.Combine(steamApps, "appmanifest_123456.acf");
        File.WriteAllText(validManifest, "\"AppState\" { \"installdir\" \"ridefolder\" \"name\" \"Ride\" }");
        var steam = resolver.Resolve(executable);
        Equal(LaunchStrategy.Steam, steam.Strategy);
        Equal("123456", steam.SteamAppId);
        Equal("steam://run/123456", steam.SteamUri);
        Equal(ProcessMatcher.CanonicalizePath(executable), steam.ExpectedExecutablePath);

        File.WriteAllText(Path.Combine(steamApps, "appmanifest_654321.acf"), "\"AppState\" { \"installdir\" \"RideFolder\" }");
        Equal(LaunchStrategy.Direct, resolver.Resolve(executable).Strategy);
        File.Delete(Path.Combine(steamApps, "appmanifest_654321.acf"));

        File.WriteAllText(validManifest, "\"AppState\" { \"installdir\" \"OtherFolder\" }");
        Equal(LaunchStrategy.Direct, resolver.Resolve(executable).Strategy);

        File.WriteAllText(validManifest, "\"AppState\" { \"installdir\" \"RideFolder\"");
        Equal(LaunchStrategy.Direct, resolver.Resolve(executable).Strategy);

        File.Delete(validManifest);
        File.WriteAllText(Path.Combine(steamApps, "appmanifest_notnumeric.acf"), "\"AppState\" { \"installdir\" \"RideFolder\" }");
        Equal(LaunchStrategy.Direct, resolver.Resolve(executable).Strategy);

        var outside = Path.Combine(directory, "Games", "RideFolder", "Ride.exe");
        Equal(LaunchStrategy.Direct, resolver.Resolve(outside).Strategy);
    });
}

static void SteamStartInfoUsesUriWithoutArguments()
{
    var info = SteamLaunchInfo.Create("steam://run/123456");
    Equal("steam://run/123456", info.FileName);
    Equal(string.Empty, info.Arguments);
    True(info.ArgumentList.Count == 0);
    True(info.UseShellExecute);

    var rejected = false;
    try { SteamLaunchInfo.Create("steam://run/123 --unexpected"); }
    catch (ArgumentException) { rejected = true; }
    True(rejected);

    rejected = false;
    try { LaunchPlan.Steam(@"C:\Games\game.exe", "123/456"); }
    catch (ArgumentException) { rejected = true; }
    True(rejected);
}

static void SteamManifestRejectsAmbiguousFields()
{
    WithTemporaryDirectory(directory =>
    {
        var steamApps = Path.Combine(directory, "steamapps");
        var executable = Path.Combine(steamApps, "common", "RideFolder", "Ride.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        var manifest = Path.Combine(steamApps, "appmanifest_123456.acf");
        var resolver = new SteamAwareLaunchPlanResolver();

        File.WriteAllText(manifest, "\"AppState\" { \"UserConfig\" { \"installdir\" \"RideFolder\" } }");
        Equal(LaunchStrategy.Direct, resolver.Resolve(executable).Strategy);

        File.WriteAllText(manifest, "\"AppState\" { \"installdir\" \"RideFolder\" \"installdir\" \"OtherFolder\" }");
        Equal(LaunchStrategy.Direct, resolver.Resolve(executable).Strategy);
    });
}

static void SteamPendingAdoptsOnlyExactNewProcess()
{
    var fixture = new SteamLaunchFixture();
    var result = fixture.Monitor.LaunchProfileApplication(fixture.Path, fixture.Now);
    True(result.Success, result.Error);
    True(result.Process is null);
    Equal(ProfileMonitorState.PendingLaunch, fixture.Monitor.Status.State);
    Equal(0, fixture.Launcher.LaunchCount);
    Equal(1, fixture.Launcher.SteamLaunchCount);

    var target = new ProcessIdentity(77, fixture.Now.AddSeconds(1), fixture.Path);
    fixture.Processes.Items = [target];
    fixture.Monitor.Poll(fixture.Now.AddSeconds(1));
    Equal(ProfileMonitorState.Active, fixture.Monitor.Status.State);
    True(fixture.Monitor.Status.LastResult?.Contains("PID 77", StringComparison.Ordinal) == true);

    fixture.Processes.Items = Array.Empty<ProcessIdentity>();
    fixture.Monitor.Poll(fixture.Now.AddSeconds(6));
    Equal(fixture.Original, fixture.Display.Current);
}

static void SteamPendingChecksBoundaryBeforeTimeout()
{
    var fixture = new SteamLaunchFixture(pendingTimeout: TimeSpan.FromSeconds(10));
    True(fixture.Monitor.LaunchProfileApplication(fixture.Path, fixture.Now).Success);
    fixture.Processes.Items = [new ProcessIdentity(78, fixture.Now.AddSeconds(9), fixture.Path)];

    fixture.Monitor.Poll(fixture.Now.AddSeconds(10));

    Equal(ProfileMonitorState.Active, fixture.Monitor.Status.State);
    True(fixture.Monitor.Status.LastResult?.Contains("PID 78", StringComparison.Ordinal) == true);
}

static void SteamPendingIgnoresShellOldAndWrongProcesses()
{
    var fixture = new SteamLaunchFixture();
    True(fixture.Monitor.LaunchProfileApplication(fixture.Path, fixture.Now).Success);
    fixture.Processes.Items =
    [
        new ProcessIdentity(1, fixture.Now, ProcessMatcher.CanonicalizePath(@"C:\Program Files (x86)\Steam\steam.exe")),
        new ProcessIdentity(2, fixture.Now.AddSeconds(1), ProcessMatcher.CanonicalizePath(@"C:\Other\Ride.exe")),
        new ProcessIdentity(3, fixture.Now.AddMinutes(-1), fixture.Path)
    ];

    fixture.Monitor.Poll(fixture.Now.AddSeconds(1));
    Equal(ProfileMonitorState.PendingLaunch, fixture.Monitor.Status.State);
    True(fixture.Monitor.Status.LastResult?.Contains("Wartet", StringComparison.Ordinal) == true);
}

static void SteamPendingKeepsTargetRateLimited()
{
    var fixture = new SteamLaunchFixture();
    True(fixture.Monitor.LaunchProfileApplication(fixture.Path, fixture.Now).Success);
    Equal(1, fixture.Display.SetCalls.Count);
    fixture.Display.Current = fixture.Original;

    fixture.Monitor.Poll(fixture.Now);
    Equal(2, fixture.Display.SetCalls.Count);
    Equal(fixture.Target, fixture.Display.Current);
    Equal(1, fixture.Monitor.Status.ReapplyCount);
    True(fixture.Monitor.DiagnosticEvents.Any(item => item.Message.Contains("Nachsetzung 1/unbegrenzt", StringComparison.Ordinal)));
    fixture.Display.Current = fixture.Original;
    fixture.Monitor.Poll(fixture.Now.AddSeconds(1));
    Equal(2, fixture.Display.SetCalls.Count);
    fixture.Monitor.Poll(fixture.Now.AddSeconds(5));
    Equal(3, fixture.Display.SetCalls.Count);
}

static void SteamPendingTimeoutIsBounded()
{
    var rejected = false;
    try { _ = new SteamLaunchFixture(pendingTimeout: TimeSpan.FromSeconds(121)); }
    catch (ArgumentOutOfRangeException) { rejected = true; }
    True(rejected);
}

static void SteamTimeoutAndStopRestoreOriginal()
{
    var timeout = new SteamLaunchFixture(pendingTimeout: TimeSpan.FromSeconds(10));
    True(timeout.Monitor.LaunchProfileApplication(timeout.Path, timeout.Now).Success);
    timeout.Monitor.Poll(timeout.Now.AddSeconds(10));
    Equal(timeout.Original, timeout.Display.Current);
    Equal(ProfileMonitorState.Idle, timeout.Monitor.Status.State);
    True(timeout.Monitor.DiagnosticEvents.Any(item => item.Message.Contains("Zeitüberschreitung", StringComparison.Ordinal)));

    var stopped = new SteamLaunchFixture();
    True(stopped.Monitor.LaunchProfileApplication(stopped.Path, stopped.Now).Success);
    True(stopped.Monitor.Stop().Success);
    Equal(stopped.Original, stopped.Display.Current);
    Equal(ProfileMonitorState.Stopped, stopped.Monitor.Status.State);
}

static void SteamFailuresRollBack()
{
    var startFailure = new SteamLaunchFixture();
    startFailure.Launcher.SteamError = "URI kaputt";
    var startResult = startFailure.Monitor.LaunchProfileApplication(startFailure.Path, startFailure.Now);
    True(!startResult.Success);
    Equal("URI kaputt", startResult.StartError);
    Equal(startFailure.Original, startFailure.Display.Current);

    var displayFailure = new SteamLaunchFixture();
    True(displayFailure.Monitor.LaunchProfileApplication(displayFailure.Path, displayFailure.Now).Success);
    displayFailure.Display.Current = displayFailure.Original;
    displayFailure.Display.Results.Enqueue(OperationResult.Fail("Anzeige kaputt"));
    displayFailure.Display.Results.Enqueue(OperationResult.Fail("Anzeige kaputt"));
    displayFailure.Display.Results.Enqueue(OperationResult.Fail("Anzeige kaputt"));
    displayFailure.Monitor.Poll(displayFailure.Now);
    displayFailure.Monitor.Poll(displayFailure.Now.AddSeconds(5));
    displayFailure.Monitor.Poll(displayFailure.Now.AddSeconds(10));
    Equal(displayFailure.Original, displayFailure.Display.Current);
    Equal(ProfileMonitorState.Idle, displayFailure.Monitor.Status.State);
    True(displayFailure.Monitor.DiagnosticEvents.Any(item => item.Message.Contains("dauerhaft", StringComparison.Ordinal)));
}

static void SteamRestoreFailureRemainsManaged()
{
    var fixture = new SteamLaunchFixture(pendingTimeout: TimeSpan.FromSeconds(10));
    fixture.Display.Results.Enqueue(OperationResult.Ok());
    fixture.Display.Results.Enqueue(OperationResult.Fail("Restore kaputt"));
    True(fixture.Monitor.LaunchProfileApplication(fixture.Path, fixture.Now).Success);
    fixture.Monitor.Poll(fixture.Now.AddSeconds(10));
    Equal(ProfileMonitorState.Restoring, fixture.Monitor.Status.State);

    fixture.Display.Results.Enqueue(OperationResult.Ok());
    fixture.Monitor.Poll(fixture.Now.AddSeconds(15));
    Equal(fixture.Original, fixture.Display.Current);
    Equal(ProfileMonitorState.Idle, fixture.Monitor.Status.State);

    var stopped = new SteamLaunchFixture();
    stopped.Display.Results.Enqueue(OperationResult.Ok());
    stopped.Display.Results.Enqueue(OperationResult.Fail("Restore beim Stop kaputt"));
    True(stopped.Monitor.LaunchProfileApplication(stopped.Path, stopped.Now).Success);
    var beforeStop = DateTime.UtcNow;
    True(!stopped.Monitor.Stop().Success);
    Equal(ProfileMonitorState.Restoring, stopped.Monitor.Status.State);
    stopped.Display.Results.Enqueue(OperationResult.Ok());
    stopped.Monitor.Poll(beforeStop.AddSeconds(10));
    Equal(stopped.Original, stopped.Display.Current);
    Equal(ProfileMonitorState.Stopped, stopped.Monitor.Status.State);
}

static void ParallelSteamLaunchStartsOnce()
{
    var fixture = new SteamLaunchFixture();
    var first = Task.Run(() => fixture.Monitor.LaunchProfileApplication(fixture.Path, fixture.Now));
    var second = Task.Run(() => fixture.Monitor.LaunchProfileApplication(fixture.Path, fixture.Now));
    Task.WaitAll(first, second);
    Equal(1, fixture.Launcher.SteamLaunchCount);
    Equal(1, new[] { first.Result, second.Result }.Count(result => result.Success));
}

static void ProfilePoliciesPersistAndValidate()
{
    WithTemporaryDirectory(directory =>
    {
        var path = Path.Combine(directory, "profiles.json");
        File.WriteAllText(path, "[{\"Process\":\"legacy.exe\",\"Width\":1920,\"Height\":1080,\"Frequency\":60}]");
        var legacy = new ProfileStore(path);
        Equal(ProfileRetentionPolicy.Startup, legacy.Profiles["legacy.exe"].Policy);

        legacy.Profiles.Clear();
        foreach (var policy in Enum.GetValues<ProfileRetentionPolicy>())
            legacy.Profiles[$"{policy}.exe"] = Profile(Mode(1280, 720, 60), policy);
        True(legacy.Save().Success);

        var json = File.ReadAllText(path);
        foreach (var policy in Enum.GetValues<ProfileRetentionPolicy>())
            True(json.Contains($"\"Policy\": \"{policy}\"", StringComparison.Ordinal));

        var roundTrip = new ProfileStore(path);
        foreach (var policy in Enum.GetValues<ProfileRetentionPolicy>())
            Equal(policy, roundTrip.Profiles[$"{policy}.exe"].Policy);

        const string invalid = "[{\"Process\":\"game.exe\",\"Width\":1920,\"Height\":1080,\"Frequency\":60,\"Policy\":\"Forever\"}]";
        File.WriteAllText(path, invalid);
        True(!roundTrip.Load().Success);
        Equal(3, roundTrip.Profiles.Count);
        True(!roundTrip.Save().Success);
        Equal(invalid, File.ReadAllText(path));

        const string invalidNull = "[{\"Process\":\"game.exe\",\"Width\":1920,\"Height\":1080,\"Frequency\":60,\"Policy\":null}]";
        File.WriteAllText(path, invalidNull);
        True(!roundTrip.Load().Success);
        True(!roundTrip.Save().Success);
        Equal(invalidNull, File.ReadAllText(path));

        const string invalidEmpty = "[{\"Process\":\"game.exe\",\"Width\":1920,\"Height\":1080,\"Frequency\":60,\"Policy\":\"\"}]";
        File.WriteAllText(path, invalidEmpty);
        True(!roundTrip.Load().Success);
        Equal(3, roundTrip.Profiles.Count);
        True(!roundTrip.Save().Success);
        Equal(invalidEmpty, File.ReadAllText(path));

        const string invalidNumericName = "[{\"Process\":\"game.exe\",\"Width\":1920,\"Height\":1080,\"Frequency\":60,\"Policy\":\"1\"}]";
        File.WriteAllText(path, invalidNumericName);
        True(!roundTrip.Load().Success);
        Equal(3, roundTrip.Profiles.Count);
        True(!roundTrip.Save().Success);
        Equal(invalidNumericName, File.ReadAllText(path));
    });

    WithTemporaryDirectory(directory =>
    {
        var path = Path.Combine(directory, "profiles.json");
        var store = new ProfileStore(path);
        store.Profiles["game.exe"] = Profile(Mode(1920, 1080, 60), (ProfileRetentionPolicy)999);
        True(!store.Save().Success);
        True(!File.Exists(path));
    });
}

static void OncePolicyDoesNotReapply()
{
    var fixture = new MonitorFixture(ProfileRetentionPolicy.Once);
    fixture.Monitor.Poll(fixture.Now);
    Equal(1, fixture.Display.SetCalls.Count);

    fixture.Display.Current = fixture.Original;
    fixture.Monitor.Poll(fixture.Now.AddSeconds(5));
    fixture.Monitor.Poll(fixture.Now.AddSeconds(10));

    Equal(1, fixture.Display.SetCalls.Count);
    Equal(ProfileRetentionPolicy.Once, fixture.Monitor.Status.Policy);
    Equal(0, fixture.Monitor.Status.ReapplyCount);
    Equal(1, fixture.Monitor.DiagnosticEvents.Count(item => item.Message.Contains("Modusabweichung nicht nachgesetzt", StringComparison.Ordinal)));
}

static void StartupPolicyIsBoundedAndQuiet()
{
    var byCount = new MonitorFixture(ProfileRetentionPolicy.Startup);
    byCount.Monitor.Poll(byCount.Now);
    for (var attempt = 1; attempt <= 3; attempt++)
    {
        byCount.Display.Current = byCount.Original;
        byCount.Monitor.Poll(byCount.Now.AddSeconds(attempt * 5));
    }
    Equal(4, byCount.Display.SetCalls.Count);
    Equal(3, byCount.Monitor.Status.ReapplyCount);

    byCount.Display.Current = byCount.Original;
    byCount.Monitor.Poll(byCount.Now.AddSeconds(20));
    byCount.Monitor.Poll(byCount.Now.AddSeconds(25));
    byCount.Monitor.Poll(byCount.Now.AddSeconds(30));
    Equal(4, byCount.Display.SetCalls.Count);
    Equal(1, byCount.Monitor.DiagnosticEvents.Count(item => item.Message.Contains("Modusabweichung nicht nachgesetzt", StringComparison.Ordinal)));

    var byTime = new MonitorFixture(ProfileRetentionPolicy.Startup);
    byTime.Monitor.Poll(byTime.Now);
    byTime.Display.Current = byTime.Original;
    byTime.Monitor.Poll(byTime.Now.AddSeconds(30));
    Equal(1, byTime.Display.SetCalls.Count);
    True(byTime.Monitor.Status.LastResult?.Contains("30-Sekunden", StringComparison.Ordinal) == true);

    var justBeforeBoundary = new MonitorFixture(ProfileRetentionPolicy.Startup);
    justBeforeBoundary.Monitor.Poll(justBeforeBoundary.Now);
    justBeforeBoundary.Display.Current = justBeforeBoundary.Original;
    justBeforeBoundary.Monitor.Poll(justBeforeBoundary.Now.AddSeconds(30).AddTicks(-1));
    Equal(2, justBeforeBoundary.Display.SetCalls.Count);

    var failedReapplies = new MonitorFixture(ProfileRetentionPolicy.Startup);
    failedReapplies.Monitor.Poll(failedReapplies.Now);
    failedReapplies.Display.Current = failedReapplies.Original;
    failedReapplies.Display.Results.Enqueue(OperationResult.Fail("Fehler 1"));
    failedReapplies.Display.Results.Enqueue(OperationResult.Fail("Fehler 2"));
    failedReapplies.Display.Results.Enqueue(OperationResult.Fail("Fehler 3"));
    failedReapplies.Monitor.Poll(failedReapplies.Now.AddSeconds(5));
    failedReapplies.Monitor.Poll(failedReapplies.Now.AddSeconds(10));
    failedReapplies.Monitor.Poll(failedReapplies.Now.AddSeconds(15));
    failedReapplies.Monitor.Poll(failedReapplies.Now.AddSeconds(20));
    Equal(4, failedReapplies.Display.SetCalls.Count);
    Equal(3, failedReapplies.Monitor.Status.ReapplyCount);
    Equal(1, failedReapplies.Monitor.DiagnosticEvents.Count(item => item.Message.Contains("Modusabweichung nicht nachgesetzt", StringComparison.Ordinal)));
}

static void ContinuousPolicyKeepsReapplying()
{
    var fixture = new MonitorFixture(ProfileRetentionPolicy.Continuous);
    fixture.Monitor.Poll(fixture.Now);
    for (var attempt = 1; attempt <= 6; attempt++)
    {
        fixture.Display.Current = fixture.Original;
        fixture.Monitor.Poll(fixture.Now.AddSeconds(attempt * 5));
    }
    Equal(7, fixture.Display.SetCalls.Count);
    Equal(6, fixture.Monitor.Status.ReapplyCount);
    Equal(ProfileRetentionPolicy.Continuous, fixture.Monitor.Status.Policy);
}

static void InitialFailuresRetryForEveryPolicy()
{
    foreach (var policy in Enum.GetValues<ProfileRetentionPolicy>())
    {
        var fixture = new MonitorFixture(policy);
        fixture.Display.Results.Enqueue(OperationResult.Fail("erster Fehler"));
        fixture.Display.Results.Enqueue(OperationResult.Fail("zweiter Fehler"));
        fixture.Display.Results.Enqueue(OperationResult.Ok());

        fixture.Monitor.Poll(fixture.Now);
        fixture.Monitor.Poll(fixture.Now.AddSeconds(5));
        fixture.Monitor.Poll(fixture.Now.AddSeconds(10));

        Equal(3, fixture.Display.SetCalls.Count);
        Equal(ProfileMonitorState.Active, fixture.Monitor.Status.State);
        Equal(0, fixture.Monitor.Status.ReapplyCount);
    }
}

static void DisplayModeEquivalenceUsesOneHertzTolerance()
{
    True(DisplayModeEquivalence.AreEquivalent(Mode(3840, 1080, 100), Mode(3840, 1080, 99)));
    True(DisplayModeEquivalence.AreEquivalent(Mode(3840, 1080, 99), Mode(3840, 1080, 100)));
    True(!DisplayModeEquivalence.AreEquivalent(Mode(3840, 1080, 100), Mode(3840, 1080, 98)));
    True(!DisplayModeEquivalence.AreEquivalent(Mode(3840, 1080, 100), Mode(2560, 1080, 100)));
    True(!DisplayModeEquivalence.AreEquivalent(null, Mode(3840, 1080, 100)));
    True(DisplayModeEquivalence.AreEquivalent(Mode(1, 1, uint.MaxValue), Mode(1, 1, uint.MaxValue - 1)));
    True(!DisplayModeEquivalence.AreEquivalent(Mode(1, 1, uint.MaxValue), Mode(1, 1, 0)));
    True(!Mode(3840, 1080, 100).Equals(Mode(3840, 1080, 99)));
}

static void RestoreUsesFrequencyTolerance()
{
    var fixture = new MonitorFixture();
    fixture.Monitor.Poll(fixture.Now);
    fixture.Display.Current = Mode(fixture.Original.Width, fixture.Original.Height, fixture.Original.Frequency - 1);

    True(fixture.Monitor.Stop().Success);
    Equal(1, fixture.Display.SetCalls.Count);
}

static void DeviationDiagnosticsAreComplete()
{
    var fixture = new MonitorFixture(ProfileRetentionPolicy.Continuous);
    fixture.Monitor.Poll(fixture.Now);
    fixture.Display.Current = Mode(1024, 768, 75);
    fixture.Monitor.Poll(fixture.Now.AddSeconds(5));

    var entry = fixture.Monitor.DiagnosticEvents.Single(item => item.Message.Contains("Nachsetzung 1/unbegrenzt", StringComparison.Ordinal));
    True(entry.Message.Contains("beobachtet 1024x768 @ 75Hz", StringComparison.Ordinal));
    True(entry.Message.Contains("Ziel 1280x720 @ 60Hz", StringComparison.Ordinal));
    True(entry.Message.Contains("Richtlinie Dauerhaft erzwingen", StringComparison.Ordinal));
    Equal(1, fixture.Monitor.Status.ReapplyCount);
}

static void SteamPendingAndActivePolicyAreSeparated()
{
    var once = new SteamLaunchFixture(policy: ProfileRetentionPolicy.Once);
    True(once.Monitor.LaunchProfileApplication(once.Path, once.Now).Success);
    once.Display.Current = once.Original;
    once.Monitor.Poll(once.Now);
    Equal(2, once.Display.SetCalls.Count);

    var startup = new SteamLaunchFixture(policy: ProfileRetentionPolicy.Startup);
    True(startup.Monitor.LaunchProfileApplication(startup.Path, startup.Now).Success);
    startup.Display.Current = startup.Original;
    startup.Monitor.Poll(startup.Now);
    var process = new ProcessIdentity(77, startup.Now.AddSeconds(1), startup.Path);
    startup.Processes.Items = [process];
    startup.Monitor.Poll(startup.Now.AddSeconds(5));
    startup.Display.Current = startup.Original;
    startup.Monitor.Poll(startup.Now.AddSeconds(10));
    Equal(1, startup.Monitor.Status.ReapplyCount);

    startup.Display.Current = startup.Original;
    startup.Monitor.Poll(startup.Now.AddSeconds(35));
    Equal(3, startup.Display.SetCalls.Count);
    True(startup.Monitor.Status.LastResult?.Contains("30-Sekunden", StringComparison.Ordinal) == true);
}

static void DirectLaunchStartsPolicyWindowImmediately()
{
    var fixture = new LaunchFixture(policy: ProfileRetentionPolicy.Startup);
    True(fixture.Monitor.LaunchProfileApplication(fixture.Path, fixture.Now).Success);
    fixture.Processes.Items = [fixture.Launcher.Identity];
    fixture.Display.Current = fixture.Original;
    fixture.Monitor.Poll(fixture.Now.AddSeconds(30));
    Equal(1, fixture.Display.SetCalls.Count);
    True(fixture.Monitor.Status.LastResult?.Contains("30-Sekunden", StringComparison.Ordinal) == true);
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

static void ProfileEditingRollsBackOnSaveFailure()
{
    WithTemporaryDirectory(directory =>
    {
        var blockedDirectory = Path.Combine(directory, "blockiert");
        File.WriteAllText(blockedDirectory, "keine Verzeichnis");
        var store = new ProfileStore(Path.Combine(blockedDirectory, "profiles.json"));
        var originalKey = @"C:\Games\old.exe";
        var newKey = @"D:\Games\new.exe";
        var originalMode = Mode(1920, 1080, 60);
        var previousTarget = Profile(Mode(1024, 768, 75), ProfileRetentionPolicy.Startup);
        store.Profiles[originalKey] = Profile(originalMode, ProfileRetentionPolicy.Once);
        store.Profiles[newKey] = previousTarget;

        var result = ProfileEditWorkflow.Save(store, originalKey, newKey, Mode(2560, 1440, 144), ProfileRetentionPolicy.Continuous);

        True(!result.Success);
        Equal(2, store.Profiles.Count);
        True(store.Profiles.TryGetValue(originalKey, out var restored));
        Equal(Profile(originalMode, ProfileRetentionPolicy.Once), restored);
        Equal(previousTarget, store.Profiles[newKey]);
    });
}

static void ProfileEditingUpdatesAndMovesProfile()
{
    WithTemporaryDirectory(directory =>
    {
        var store = new ProfileStore(Path.Combine(directory, "profiles.json"));
        var originalKey = @"C:\Games\old.exe";
        var newKey = @"D:\Games\new.exe";
        store.Profiles[originalKey] = Profile(Mode(1920, 1080, 60), ProfileRetentionPolicy.Once);

        True(ProfileEditWorkflow.Save(store, originalKey, newKey, Mode(2560, 1440, 144), ProfileRetentionPolicy.Continuous).Success);
        Equal(1, store.Profiles.Count);
        True(!store.Profiles.ContainsKey(originalKey));
        True(store.Profiles.TryGetValue(newKey, out var saved));
        Equal(Profile(Mode(2560, 1440, 144), ProfileRetentionPolicy.Continuous), saved);

        True(ProfileEditWorkflow.Save(store, newKey, newKey, Mode(1920, 1080, 120), ProfileRetentionPolicy.Startup).Success);
        Equal(1, store.Profiles.Count);
        Equal(Profile(Mode(1920, 1080, 120), ProfileRetentionPolicy.Startup), store.Profiles[newKey]);
    });
}

static void ProfileRemovalRollsBackOnSaveFailure()
{
    WithTemporaryDirectory(directory =>
    {
        var blockedDirectory = Path.Combine(directory, "blockiert");
        File.WriteAllText(blockedDirectory, "kein Verzeichnis");
        var store = new ProfileStore(Path.Combine(blockedDirectory, "profiles.json"));
        var key = @"C:\Games\game.exe";
        var profile = Profile(Mode(2560, 1440, 144), ProfileRetentionPolicy.Continuous);
        store.Profiles[key] = profile;

        var result = ProfileEditWorkflow.Remove(store, key);

        True(!result.Success);
        Equal(1, store.Profiles.Count);
        True(ReferenceEquals(profile, store.Profiles[key]));
    });
}

static void MissingExecutableDoesNotFlagLegacyProfile()
{
    True(ProcessPresentation.IsMissingProfileExecutable(@"C:\nicht-vorhanden\game.exe", _ => false));
    True(!ProcessPresentation.IsMissingProfileExecutable("game.exe", _ => false));
}

static DisplayMode Mode(uint width, uint height, uint frequency) => new()
{
    Width = width,
    Height = height,
    Frequency = frequency,
    Label = $"{width}x{height} @ {frequency}Hz"
};

static DisplayProfile Profile(DisplayMode mode, ProfileRetentionPolicy policy = ProfileRetentionPolicy.Startup) => new(mode, policy);

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

    public MonitorFixture(ProfileRetentionPolicy policy = ProfileRetentionPolicy.Startup)
    {
        var path = ProcessMatcher.CanonicalizePath(@"C:\Games\game.exe");
        Display = new FakeDisplay { Current = Original };
        Processes = new FakeProcesses { Items = [new(10, DateTime.UnixEpoch, path)] };
        var profiles = new ConcurrentDictionary<string, DisplayProfile>(StringComparer.OrdinalIgnoreCase) { [path] = new DisplayProfile(Target, policy) };
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

sealed class LaunchFixture
{
    public DateTime Now { get; } = new(2026, 9, 13, 10, 0, 0, DateTimeKind.Utc);
    public string Path { get; } = ProcessMatcher.CanonicalizePath(@"C:\Games\game.exe");
    public DisplayMode Original { get; } = CreateMode(1920, 1080, 60);
    public DisplayMode Target { get; } = CreateMode(1280, 720, 60);
    public ConcurrentDictionary<string, DisplayProfile> Profiles { get; }
    public FakeDisplay Display { get; }
    public FakeProcesses Processes { get; } = new();
    public FakeLauncher Launcher { get; }
    public ProfileMonitor Monitor { get; }

    public LaunchFixture(Func<string, bool>? fileExists = null, ProfileRetentionPolicy policy = ProfileRetentionPolicy.Startup)
    {
        Display = new FakeDisplay { Current = Original };
        Launcher = new FakeLauncher(new ProcessIdentity(42, DateTime.UnixEpoch.AddHours(1), Path));
        Profiles = new ConcurrentDictionary<string, DisplayProfile>(StringComparer.OrdinalIgnoreCase) { [Path] = new DisplayProfile(Target, policy) };
        Monitor = new ProfileMonitor(
            Profiles,
            Display,
            Processes,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5),
            Launcher,
            fileExists ?? (_ => true));
    }

    private static DisplayMode CreateMode(uint width, uint height, uint frequency) => new()
    {
        Width = width,
        Height = height,
        Frequency = frequency,
        Label = $"{width}x{height} @ {frequency}Hz"
    };
}

sealed class SteamLaunchFixture
{
    public DateTime Now { get; } = new(2026, 9, 13, 10, 0, 0, DateTimeKind.Utc);
    public string Path { get; } = ProcessMatcher.CanonicalizePath(@"C:\SteamLibrary\steamapps\common\RideFolder\Ride.exe");
    public DisplayMode Original { get; } = CreateMode(1920, 1080, 60);
    public DisplayMode Target { get; } = CreateMode(1280, 720, 60);
    public FakeDisplay Display { get; }
    public FakeProcesses Processes { get; } = new();
    public FakeLauncher Launcher { get; }
    public ProfileMonitor Monitor { get; }

    public SteamLaunchFixture(TimeSpan? pendingTimeout = null, ProfileRetentionPolicy policy = ProfileRetentionPolicy.Startup)
    {
        Display = new FakeDisplay { Current = Original };
        Launcher = new FakeLauncher(new ProcessIdentity(900, Now, @"C:\Program Files (x86)\Steam\steam.exe"));
        var profiles = new ConcurrentDictionary<string, DisplayProfile>(StringComparer.OrdinalIgnoreCase) { [Path] = new DisplayProfile(Target, policy) };
        Monitor = new ProfileMonitor(
            profiles,
            Display,
            Processes,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5),
            Launcher,
            _ => true,
            new FakeLaunchPlanResolver(LaunchPlan.Steam(Path, "123456")),
            pendingTimeout ?? TimeSpan.FromSeconds(120));
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
    public Action<DisplayMode>? OnSet { get; set; }
    public DisplayMode? GetCurrentDisplayMode() => Current;
    public OperationResult SetDisplayMode(DisplayMode mode)
    {
        SetCalls.Add(mode);
        OnSet?.Invoke(mode);
        var result = Results.Count > 0 ? Results.Dequeue() : OperationResult.Ok();
        if (result.Success) Current = mode;
        return result;
    }
}

sealed class FakeLauncher(ProcessIdentity identity) : IApplicationLauncher
{
    public ProcessIdentity Identity { get; set; } = identity;
    public string? Error { get; set; }
    public int LaunchCount { get; private set; }
    public int SteamLaunchCount { get; private set; }
    public string? SteamError { get; set; }
    public string? LastSteamUri { get; private set; }
    public Action<string>? OnLaunch { get; set; }
    public FakeLaunchedApplication? LastHandle { get; private set; }

    public ILaunchedApplication Launch(string executablePath)
    {
        LaunchCount++;
        OnLaunch?.Invoke(executablePath);
        if (Error is not null) throw new InvalidOperationException(Error);
        return LastHandle = new FakeLaunchedApplication(Identity);
    }

    public void LaunchSteam(string steamUri)
    {
        SteamLaunchCount++;
        LastSteamUri = steamUri;
        if (SteamError is not null) throw new InvalidOperationException(SteamError);
    }
}

sealed class FakeLaunchPlanResolver(LaunchPlan plan) : ILaunchPlanResolver
{
    public LaunchPlan Resolve(string executablePath) => plan;
}

sealed class FakeLaunchedApplication(ProcessIdentity identity) : ILaunchedApplication
{
    public ProcessIdentity Identity { get; } = identity;
    public bool Disposed { get; private set; }
    public void Dispose() => Disposed = true;
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
