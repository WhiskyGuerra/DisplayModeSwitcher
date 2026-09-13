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
    ("V1-Profile werden als explizites Primärmonitor-Ziel migriert", V1ProfilesMigrateToPrimaryTarget),
    ("Profile kopieren Monitorziele und Anzeigemodi tief", DisplayProfilesAreDeeplyImmutable),
    ("V2-Profile schreiben Primär-, Specific- und Mehrmonitorziele verlustfrei", V2ProfilesRoundTripAllTargetKinds),
    ("Ungültige und unbekannte V2-Daten sperren Laden und Überschreiben", InvalidV2ProfilesAreBlockedTransactionally),
    ("Ungültige In-Memory-Ziele überschreiben keine gültige Datei", InvalidInMemoryTargetsDoNotOverwrite),
    ("Mehrmonitor-Workflows rollen vollständige Ziellisten tief zurück", MultiTargetWorkflowRollsBackDeeply),
    ("Legacy-Monitor lässt nicht unterstützte Ziele ohne Seiteneffekt", UnsupportedTargetsHaveNoRuntimeSideEffects),
    ("Die Übergangsoberfläche reduziert keine nicht unterstützten Ziele", CurrentUiWorkflowPreservesUnsupportedTargets),
    ("Monitoridentitäten vergleichen Gerätepfade ohne Großschreibung", MonitorSelectorIdentityIsCaseInsensitive),
    ("Einmalige Richtlinie setzt während der Laufzeit nicht nach", OncePolicyDoesNotReapply),
    ("Startphasenrichtlinie begrenzt Zeit, Anzahl und Diagnose", StartupPolicyIsBoundedAndQuiet),
    ("Dauerhafte Richtlinie setzt weiterhin rate-limitiert nach", ContinuousPolicyKeepsReapplying),
    ("Initiale Aktivierungsfehler werden für alle Richtlinien wiederholt", InitialFailuresRetryForEveryPolicy),
    ("Anzeigemodi verwenden eine explizite Ein-Hertz-Toleranz", DisplayModeEquivalenceUsesOneHertzTolerance),
    ("Wiederherstellung vermeidet tolerierbare Hertz-Wechsel", RestoreUsesFrequencyTolerance),
    ("Abweichungsdiagnose enthält Modus, Richtlinie und Nachsetzung", DeviationDiagnosticsAreComplete),
    ("Steam-Wartephase bleibt aktiv und startet danach die Profilrichtlinie", SteamPendingAndActivePolicyAreSeparated),
    ("Direktstart beginnt die Startphase beim erfolgreichen Start", DirectLaunchStartsPolicyWindowImmediately),
    ("Topologie ordnet physische Monitore ihren aktuellen Quellen zu", TopologyMapsPhysicalTargetsToSources),
    ("Topologieabfrage wiederholt nach unzureichendem Puffer vollständig", TopologyRetriesWholeQueryAfterInsufficientBuffer),
    ("Specific-Matching verwendet ausschließlich den stabilen Gerätepfad", SpecificMatchingUsesOnlyPersistentPath),
    ("Fehlende und doppelte Gerätepfade werden fail-safe behandelt", MissingAndDuplicatePathsFailSafe),
    ("Primärmonitor muss genau einmal und ohne Klon-Gruppe vorkommen", PrimaryMatchingIsFailSafe),
    ("GDI-Neuordnung wird mit jedem Snapshot frisch ermittelt", GdiReorderUsesFreshSnapshot),
    ("Monitor ohne Gerätepfad bleibt sichtbar aber nicht persistierbar", EmptyDevicePathIsVisibleButUnpersistable),
    ("Klon-Quellen blockieren Primary- und Specific-Auflösung", CloneSourcesAreRejected),
    ("Virtuelle Klon-Gruppen ohne Quellmodus werden blockiert", VirtualCloneGroupsAreRejected),
    ("Monitorbeschriftungen verwenden Windows-Namen und ehrliche Fallbacks", EndpointLabelsUseWindowsNamesAndFallbacks),
    ("Moduslisten lesen nur die Quelle des gewählten Monitors", AvailableModesReadOnlySelectedSource),
    ("Moduslisten deduplizieren die vollständige Modusidentität", AvailableModesDeduplicateFullIdentity),
    ("Native Display-Strukturen entsprechen den Win32-Größen und Offsets", NativeDisplayStructLayoutsMatchWin32),
    ("Topologiesnapshots sind unveränderlich und führen keine Schreibaktion aus", TopologySnapshotsAreImmutableAndReadOnly),
    ("Gezieltes Setzen beschreibt ausschließlich ausgewählte Quellen", TargetedDisplayServiceTests.OnlySelectedSourcesAreWritten),
    ("Mehrmonitor-Apply bindet den Primärmonitor an seinen Gerätepfad", TargetedDisplayServiceTests.MultiTargetAndPrimaryAreBound),
    ("Unsichere Monitor- und Modusauflösungen blockieren vor jedem Apply", TargetedDisplayServiceTests.UnsafePreflightCasesNeverApply),
    ("Alle nativen Tests laufen vor dem ersten temporären Apply", TargetedDisplayServiceTests.EveryTestPrecedesFirstApply),
    ("Reihenfolgeänderungen bleiben erlaubt, Quellenänderungen blockieren", TargetedDisplayServiceTests.TopologyReorderIsAcceptedButSourceChangeAborts),
    ("Teilfehler rollen ausgewählte Monitore in umgekehrter Reihenfolge zurück", TargetedDisplayServiceTests.PartialFailureRollsBackInReverseOrder),
    ("Fehlgeschlagener Rollback bleibt als wiederholbare Restore-Schuld erhalten", TargetedDisplayServiceTests.RollbackFailureRemainsDebt),
    ("Restore folgt dem Gerätepfad und behält Disconnect-Schulden", TargetedDisplayServiceTests.RestoreUsesPathAfterGdiReorderAndKeepsDisconnectDebt),
    ("Ein-Hertz-Toleranz entscheidet nur über die Notwendigkeit eines Writes", TargetedDisplayServiceTests.FrequencyToleranceControlsOnlyWhetherToWrite),
    ("Native Moduskandidaten werden über aktuelle BPP und Flags aufgelöst", TargetedDisplayServiceTests.CandidateUsesCurrentBppAndFlags),
    ("Native Modusdubletten mit gleichen wirksamen Feldern werden zusammengefasst", TargetedDisplayServiceTests.CandidateCollapsesEquivalentNativeDuplicates),
    ("Native Moduskandidaten bevorzugen die aktuelle Farbtiefe", TargetedDisplayServiceTests.CandidatePrefersCurrentBppWithoutExactFlagMatch),
    ("Native Zielaufrufe erlauben nur sichere Felder und zwei Flag-Arten", TargetedDisplayServiceTests.NativeContractHasOnlyAllowedFieldsAndKinds),
    ("Alle nativen Rückgabecodes einschließlich Restart sind strukturiert", TargetedDisplayServiceTests.NativeReturnCodesAreCompleteAndRestartFailsApply),
    ("Target-Profilengine aktiviert Primär-, Specific- und Mehrfachziele", TargetedProfileMonitorTests.InitialBatchesAreExact),
    ("Target-Profilengine wiederholt sichere Initialfehler mit Abstand", TargetedProfileMonitorTests.InitialFailureRetriesWithoutLatch),
    ("Target-Profilengine drosselt kontrollierte Apply-Wiederholungen", TargetedProfileMonitorTests.ControlledApplyFailureIsRateLimited),
    ("Target-Profilengine sperrt bei Rollback-Schuld Apply und Start", TargetedProfileMonitorTests.RollbackDebtBlocksFurtherWork),
    ("Target-Profilengine stellt nur noch offene Zielschulden wieder her", TargetedProfileMonitorTests.RestoreDebtRetriesOnlyRemainingTarget),
    ("Target-Profilengine bewahrt Initialbelege bei Nachsetzungen", TargetedProfileMonitorTests.ReapplyPoliciesAndInitialReceipts),
    ("Target-Profilengine zählt No-op-Prüfungen nicht als Startup-Nachsetzungen", TargetedProfileMonitorTests.StartupNoOpChecksPreserveReapplyBudget),
    ("Target-Profilengine zählt fehlgeschlagene Startup-Korrekturen weiterhin", TargetedProfileMonitorTests.FailedStartupCorrectionsStillConsumeBudget),
    ("Target-Profilengine bindet Nachsetzungen an initiale Gerätepfade", TargetedProfileMonitorTests.ReapplyRemainsBoundAndOwnsLateChanges),
    ("Target-Profilengine restauriert nach Teilrollback den Initialbeleg", TargetedProfileMonitorTests.ReapplyDebtThenRestoresInitialReceipt),
    ("Target-Profilengine startet Direct erst nach Ziel-Batch", TargetedProfileMonitorTests.DirectLaunchSafetyAndRestore),
    ("Target-Profilengine serialisiert parallele kontrollierte Starts", TargetedProfileMonitorTests.ParallelLaunchStartsOnce),
    ("Target-Profilengine hält Steam-Ziele und stellt bei Timeout wieder her", TargetedProfileMonitorTests.SteamPendingAndTimeoutRestore),
    ("Target-Profilengine bewahrt Stop-Schulden aus Steam Pending", TargetedProfileMonitorTests.StopPendingDebtRemainsManaged),
    ("Target-Profilengine begrenzt das Steam-Wartefenster", TargetedProfileMonitorTests.PendingTimeoutIsBounded),
    ("Target-Profilengine restauriert ToolChanged-false Ziele nicht", TargetedProfileMonitorTests.UnchangedTargetsAreNotRestored),
    ("Target-Profilengine liefert per-target Diagnose", TargetedProfileMonitorTests.DiagnosticsArePerTarget),
    ("Zielentwurf startet ohne implizite Monitorwahl", ProfileTargetEditorTests.NewDraftHasNoImplicitTarget),
    ("Zielentwurf speichert spezifische Snapshots und mehrere Ziele", ProfileTargetEditorTests.AddsSpecificAndMultipleTargetsWithSnapshots),
    ("Zielentwurf blockiert doppelte Selektoren und physische Ziele", ProfileTargetEditorTests.DuplicateSelectorsAndPhysicalTargetsAreBlocked),
    ("Zielentwurf liest und dedupliziert nur Modi der gewählten Quelle", ProfileTargetEditorTests.ModesComeOnlyFromSelectedSourceAndAreDeduplicated),
    ("Zielentwurf lädt ohne gespeicherte Daten zu verändern", ProfileTargetEditorTests.LoadingAndReadingNeverMutatesStoredTargets),
    ("Fehlende und mehrdeutige Editorziele bleiben fail-safe erhalten", ProfileTargetEditorTests.MissingAndAmbiguousTargetsArePreservedFailSafe),
    ("Neuzuordnung ist ausschließlich bestätigt und explizit", ProfileTargetEditorTests.RebindRequiresConfirmationAndDoesNotAutoMatch),
    ("Neuzuordnung verlangt einen gültigen Modus der neuen Quelle", ProfileTargetEditorTests.RebindRequiresAValidNewMode),
    ("Nicht verfügbarer gespeicherter Modus bleibt sichtbar", ProfileTargetEditorTests.UnavailableStoredModeRemainsVisible),
    ("Editoridentität bleibt bei GDI-Neuordnung am Gerätepfad", ProfileTargetEditorTests.GdiReorderDoesNotChangeSpecificChoice),
    ("Klon- und unpersistierbare Endpoints sind nicht auswählbar", ProfileTargetEditorTests.CloneAndUnpersistableEndpointsCannotBeChosen),
    ("Unsichere Quellen blockieren, native Modusdubletten werden zusammengefasst", ProfileTargetEditorTests.UnsafeSourcesAreBlockedAndNativeDuplicatesAreCollapsed),
    ("Native Modusdubletten mit verschiedener Farbtiefe bleiben ein logischer Modus", ProfileTargetEditorTests.NativeDuplicatesWithDifferentBppRemainOneLogicalMode),
    ("Tiefe Zieländerungen markieren den Entwurf als geändert", ProfileTargetEditorTests.DirtyStateTracksDeepTargetChanges),
    ("Profilspeichern übernimmt nur Modusänderungen am ausgewählten Ziel", ProfileTargetEditorTests.ProfileSaveAcceptsOnlyModeChangeOnSelectedTarget),
    ("Profilspeichern blockiert während der Modus noch lädt", ProfileTargetEditorTests.ProfileSaveBlocksWhileSelectedTargetModeIsLoading)
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
    var manager = new ProfileManager(profiles, new TargetedProfileMonitorTests.FakeTargetedDisplay(), new FakeProcesses());
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

static void V1ProfilesMigrateToPrimaryTarget()
{
    WithTemporaryDirectory(directory =>
    {
        var path = Path.Combine(directory, "profiles.json");
        File.WriteAllText(path, "[{\"Process\":\"legacy.exe\",\"Width\":1920,\"Height\":1080,\"Frequency\":100}]");
        var store = new ProfileStore(path);

        var profile = store.Profiles["legacy.exe"];
        Equal(ProfileRetentionPolicy.Startup, profile.Policy);
        Equal(1, profile.Targets.Count);
        True(profile.Targets[0].MonitorSelector is PrimaryMonitor);
        Equal(Mode(1920, 1080, 100), profile.Targets[0].Mode);

        var json = File.ReadAllText(path);
        True(json.Contains("\"Version\": 2", StringComparison.Ordinal));
        True(json.Contains("\"Kind\": \"PrimaryMonitor\"", StringComparison.Ordinal));
        True(!json.TrimStart().StartsWith("[", StringComparison.Ordinal));

        var unchangedTimestamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, unchangedTimestamp);
        var secondLoad = new ProfileStore(path);
        Equal(profile, secondLoad.Profiles["legacy.exe"]);
        Equal(unchangedTimestamp, File.GetLastWriteTimeUtc(path));
    });
}

static void DisplayProfilesAreDeeplyImmutable()
{
    var sourceMode = Mode(3840, 1080, 100);
    var sourceSelector = new SpecificMonitor(@"\\?\DISPLAY#SAM0E16#ONE", "Samsung C49HG9", "SAM", "0E16");
    var sourceTarget = new DisplayProfileTarget(sourceSelector, sourceMode);
    var sourceTargets = new List<DisplayProfileTarget> { sourceTarget };
    var profile = new DisplayProfile(ProfileRetentionPolicy.Startup, sourceTargets);

    sourceTargets.Clear();
    Equal(1, profile.Targets.Count);
    True(!ReferenceEquals(sourceTarget, profile.Targets[0]));
    True(!ReferenceEquals(sourceMode, profile.Targets[0].Mode));
    True(!ReferenceEquals(sourceSelector, profile.Targets[0].MonitorSelector));

    var copy = profile.DeepCopy();
    Equal(profile, copy);
    True(!ReferenceEquals(profile.Targets, copy.Targets));
    True(!ReferenceEquals(profile.Targets[0], copy.Targets[0]));
    True(!ReferenceEquals(profile.Targets[0].Mode, copy.Targets[0].Mode));
    True(!ReferenceEquals(profile.Targets[0].MonitorSelector, copy.Targets[0].MonitorSelector));
}

static void V2ProfilesRoundTripAllTargetKinds()
{
    WithTemporaryDirectory(directory =>
    {
        var path = Path.Combine(directory, "profiles.json");
        var samsung = new SpecificMonitor(@"\\?\DISPLAY#SAM0E16#A&111&0&UID1#{monitor}", "Samsung C49HG9", "SAM", "0E16");
        var odyssey = new SpecificMonitor(@"\\?\DISPLAY#SAM71AB#B&222&0&UID2#{monitor}", "Odyssey Neo G9", "SAM", "71AB");
        var store = new ProfileStore(path);
        store.Profiles["primary.exe"] = Profile(Mode(1920, 1080, 100), ProfileRetentionPolicy.Once);
        store.Profiles["specific.exe"] = new DisplayProfile(ProfileRetentionPolicy.Startup,
            [new DisplayProfileTarget(samsung, Mode(3840, 1080, 100))]);
        store.Profiles["multi.exe"] = new DisplayProfile(ProfileRetentionPolicy.Continuous,
        [
            new DisplayProfileTarget(samsung, Mode(3840, 1080, 100)),
            new DisplayProfileTarget(odyssey, Mode(5120, 1440, 120))
        ]);

        True(store.Save().Success);
        var loaded = new ProfileStore(path);
        Equal(3, loaded.Profiles.Count);
        True(loaded.Profiles["primary.exe"].Targets[0].MonitorSelector is PrimaryMonitor);
        var loadedSpecific = (SpecificMonitor)loaded.Profiles["specific.exe"].Targets[0].MonitorSelector;
        Equal(samsung.MonitorDevicePath, loadedSpecific.MonitorDevicePath);
        Equal("Samsung C49HG9", loadedSpecific.FriendlyNameSnapshot);
        Equal("SAM", loadedSpecific.EdidManufacturerId);
        Equal("0E16", loadedSpecific.EdidProductCodeId);
        Equal(2, loaded.Profiles["multi.exe"].Targets.Count);
        Equal(store.Profiles["multi.exe"], loaded.Profiles["multi.exe"]);

        var json = File.ReadAllText(path);
        True(json.Contains("\"Kind\": \"SpecificMonitor\"", StringComparison.Ordinal));
        True(json.Contains("\"Policy\": \"Continuous\"", StringComparison.Ordinal));
        True(json.Contains("Samsung C49HG9", StringComparison.Ordinal));
    });
}

static void InvalidV2ProfilesAreBlockedTransactionally()
{
    var validPrimaryTarget = "{\"MonitorSelector\":{\"Kind\":\"PrimaryMonitor\"},\"Mode\":{\"Width\":1920,\"Height\":1080,\"Frequency\":60}}";
    var validProfile = $"{{\"Process\":\"game.exe\",\"Policy\":\"Startup\",\"Targets\":[{validPrimaryTarget}]}}";
    var validSpecificTarget = "{\"MonitorSelector\":{\"Kind\":\"SpecificMonitor\",\"MonitorDevicePath\":\"\\\\\\\\?\\\\DISPLAY#SAM1234#ONE\",\"FriendlyNameSnapshot\":\"Samsung\"},\"Mode\":{\"Width\":1920,\"Height\":1080,\"Frequency\":60}}";
    var invalidDocuments = new[]
    {
        $"{{\"Version\":3,\"Profiles\":[{validProfile}]}}",
        "{\"Version\":2,\"Profiles\":null}",
        "{\"Version\":2,\"Profiles\":[null]}",
        "{\"Version\":2,\"Profiles\":[{\"Process\":null,\"Policy\":\"Startup\",\"Targets\":[]}]}",
        "{\"Version\":2,\"Profiles\":[{\"Process\":\"game.exe\",\"Policy\":null,\"Targets\":[]}]}",
        "{\"Version\":2,\"Profiles\":[{\"Process\":\"game.exe\",\"Policy\":\"\",\"Targets\":[]}]}",
        "{\"Version\":2,\"Profiles\":[{\"Process\":\"game.exe\",\"Policy\":\"Forever\",\"Targets\":[]}]}",
        "{\"Version\":2,\"Profiles\":[{\"Process\":\"game.exe\",\"Policy\":\"Startup\",\"Targets\":null}]}",
        "{\"Version\":2,\"Profiles\":[{\"Process\":\"game.exe\",\"Policy\":\"Startup\",\"Targets\":[]}]}",
        "{\"Version\":2,\"Profiles\":[{\"Process\":\"game.exe\",\"Policy\":\"Startup\",\"Targets\":[{\"MonitorSelector\":{\"Kind\":\"SimilarMonitor\"},\"Mode\":{\"Width\":1920,\"Height\":1080,\"Frequency\":60}}]}]}",
        "{\"Version\":2,\"Profiles\":[{\"Process\":\"game.exe\",\"Policy\":\"Startup\",\"Targets\":[null]}]}",
        "{\"Version\":2,\"Profiles\":[{\"Process\":\"game.exe\",\"Policy\":\"Startup\",\"Targets\":[{\"MonitorSelector\":null,\"Mode\":{\"Width\":1920,\"Height\":1080,\"Frequency\":60}}]}]}",
        "{\"Version\":2,\"Profiles\":[{\"Process\":\"game.exe\",\"Policy\":\"Startup\",\"Targets\":[{\"MonitorSelector\":{\"Kind\":\"PrimaryMonitor\"},\"Mode\":null}]}]}",
        "{\"Version\":2,\"Profiles\":[{\"Process\":\"game.exe\",\"Policy\":\"Startup\",\"Targets\":[{\"MonitorSelector\":{\"Kind\":\"PrimaryMonitor\"},\"Mode\":{\"Width\":0,\"Height\":1080,\"Frequency\":60}}]}]}",
        "{\"Version\":2,\"Profiles\":[{\"Process\":\"game.exe\",\"Policy\":\"Startup\",\"Targets\":[{\"MonitorSelector\":{\"Kind\":\"SpecificMonitor\",\"MonitorDevicePath\":\"\\\\.\\\\DISPLAY1\",\"FriendlyNameSnapshot\":\"Samsung\"},\"Mode\":{\"Width\":1920,\"Height\":1080,\"Frequency\":60}}]}]}",
        "{\"Version\":2,\"Profiles\":[{\"Process\":\"game.exe\",\"Policy\":\"Startup\",\"Targets\":[{\"MonitorSelector\":{\"Kind\":\"SpecificMonitor\",\"MonitorDevicePath\":\"\\\\\\\\?\\\\NOT_A_MONITOR\",\"FriendlyNameSnapshot\":\"Samsung\"},\"Mode\":{\"Width\":1920,\"Height\":1080,\"Frequency\":60}}]}]}",
        $"{{\"Version\":2,\"Profiles\":[{{\"Process\":\"game.exe\",\"Policy\":\"Startup\",\"Targets\":[{validPrimaryTarget},{validPrimaryTarget}]}}]}}",
        $"{{\"Version\":2,\"Profiles\":[{{\"Process\":\"game.exe\",\"Policy\":\"Startup\",\"Targets\":[{validSpecificTarget},{validSpecificTarget.Replace("SAM1234", "sam1234", StringComparison.Ordinal)}]}}]}}",
        $"{{\"Version\":2,\"Profiles\":[{validProfile},{validProfile.Replace("game.exe", "GAME.EXE", StringComparison.Ordinal)}]}}"
    };

    WithTemporaryDirectory(directory =>
    {
        var path = Path.Combine(directory, "profiles.json");
        File.WriteAllText(path, $"{{\"Version\":2,\"Profiles\":[{validProfile}]}}");
        var store = new ProfileStore(path);
        Equal(1, store.Profiles.Count);

        foreach (var invalid in invalidDocuments)
        {
            File.WriteAllText(path, invalid);
            True(!store.Load().Success);
            Equal(1, store.Profiles.Count);
            True(store.Profiles.ContainsKey("game.exe"));
            True(!store.Save().Success);
            Equal(invalid, File.ReadAllText(path));
        }
    });
}

static void InvalidInMemoryTargetsDoNotOverwrite()
{
    WithTemporaryDirectory(directory =>
    {
        var path = Path.Combine(directory, "profiles.json");
        var store = new ProfileStore(path);
        store.Profiles["valid.exe"] = Profile(Mode(1920, 1080, 60));
        True(store.Save().Success);
        var validJson = File.ReadAllText(path);

        store.Profiles["invalid.exe"] = new DisplayProfile(ProfileRetentionPolicy.Startup, []);
        True(!store.Save().Success);
        Equal(validJson, File.ReadAllText(path));
    });
}

static void MultiTargetWorkflowRollsBackDeeply()
{
    WithTemporaryDirectory(directory =>
    {
        var blockedDirectory = Path.Combine(directory, "blockiert");
        File.WriteAllText(blockedDirectory, "kein Verzeichnis");
        var store = new ProfileStore(Path.Combine(blockedDirectory, "profiles.json"));
        var oldKey = @"C:\Games\old.exe";
        var newKey = @"D:\Games\new.exe";
        var original = MultiProfile();
        var collision = new DisplayProfile(ProfileRetentionPolicy.Once,
            [new DisplayProfileTarget(new SpecificMonitor(@"\\?\DISPLAY#DEL0001#OLD", "Anderer Monitor"), Mode(1024, 768, 60))]);
        store.Profiles[oldKey] = original;
        store.Profiles[newKey] = collision;

        var result = ProfileEditWorkflow.Save(store, oldKey, newKey,
            new DisplayProfile(ProfileRetentionPolicy.Once,
                [new DisplayProfileTarget(new PrimaryMonitor(), Mode(800, 600, 60))]));

        True(!result.Success);
        Equal(original, store.Profiles[oldKey]);
        Equal(collision, store.Profiles[newKey]);
        True(!ReferenceEquals(original, store.Profiles[oldKey]));
        True(!ReferenceEquals(original.Targets, store.Profiles[oldKey].Targets));

        var removedReference = store.Profiles[oldKey];
        True(!ProfileEditWorkflow.Remove(store, oldKey).Success);
        Equal(original, store.Profiles[oldKey]);
        True(!ReferenceEquals(removedReference, store.Profiles[oldKey]));
    });
}

static void UnsupportedTargetsHaveNoRuntimeSideEffects()
{
    foreach (var unsupported in new[]
    {
        new DisplayProfile(ProfileRetentionPolicy.Startup,
            [new DisplayProfileTarget(new SpecificMonitor(@"\\?\DISPLAY#SAM0E16#ONE", "Samsung C49HG9"), Mode(3840, 1080, 100))]),
        MultiProfile()
    })
    {
        var path = ProcessMatcher.CanonicalizePath(@"C:\Games\game.exe");
        var display = new FakeDisplay { Current = Mode(1920, 1080, 60) };
        var launcher = new FakeLauncher(new ProcessIdentity(42, DateTime.UnixEpoch, path));
        var launchPlans = new FakeLaunchPlanResolver(LaunchPlan.Direct(path));
        var processes = new FakeProcesses();
        var profiles = new ConcurrentDictionary<string, DisplayProfile>(StringComparer.OrdinalIgnoreCase) { [path] = unsupported };
        var monitor = new ProfileMonitor(profiles, display, processes, launcher: launcher, fileExists: _ => true, launchPlans: launchPlans);

        var launch = monitor.LaunchProfileApplication(path, DateTime.UnixEpoch);
        True(!launch.Success);
        True(launch.Error?.Contains("ausschließlich genau ein Ziel", StringComparison.Ordinal) == true);
        Equal(0, display.GetCalls);
        Equal(0, display.SetCalls.Count);
        Equal(0, launcher.LaunchCount);
        Equal(0, launcher.SteamLaunchCount);
        Equal(0, launchPlans.ResolveCount);
        Equal(0, processes.GetCalls);

        processes.Items = [new ProcessIdentity(7, DateTime.UnixEpoch, path)];
        monitor.Poll(DateTime.UnixEpoch.AddSeconds(1));
        monitor.Poll(DateTime.UnixEpoch.AddSeconds(2));
        Equal(ProfileMonitorState.Error, monitor.Status.State);
        Equal(0, display.GetCalls);
        Equal(0, display.SetCalls.Count);
        Equal(0, launcher.LaunchCount);
        Equal(0, launcher.SteamLaunchCount);
        Equal(0, launchPlans.ResolveCount);
        Equal(2, processes.GetCalls);
        Equal(1, monitor.DiagnosticEvents.Count(item => item.Message.Contains("Profilstart abgelehnt", StringComparison.Ordinal)));
        Equal(0, monitor.DiagnosticEvents.Count(item => item.Message.Contains("Automatische Profilaktivierung abgelehnt", StringComparison.Ordinal)));
    }
}

static void CurrentUiWorkflowPreservesUnsupportedTargets()
{
    WithTemporaryDirectory(directory =>
    {
        var store = new ProfileStore(Path.Combine(directory, "profiles.json"));
        var key = @"C:\Games\game.exe";
        var original = MultiProfile();
        store.Profiles[key] = original;
        True(store.Save().Success);

        var result = ProfileEditWorkflow.Save(store, key, key, Mode(800, 600, 60), ProfileRetentionPolicy.Once);
        True(!result.Success);
        Equal(original, store.Profiles[key]);
        Equal(2, store.Profiles[key].Targets.Count);

        var reloaded = new ProfileStore(store.FilePath);
        Equal(original, reloaded.Profiles[key]);
        Equal("2 Monitorziele", DisplayProfilePresentation.DescribeTargets(original));
    });
}

static void MonitorSelectorIdentityIsCaseInsensitive()
{
    var first = new SpecificMonitor(@"\\?\DISPLAY#SAM0E16#ABC", "Samsung C49HG9", "SAM", "0E16");
    var sameIdentity = new SpecificMonitor(@"\\?\display#sam0e16#abc", "Umbenannter Hinweis", "XXX", "FFFF");
    var other = new SpecificMonitor(@"\\?\DISPLAY#SAM0E16#OTHER", "Samsung C49HG9", "SAM", "0E16");

    True(MonitorSelectorIdentity.Equals(first, sameIdentity));
    True(!MonitorSelectorIdentity.Equals(first, other));
    True(MonitorSelectorIdentity.Equals(new PrimaryMonitor(), new PrimaryMonitor()));
    True(!MonitorSelectorIdentity.Equals(new PrimaryMonitor(), first));
    Equal("Samsung C49HG9 → 3840x1080@100Hz", DisplayProfilePresentation.DescribeTargets(
        new DisplayProfile(ProfileRetentionPolicy.Startup,
            [new DisplayProfileTarget(first, Mode(3840, 1080, 100))])));
}

static DisplayProfile MultiProfile() => new(ProfileRetentionPolicy.Continuous,
[
    new DisplayProfileTarget(new SpecificMonitor(@"\\?\DISPLAY#SAM0E16#ONE", "Samsung C49HG9", "SAM", "0E16"), Mode(3840, 1080, 100)),
    new DisplayProfileTarget(new SpecificMonitor(@"\\?\DISPLAY#SAM71AB#TWO", "Odyssey Neo G9", "SAM", "71AB"), Mode(5120, 1440, 120))
]);

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

static void TopologyMapsPhysicalTargetsToSources()
{
    var api = TwoMonitorDisplayApi();
    var result = new WindowsDisplayTopologyService(api).GetSnapshot();

    True(result.Success, result.Error?.Message);
    Equal(2, result.Value!.Endpoints.Count);
    var samsung = result.Value.Endpoints.Single(endpoint => endpoint.FriendlyName == "C49HG9x");
    Equal(@"\\?\DISPLAY#SAM0F9C#A", samsung.MonitorDevicePath);
    Equal(@"\\.\DISPLAY1", samsung.GdiSourceName);
    Equal((ushort)0x4c2d, samsung.EdidManufacturerId);
    Equal((ushort)0x0f9c, samsung.EdidProductCodeId);
    Equal(DisplayOutputTechnology.DisplayPortExternal, samsung.OutputTechnology);
    Equal((uint)2, samsung.ConnectorInstance);
    True(samsung.IsPrimary);
    Equal(new DisplayPoint(0, 0), samsung.Position);
    Equal((uint)3840, samsung.CurrentMode!.Width);

    var other = result.Value.Endpoints.Single(endpoint => endpoint.FriendlyName == "Office Monitor");
    Equal(@"\\.\DISPLAY2", other.GdiSourceName);
    True(!other.IsPrimary);
    Equal(new DisplayPoint(3840, 0), other.Position);
    Equal(0, api.MutationCalls);
}

static void TopologyRetriesWholeQueryAfterInsufficientBuffer()
{
    var api = TwoMonitorDisplayApi();
    api.InsufficientQueryCount = 1;

    var result = new WindowsDisplayTopologyService(api).GetSnapshot();

    True(result.Success, result.Error?.Message);
    Equal(2, api.SizeCalls);
    Equal(2, api.QueryCalls);
    Equal((uint)(WindowsDisplayTopologyService.QueryOnlyActivePaths | WindowsDisplayTopologyService.QueryVirtualModeAware), api.LastFlags);
}

static void SpecificMatchingUsesOnlyPersistentPath()
{
    var first = Endpoint(@"\\?\DISPLAY#SAM1234#ONE", "Same Name", @"\\.\DISPLAY1", isPrimary: true, manufacturer: 0x4c2d, product: 0x1234);
    var second = Endpoint(@"\\?\DISPLAY#SAM1234#TWO", "Same Name", @"\\.\DISPLAY2", manufacturer: 0x4c2d, product: 0x1234);
    var snapshot = new DisplayTopologySnapshot([first, second]);

    var exact = MonitorSelectorMatcher.Resolve(snapshot, new SpecificMonitor(
        @"\\?\display#sam1234#two",
        "falscher alter Name",
        "anders",
        "anders"));
    Equal(MonitorMatchStatus.Exact, exact.Status);
    Equal(second.TargetIdentity, exact.Endpoint!.TargetIdentity);

    var noFriendlyFallback = MonitorSelectorMatcher.Resolve(snapshot, new SpecificMonitor(
        @"\\?\DISPLAY#NICHT#DA",
        "Same Name",
        "4C2D",
        "1234"));
    Equal(MonitorMatchStatus.Missing, noFriendlyFallback.Status);
}

static void MissingAndDuplicatePathsFailSafe()
{
    var endpoint = Endpoint(@"\\?\DISPLAY#SAM0F9C#A", "C49HG9x", @"\\.\DISPLAY1", isPrimary: true);
    var selector = new SpecificMonitor(@"\\?\display#sam0f9c#a", "C49HG9x");

    Equal(MonitorMatchStatus.Missing, MonitorSelectorMatcher.Resolve(new DisplayTopologySnapshot([]), selector).Status);
    Equal(MonitorMatchStatus.Ambiguous, MonitorSelectorMatcher.Resolve(
        new DisplayTopologySnapshot([endpoint, endpoint with { TargetIdentity = new DisplayTargetIdentity(new DisplayAdapterId(1), 99) }]),
        selector).Status);
    Equal(MonitorMatchStatus.Unpersistable, MonitorSelectorMatcher.Resolve(
        new DisplayTopologySnapshot([endpoint]),
        new SpecificMonitor("   ", "C49HG9x")).Status);
}

static void PrimaryMatchingIsFailSafe()
{
    var first = Endpoint("path-a", "A", @"\\.\DISPLAY1", isPrimary: true);
    var second = Endpoint("path-b", "B", @"\\.\DISPLAY2", isPrimary: true, sourceId: 2, targetId: 2);

    Equal(MonitorMatchStatus.Missing, MonitorSelectorMatcher.Resolve(
        new DisplayTopologySnapshot([first with { IsPrimary = false }]), new PrimaryMonitor()).Status);
    Equal(MonitorMatchStatus.Ambiguous, MonitorSelectorMatcher.Resolve(
        new DisplayTopologySnapshot([first, second]), new PrimaryMonitor()).Status);
    var exact = MonitorSelectorMatcher.Resolve(new DisplayTopologySnapshot([first]), new PrimaryMonitor());
    Equal(MonitorMatchStatus.Exact, exact.Status);
    Equal(first.TargetIdentity, exact.Endpoint!.TargetIdentity);
}

static void GdiReorderUsesFreshSnapshot()
{
    var api = TwoMonitorDisplayApi();
    var service = new WindowsDisplayTopologyService(api);
    var selector = new SpecificMonitor(@"\\?\DISPLAY#SAM0F9C#A", "C49HG9x");
    var before = service.GetSnapshot().Value!;
    Equal(@"\\.\DISPLAY1", MonitorSelectorMatcher.Resolve(before, selector).Endpoint!.GdiSourceName);

    api.SourceNames[new DisplaySourceIdentity(new DisplayAdapterId(1), 1)] = @"\\.\DISPLAY3";
    api.PrimarySources.Clear();
    api.PrimarySources.Add(@"\\.\DISPLAY3");
    api.CurrentModes[@"\\.\DISPLAY3"] = new EndpointDisplayMode(5120, 1440, 120, 32, 0, DisplayOrientation.Default, 0);
    var after = service.GetSnapshot().Value!;

    Equal(@"\\.\DISPLAY3", MonitorSelectorMatcher.Resolve(after, selector).Endpoint!.GdiSourceName);
    Equal(@"\\.\DISPLAY1", MonitorSelectorMatcher.Resolve(before, selector).Endpoint!.GdiSourceName);
    True(!ReferenceEquals(before, after));
}

static void EmptyDevicePathIsVisibleButUnpersistable()
{
    var endpoint = Endpoint(string.Empty, "Projector", @"\\.\DISPLAY4");
    var snapshot = new DisplayTopologySnapshot([endpoint]);

    Equal(1, snapshot.Endpoints.Count);
    True(!snapshot.Endpoints[0].IsPersistable);
    Equal(MonitorMatchStatus.Unpersistable, MonitorSelectorMatcher.CreateSpecificSelection(endpoint).Status);
    Equal(MonitorMatchStatus.Missing, MonitorSelectorMatcher.Resolve(
        snapshot,
        new SpecificMonitor("Projector", "Projector")).Status);
}

static void CloneSourcesAreRejected()
{
    var api = TwoMonitorDisplayApi();
    api.Paths =
    [
        api.Paths[0],
        api.Paths[1] with { SourceIdentity = api.Paths[0].SourceIdentity, SourceModeInfoIndex = api.Paths[0].SourceModeInfoIndex }
    ];
    api.SourceNames[api.Paths[0].SourceIdentity] = @"\\.\DISPLAY1";
    var snapshot = new WindowsDisplayTopologyService(api).GetSnapshot().Value!;

    True(snapshot.Endpoints.All(endpoint => endpoint.IsCloneSource));
    Equal(MonitorMatchStatus.CloneGroupUnsupported, MonitorSelectorMatcher.Resolve(snapshot, new PrimaryMonitor()).Status);
    Equal(MonitorMatchStatus.CloneGroupUnsupported, MonitorSelectorMatcher.Resolve(
        snapshot,
        new SpecificMonitor(snapshot.Endpoints[1].MonitorDevicePath, snapshot.Endpoints[1].FriendlyName)).Status);
}

static void VirtualCloneGroupsAreRejected()
{
    var api = TwoMonitorDisplayApi();
    api.Paths =
    [
        api.Paths[0] with { SourceModeInfoIndex = null, CloneGroupId = 7 },
        api.Paths[1] with { SourceModeInfoIndex = null, CloneGroupId = 7 }
    ];

    var snapshot = new WindowsDisplayTopologyService(api).GetSnapshot().Value!;

    True(snapshot.Endpoints.All(endpoint => endpoint.IsCloneSource));
    Equal(MonitorMatchStatus.CloneGroupUnsupported, MonitorSelectorMatcher.Resolve(
        snapshot,
        new SpecificMonitor(snapshot.Endpoints[0].MonitorDevicePath, snapshot.Endpoints[0].FriendlyName)).Status);
}

static void EndpointLabelsUseWindowsNamesAndFallbacks()
{
    var neo = Endpoint("neo-path", "  Odyssey Neo G9  ", @"\\.\DISPLAY7", isPrimary: true) with
    {
        OutputTechnology = DisplayOutputTechnology.Hdmi,
        Position = new DisplayPoint(-5120, 0),
        CurrentMode = new EndpointDisplayMode(5120, 1440, 240, 32, 0, DisplayOrientation.Default, 0)
    };
    var neoLabel = DisplayEndpointLabel.Format(neo);
    True(neoLabel.StartsWith("Odyssey Neo G9 (", StringComparison.Ordinal));
    True(neoLabel.Contains("Primär", StringComparison.Ordinal));
    True(neoLabel.Contains("HDMI", StringComparison.Ordinal));
    True(neoLabel.Contains("Anzeige 7", StringComparison.Ordinal));
    True(neoLabel.Contains("5120x1440", StringComparison.Ordinal));
    True(neoLabel.Contains("Position -5120/0", StringComparison.Ordinal));

    var edidFallback = DisplayEndpointLabel.Format(Endpoint("path", "", @"\\.\DISPLAY2", manufacturer: 0x4c2d, product: 0x1234));
    True(edidFallback.StartsWith("Monitor 0x4C2D/0x1234", StringComparison.Ordinal));
    True(DisplayEndpointLabel.Format(Endpoint("path", "", string.Empty)).StartsWith("Unbekannter Monitor", StringComparison.Ordinal));
}

static void AvailableModesReadOnlySelectedSource()
{
    var api = TwoMonitorDisplayApi();
    api.AvailableModes[@"\\.\DISPLAY1"] = [new EndpointDisplayMode(3840, 1080, 100, 32, 0, DisplayOrientation.Default, 0)];
    api.AvailableModes[@"\\.\DISPLAY2"] = [new EndpointDisplayMode(1920, 1080, 60, 32, 0, DisplayOrientation.Default, 0)];
    var service = new WindowsDisplayTopologyService(api);
    var endpoint = service.GetSnapshot().Value!.Endpoints.Single(item => item.GdiSourceName == @"\\.\DISPLAY2");
    api.ModeReadSources.Clear();

    var result = service.GetAvailableModes(endpoint);

    True(result.Success, result.Error?.Message);
    Equal(1, result.Value!.Count);
    True(api.ModeReadSources.All(source => source == @"\\.\DISPLAY2"));
    True(!api.ModeReadSources.Contains(@"\\.\DISPLAY1"));
    Equal(0, api.MutationCalls);
}

static void AvailableModesDeduplicateFullIdentity()
{
    var api = TwoMonitorDisplayApi();
    var baseMode = new EndpointDisplayMode(1920, 1080, 60, 32, 0, DisplayOrientation.Default, 0);
    api.AvailableModes[@"\\.\DISPLAY1"] =
    [
        baseMode,
        baseMode,
        baseMode with { Orientation = DisplayOrientation.Rotate90 },
        baseMode with { BitsPerPixel = 24 },
        baseMode with { DisplayFlags = 2 }
    ];
    var endpoint = new WindowsDisplayTopologyService(api).GetSnapshot().Value!.Endpoints[0];

    var modes = new WindowsDisplayTopologyService(api).GetAvailableModes(endpoint).Value!;

    Equal(4, modes.Count);
    True(modes.Contains(baseMode));
    True(modes.Contains(baseMode with { Orientation = DisplayOrientation.Rotate90 }));
}

static void NativeDisplayStructLayoutsMatchWin32()
{
    var apiType = typeof(WindowsDisplayApi);
    Type Nested(string name) => apiType.GetNestedType(
        name,
        System.Reflection.BindingFlags.NonPublic) ?? throw new InvalidOperationException($"Native Struktur {name} fehlt.");
    void Size(string name, int expected) => Equal(expected, System.Runtime.InteropServices.Marshal.SizeOf(Nested(name)));

    Size("Luid", 8);
    Size("DisplayConfigPathSourceInfo", 20);
    Size("DisplayConfigPathTargetInfo", 48);
    Size("DisplayConfigPathInfo", 72);
    Size("DisplayConfigModeInfo", 64);
    Size("DisplayConfigDeviceInfoHeader", 20);
    Size("DisplayConfigTargetDeviceName", 420);
    Size("DisplayConfigSourceDeviceName", 84);
    Size("DisplayDevice", 840);
    Size("DevMode", 220);

    var devMode = Nested("DevMode");
    Equal(new IntPtr(76), System.Runtime.InteropServices.Marshal.OffsetOf(devMode, "DisplayInfo"));
    Equal(new IntPtr(168), System.Runtime.InteropServices.Marshal.OffsetOf(devMode, "BitsPerPel"));
    Equal(new IntPtr(184), System.Runtime.InteropServices.Marshal.OffsetOf(devMode, "DisplayFrequency"));
    var displayUnion = Nested("DevModeDisplayUnion");
    Equal(new IntPtr(8), System.Runtime.InteropServices.Marshal.OffsetOf(displayUnion, "DisplayOrientation"));
    Equal(new IntPtr(12), System.Runtime.InteropServices.Marshal.OffsetOf(displayUnion, "DisplayFixedOutput"));
}

static void TopologySnapshotsAreImmutableAndReadOnly()
{
    var api = TwoMonitorDisplayApi();
    var snapshot = new WindowsDisplayTopologyService(api).GetSnapshot().Value!;

    True(snapshot.Endpoints is not DisplayEndpoint[]);
    True(snapshot.Endpoints is not ICollection<DisplayEndpoint> collection || collection.IsReadOnly);
    Equal(0, api.MutationCalls);
    Equal(0, api.RegistryWriteCalls);
    Equal(0, api.FileWriteCalls);
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
        Equal(profile, store.Profiles[key]);
        True(!ReferenceEquals(profile, store.Profiles[key]));
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

static DisplayEndpoint Endpoint(
    string path,
    string friendlyName,
    string sourceName,
    bool isPrimary = false,
    ushort manufacturer = 0,
    ushort product = 0,
    uint sourceId = 1,
    uint targetId = 1) => new(
        new DisplayTargetIdentity(new DisplayAdapterId(1), targetId),
        new DisplaySourceIdentity(new DisplayAdapterId(1), sourceId),
        path,
        friendlyName,
        manufacturer,
        product,
        DisplayOutputTechnology.DisplayPortExternal,
        0,
        sourceName,
        isPrimary,
        new DisplayPoint(0, 0),
        new EndpointDisplayMode(1920, 1080, 60, 32, 0, DisplayOrientation.Default, 0),
        false);

static FakeWindowsDisplayApi TwoMonitorDisplayApi()
{
    var source1 = new DisplaySourceIdentity(new DisplayAdapterId(1), 1);
    var source2 = new DisplaySourceIdentity(new DisplayAdapterId(1), 2);
    var target1 = new DisplayTargetIdentity(new DisplayAdapterId(1), 11);
    var target2 = new DisplayTargetIdentity(new DisplayAdapterId(1), 12);
    var api = new FakeWindowsDisplayApi
    {
        Paths =
        [
            new WindowsDisplayConfigPath(source1, target1, 1, null, DisplayOutputTechnology.DisplayPortExternal),
            new WindowsDisplayConfigPath(source2, target2, 2, null, DisplayOutputTechnology.Hdmi)
        ],
        Modes =
        [
            new WindowsDisplayConfigMode(false, default, 0, 0, default),
            new WindowsDisplayConfigMode(true, source1, 3840, 1080, new DisplayPoint(0, 0)),
            new WindowsDisplayConfigMode(true, source2, 1920, 1080, new DisplayPoint(3840, 0))
        ]
    };
    api.TargetNames[target1] = new WindowsTargetDeviceName(
        @"\\?\DISPLAY#SAM0F9C#A",
        "  C49HG9x  ",
        0x4c2d,
        0x0f9c,
        DisplayOutputTechnology.DisplayPortExternal,
        2);
    api.TargetNames[target2] = new WindowsTargetDeviceName(
        @"\\?\DISPLAY#DEL1234#B",
        "Office Monitor",
        0x10ac,
        0x1234,
        DisplayOutputTechnology.Hdmi,
        1);
    api.SourceNames[source1] = @"\\.\DISPLAY1";
    api.SourceNames[source2] = @"\\.\DISPLAY2";
    api.PrimarySources.Add(@"\\.\DISPLAY1");
    api.CurrentModes[@"\\.\DISPLAY1"] = new EndpointDisplayMode(3840, 1080, 100, 32, 0, DisplayOrientation.Default, 0);
    api.CurrentModes[@"\\.\DISPLAY2"] = new EndpointDisplayMode(1920, 1080, 60, 32, 0, DisplayOrientation.Default, 0);
    return api;
}

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
    public int GetCalls { get; private set; }
    public Queue<OperationResult> Results { get; } = new();
    public List<DisplayMode> SetCalls { get; } = new();
    public Action<DisplayMode>? OnSet { get; set; }
    public DisplayMode? GetCurrentDisplayMode()
    {
        GetCalls++;
        return Current;
    }
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

sealed class FakeLaunchPlanResolver : ILaunchPlanResolver
{
    private readonly LaunchPlan _plan;
    public int ResolveCount { get; private set; }

    public FakeLaunchPlanResolver(LaunchPlan plan) => _plan = plan;

    public LaunchPlan Resolve(string executablePath)
    {
        ResolveCount++;
        return _plan;
    }
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
    public int GetCalls { get; private set; }
    public IReadOnlyList<ProcessIdentity> GetCurrentSessionProcesses()
    {
        GetCalls++;
        return Items;
    }
}

sealed class FakeAutostartRegistry : IAutostartRegistry
{
    public string? Value { get; set; }
    public OperationResult Read(out string? value) { value = Value; return OperationResult.Ok(); }
    public OperationResult Write(string value) { Value = value; return OperationResult.Ok(); }
    public OperationResult Delete() { Value = null; return OperationResult.Ok(); }
}

sealed class FakeWindowsDisplayApi : IWindowsDisplayApi
{
    public IReadOnlyList<WindowsDisplayConfigPath> Paths { get; set; } = Array.Empty<WindowsDisplayConfigPath>();
    public IReadOnlyList<WindowsDisplayConfigMode> Modes { get; set; } = Array.Empty<WindowsDisplayConfigMode>();
    public Dictionary<DisplayTargetIdentity, WindowsTargetDeviceName> TargetNames { get; } = new();
    public Dictionary<DisplaySourceIdentity, string> SourceNames { get; } = new();
    public HashSet<string> PrimarySources { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, EndpointDisplayMode> CurrentModes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, IReadOnlyList<EndpointDisplayMode>> AvailableModes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> ModeReadSources { get; } = new();
    public int InsufficientQueryCount { get; set; }
    public int SizeCalls { get; private set; }
    public int QueryCalls { get; private set; }
    public uint LastFlags { get; private set; }
    public int MutationCalls { get; private set; }
    public int RegistryWriteCalls { get; private set; }
    public int FileWriteCalls { get; private set; }

    public int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount)
    {
        SizeCalls++;
        LastFlags = flags;
        pathCount = checked((uint)Paths.Count);
        modeCount = checked((uint)Modes.Count);
        return WindowsDisplayTopologyService.ErrorSuccess;
    }

    public int QueryDisplayConfig(
        uint flags,
        uint pathCapacity,
        uint modeCapacity,
        out IReadOnlyList<WindowsDisplayConfigPath> paths,
        out IReadOnlyList<WindowsDisplayConfigMode> modes)
    {
        QueryCalls++;
        LastFlags = flags;
        if (InsufficientQueryCount > 0)
        {
            InsufficientQueryCount--;
            paths = Array.Empty<WindowsDisplayConfigPath>();
            modes = Array.Empty<WindowsDisplayConfigMode>();
            return WindowsDisplayTopologyService.ErrorInsufficientBuffer;
        }

        if (pathCapacity < Paths.Count || modeCapacity < Modes.Count)
        {
            paths = Array.Empty<WindowsDisplayConfigPath>();
            modes = Array.Empty<WindowsDisplayConfigMode>();
            return WindowsDisplayTopologyService.ErrorInsufficientBuffer;
        }

        paths = Array.AsReadOnly(Paths.ToArray());
        modes = Array.AsReadOnly(Modes.ToArray());
        return WindowsDisplayTopologyService.ErrorSuccess;
    }

    public int GetTargetDeviceName(DisplayTargetIdentity target, out WindowsTargetDeviceName deviceName)
    {
        if (TargetNames.TryGetValue(target, out var found))
        {
            deviceName = found;
            return WindowsDisplayTopologyService.ErrorSuccess;
        }

        deviceName = new WindowsTargetDeviceName(string.Empty, string.Empty, 0, 0, DisplayOutputTechnology.Other, 0);
        return 1168;
    }

    public int GetSourceDeviceName(DisplaySourceIdentity source, out string gdiSourceName)
    {
        if (SourceNames.TryGetValue(source, out var found))
        {
            gdiSourceName = found;
            return WindowsDisplayTopologyService.ErrorSuccess;
        }

        gdiSourceName = string.Empty;
        return 1168;
    }

    public bool IsPrimarySource(string gdiSourceName) => PrimarySources.Contains(gdiSourceName);

    public int ReadDisplaySettings(string gdiSourceName, int modeNumber, out EndpointDisplayMode? mode)
    {
        ModeReadSources.Add(gdiSourceName);
        if (modeNumber == WindowsDisplayTopologyService.EnumCurrentSettings)
        {
            if (CurrentModes.TryGetValue(gdiSourceName, out var current))
            {
                mode = current;
                return WindowsDisplayTopologyService.ErrorSuccess;
            }

            mode = null;
            return 31;
        }

        if (!AvailableModes.TryGetValue(gdiSourceName, out var modes) || modeNumber < 0 || modeNumber >= modes.Count)
        {
            mode = null;
            return WindowsDisplayTopologyService.ErrorNoMoreItems;
        }

        mode = modes[modeNumber];
        return WindowsDisplayTopologyService.ErrorSuccess;
    }

    public int ChangeDisplaySettings(
        string gdiSourceName,
        EndpointDisplayMode candidate,
        WindowsDisplayChangeKind kind)
    {
        MutationCalls++;
        if (kind == WindowsDisplayChangeKind.ApplyTemporary)
            CurrentModes[gdiSourceName] = candidate;
        return 0;
    }
}
