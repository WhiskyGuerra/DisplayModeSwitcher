using System.Collections.Concurrent;
using DisplayModeSwitcher;

/// <summary>Orchestration tests for the receipt-owning profile runtime.  These
/// are deliberately independent of topology, P/Invoke and real processes.</summary>
public static class TargetedProfileMonitorTests
{
    private static readonly DateTime T0 = DateTime.UnixEpoch.AddDays(1);

    public static void InitialBatchesAreExact()
    {
        var primary = new DisplayProfile(ProfileRetentionPolicy.Once, [Target(new PrimaryMonitor(), 120)]);
        var specific = new DisplayProfile(ProfileRetentionPolicy.Once, [Target(new SpecificMonitor("PATH-A", "A"), 100)]);
        var multi = new DisplayProfile(ProfileRetentionPolicy.Once, [Target(new SpecificMonitor("PATH-A", "A"), 100), Target(new SpecificMonitor("PATH-B", "B"), 144)]);
        RunInitial(primary); RunInitial(specific);
        var display = new FakeTargetedDisplay(); var processes = new FakeProcesses(Process("C:\\Games\\multi.exe"));
        var monitor = Monitor("C:\\Games\\multi.exe", multi, display, processes);
        monitor.Start(T0); monitor.Poll(T0);
        Expect(display.Applies.Count == 1 && display.Applies[0].Count == 2, "Mehrmonitorprofil muss genau einen Batch anwenden.");
        Expect(display.Applies[0][0].MonitorSelector is SpecificMonitor { MonitorDevicePath: "PATH-A" } && display.Applies[0][1].MonitorSelector is SpecificMonitor { MonitorDevicePath: "PATH-B" }, "Specific-Ziele dürfen nicht ersetzt werden.");
        Expect(monitor.Status.State == ProfileMonitorState.Active, "Erfolgreicher Batch muss aktiv werden.");
    }

    public static void InitialFailureRetriesWithoutLatch()
    {
        var display = new FakeTargetedDisplay(); display.ApplyResults.Enqueue(Fail("fehlt"));
        var process = Process("C:\\Games\\retry.exe"); var monitor = Monitor(process.ExecutablePath, Profile(ProfileRetentionPolicy.Startup), display, new FakeProcesses(process));
        monitor.Start(T0); monitor.Poll(T0); monitor.Poll(T0.AddSeconds(4));
        Expect(display.Applies.Count == 1 && monitor.Status.State == ProfileMonitorState.RetryPending, "Initialfehler darf weder latchen noch vor dem Intervall fluten.");
        monitor.Poll(T0.AddSeconds(5));
        Expect(display.Applies.Count == 2 && monitor.Status.State == ProfileMonitorState.Active, "Initialfehler muss rate-limited als Batch wiederholt werden.");
    }

    public static void ControlledApplyFailureIsRateLimited()
    {
        var path = "C:\\Games\\controlled-retry.exe";
        var display = new FakeTargetedDisplay();
        display.ApplyResults.Enqueue(Fail("vorübergehend"));
        var launcher = new FakeLauncher(Process(path));
        var monitor = Monitor(path, Profile(ProfileRetentionPolicy.Once), display, new FakeProcesses(), launcher);
        monitor.Start(T0);

        Expect(!monitor.LaunchProfileApplication(path, T0).Success, "Der erste Apply-Fehler muss den Start ablehnen.");
        Expect(!monitor.LaunchProfileApplication(path, T0.AddSeconds(1)).Success && display.Applies.Count == 1,
            "Kontrollierte Wiederholungen dürfen das Retry-Intervall nicht umgehen.");
        Expect(monitor.LaunchProfileApplication(path, T0.AddSeconds(5)).Success && display.Applies.Count == 2 && launcher.DirectLaunches == 1,
            "Nach Ablauf des Intervalls muss genau ein neuer Batch und danach der Start erlaubt sein.");
    }

    public static void RollbackDebtBlocksFurtherWork()
    {
        var receipt = Receipt(0, "PATH-A", true, 60, 120);
        var debt = new DisplayRestoreDebt(receipt, Error(0, "PATH-A", "Rollback fehlgeschlagen"));
        var display = new FakeTargetedDisplay(); display.ApplyResults.Enqueue(new TargetedDisplayApplyResult(false, [receipt], [debt.Error], [debt]));
        var launcher = new FakeLauncher(Process("C:\\Games\\debt.exe"));
        var monitor = Monitor("C:\\Games\\debt.exe", Profile(ProfileRetentionPolicy.Once), display, new FakeProcesses(), launcher);
        monitor.Start(T0);
        var launch = monitor.LaunchProfileApplication("C:\\Games\\debt.exe", T0);
        Expect(!launch.Success && launcher.DirectLaunches == 0 && display.Applies.Count == 1, "Debt-Apply darf keine Anwendung starten.");
        monitor.Poll(T0.AddSeconds(5));
        Expect(display.Restores.Count == 1 && display.Applies.Count == 1, "Rollback-Schuld muss vor jedem erneuten Apply behandelt werden.");
    }

    public static void RestoreDebtRetriesOnlyRemainingTarget()
    {
        var first = Receipt(0, "PATH-A", true, 60, 120); var second = Receipt(1, "PATH-B", true, 60, 144);
        var debt = new DisplayRestoreDebt(second, Error(1, "PATH-B", "getrennt"));
        var display = new FakeTargetedDisplay(); display.ApplyResults.Enqueue(new TargetedDisplayApplyResult(true, [first, second], [], []));
        display.RestoreResults.Enqueue(new TargetedDisplayRestoreResult([debt.Error], [debt])); display.RestoreResults.Enqueue(new TargetedDisplayRestoreResult([], []));
        var process = Process("C:\\Games\\restore.exe"); var processes = new FakeProcesses(process); var monitor = Monitor(process.ExecutablePath, Multi(), display, processes);
        monitor.Start(T0); monitor.Poll(T0); processes.Items.Clear(); monitor.Poll(T0.AddSeconds(1)); monitor.Poll(T0.AddSeconds(6));
        Expect(display.Restores.Count == 2 && display.Restores[0].Count == 2 && display.Restores[1].Single().MonitorDevicePath == "PATH-B", "Nur die verbliebene konkrete Restore-Schuld darf wiederholt werden.");
        Expect(monitor.Status.State == ProfileMonitorState.Idle, "Nach vollständigem Restore muss der Monitor idle sein.");
    }

    public static void ReapplyPoliciesAndInitialReceipts()
    {
        var process = Process("C:\\Games\\policy.exe");
        var once = new FakeTargetedDisplay(); var onceMonitor = Monitor(process.ExecutablePath, Profile(ProfileRetentionPolicy.Once), once, new FakeProcesses(process));
        onceMonitor.Start(T0); onceMonitor.Poll(T0); onceMonitor.Poll(T0.AddSeconds(2)); Expect(once.Applies.Count == 1, "Once darf keinen zweiten Batch starten.");

        var startup = new FakeTargetedDisplay(); startup.ApplyResults.Enqueue(Ok(Receipt(0, "PATH-A", true, 60, 120))); startup.ApplyResults.Enqueue(Ok(Receipt(0, "PATH-A", true, 77, 120)));
        startup.ApplyResults.Enqueue(Ok(Receipt(0, "PATH-A", true, 78, 120))); startup.ApplyResults.Enqueue(Ok(Receipt(0, "PATH-A", true, 79, 120)));
        var startupMonitor = Monitor(process.ExecutablePath, Profile(ProfileRetentionPolicy.Startup), startup, new FakeProcesses(process));
        startupMonitor.Start(T0); startupMonitor.Poll(T0); startupMonitor.Poll(T0.AddSeconds(1)); startupMonitor.Poll(T0.AddSeconds(2)); startupMonitor.Poll(T0.AddSeconds(3)); startupMonitor.Poll(T0.AddSeconds(4)); startupMonitor.Stop();
        Expect(startup.Applies.Count == 4, "Startup zählt Nachsetzungen als Batch und begrenzt sie auf drei.");
        Expect(startup.Restores.Single().Single().OriginalMode.Frequency == 60, "Reapply darf den initialen Originalbeleg nicht überschreiben.");
        Expect(startupMonitor.DiagnosticEvents.Count(item => item.Message.Contains("Profilziel nachgesetzt", StringComparison.Ordinal)) == 3,
            "Jede tatsächliche Nachsetzung muss genau einen zielbezogenen Diagnoseeintrag erzeugen.");
        Expect(startupMonitor.DiagnosticEvents.Any(item => item.Message.Contains("Ausgang 1920x1080 @ 77Hz", StringComparison.Ordinal)
            && item.Message.Contains("Ziel 1920x1080 @ 120Hz", StringComparison.Ordinal)),
            "Die Nachsetzungsdiagnose muss beobachteten Ausgang und Ziel enthalten.");

        var continuous = new FakeTargetedDisplay(); var continuousMonitor = Monitor(process.ExecutablePath, Profile(ProfileRetentionPolicy.Continuous), continuous, new FakeProcesses(process));
        continuousMonitor.Start(T0); continuousMonitor.Poll(T0); continuousMonitor.Poll(T0.AddSeconds(1)); continuousMonitor.Poll(T0.AddSeconds(2));
        Expect(continuous.Applies.Count == 3, "Continuous darf batchweise weiter nachsetzen.");
    }

    public static void StartupNoOpChecksPreserveReapplyBudget()
    {
        var path = "C:\\Games\\startup-noop.exe";
        var display = new FakeTargetedDisplay();
        display.ApplyResults.Enqueue(Ok(Receipt(0, "PATH-A", false, 120, 120)));
        for (var index = 0; index < 3; index++)
            display.ApplyResults.Enqueue(Ok(Receipt(0, "PATH-A", false, 120, 120)));
        display.ApplyResults.Enqueue(Ok(Receipt(0, "PATH-A", true, 75, 120)));
        display.ApplyResults.Enqueue(Ok(Receipt(0, "PATH-A", true, 76, 120)));
        display.ApplyResults.Enqueue(Ok(Receipt(0, "PATH-A", true, 77, 120)));
        var processes = new FakeProcesses(Process(path));
        var monitor = Monitor(path, Profile(ProfileRetentionPolicy.Startup), display, processes);

        monitor.Start(T0); monitor.Poll(T0);
        monitor.Poll(T0.AddSeconds(1)); monitor.Poll(T0.AddSeconds(2)); monitor.Poll(T0.AddSeconds(3));
        Expect(monitor.Status.ReapplyCount == 0, "Erfolgreiche No-op-Prüfungen dürfen das Startup-Budget nicht verbrauchen.");
        Expect(monitor.Status.LastResult == "Ziel-Batch geprüft; keine Abweichung", "Ein No-op darf im Status nicht als Nachsetzung erscheinen.");

        monitor.Poll(T0.AddSeconds(4));
        Expect(monitor.Status.ReapplyCount == 1, "Die erste spätere tatsächliche Nachsetzung muss genau einmal zählen.");
        monitor.Poll(T0.AddSeconds(5)); monitor.Poll(T0.AddSeconds(6));
        Expect(monitor.Status.ReapplyCount == 3 && display.Applies.Count == 7, "Drei tatsächliche Nachsetzungen müssen das Startup-Limit erreichen.");
        monitor.Poll(T0.AddSeconds(7));
        Expect(display.Applies.Count == 7, "Nach drei echten Versuchen darf kein vierter Korrekturbatch laufen.");

        processes.Items.Clear(); monitor.Poll(T0.AddSeconds(8));
        var restore = display.Restores.Single().Single();
        Expect(restore.ToolChanged && restore.OriginalMode.Frequency == 120,
            "Eine späte tatsächliche Nachsetzung muss weiterhin dem unveränderten Initialbeleg gehören.");
    }

    public static void FailedStartupCorrectionsStillConsumeBudget()
    {
        var path = "C:\\Games\\startup-failures.exe";
        var display = new FakeTargetedDisplay();
        display.ApplyResults.Enqueue(Ok(Receipt(0, "PATH-A", false, 120, 120)));
        display.ApplyResults.Enqueue(Fail("Fehler 1"));
        display.ApplyResults.Enqueue(Fail("Fehler 2"));
        display.ApplyResults.Enqueue(Fail("Fehler 3"));
        var monitor = Monitor(path, Profile(ProfileRetentionPolicy.Startup), display, new FakeProcesses(Process(path)));

        monitor.Start(T0); monitor.Poll(T0);
        monitor.Poll(T0.AddSeconds(1)); monitor.Poll(T0.AddSeconds(6)); monitor.Poll(T0.AddSeconds(11));
        Expect(monitor.Status.ReapplyCount == 3, "Fehlgeschlagene Korrekturversuche müssen das Startup-Budget weiterhin verbrauchen.");
        monitor.Poll(T0.AddSeconds(16));
        Expect(display.Applies.Count == 4, "Nach drei fehlgeschlagenen Korrekturversuchen muss das Startup-Limit ebenfalls greifen.");
    }

    public static void ReapplyRemainsBoundAndOwnsLateChanges()
    {
        var path = "C:\\Games\\bound.exe";
        var display = new FakeTargetedDisplay();
        display.ApplyResults.Enqueue(Ok(Receipt(0, "PATH-A", false, 120, 120)));
        display.ApplyResults.Enqueue(Ok(Receipt(0, "PATH-A", true, 75, 120)));
        var processes = new FakeProcesses(Process(path));
        var monitor = Monitor(path, Profile(ProfileRetentionPolicy.Continuous), display, processes);

        monitor.Start(T0); monitor.Poll(T0); monitor.Poll(T0.AddSeconds(1));
        Expect(display.Applies[0].Single().MonitorSelector is PrimaryMonitor,
            "Nur der Initial-Batch darf den dynamischen Primärselector verwenden.");
        Expect(display.Applies[1].Single().MonitorSelector is SpecificMonitor { MonitorDevicePath: "PATH-A" },
            "Nachsetzungen müssen den initial aufgelösten physischen Pfad verwenden.");
        processes.Items.Clear(); monitor.Poll(T0.AddSeconds(2));
        var restore = display.Restores.Single().Single();
        Expect(restore.MonitorDevicePath == "PATH-A" && restore.ToolChanged && restore.OriginalMode.Frequency == 120,
            "Eine erst später vom Tool geänderte Anzeige muss über den unveränderten Initialbeleg restauriert werden.");
    }

    public static void ReapplyDebtThenRestoresInitialReceipt()
    {
        var path = "C:\\Games\\reapply-debt.exe";
        var initial = Receipt(0, "PATH-A", true, 60, 120);
        var intermediate = Receipt(0, "PATH-A", true, 75, 120);
        var debt = new DisplayRestoreDebt(intermediate, Error(0, "PATH-A", "Rollback offen"));
        var display = new FakeTargetedDisplay();
        display.ApplyResults.Enqueue(Ok(initial));
        display.ApplyResults.Enqueue(new TargetedDisplayApplyResult(false, [intermediate], [debt.Error], [debt]));
        var monitor = Monitor(path, Profile(ProfileRetentionPolicy.Continuous), display, new FakeProcesses(Process(path)));

        monitor.Start(T0); monitor.Poll(T0); monitor.Poll(T0.AddSeconds(1));
        Expect(display.Restores.Count == 2, "Nachsetz-Rollback und Initialbeleg müssen getrennte Restore-Schritte bleiben.");
        Expect(display.Restores[0].Single().OriginalMode.Frequency == 75 && display.Restores[1].Single().OriginalMode.Frequency == 60,
            "Zuerst muss die Rollback-Schuld, danach exakt der ursprüngliche Initialbeleg restauriert werden.");
        Expect(monitor.Status.State == ProfileMonitorState.Idle, "Nach beiden erfolgreichen Restores darf keine Schuld verbleiben.");
    }

    public static void DirectLaunchSafetyAndRestore()
    {
        var display = new FakeTargetedDisplay(); display.ApplyResults.Enqueue(Fail("ungültig"));
        var launcher = new FakeLauncher(Process("C:\\Games\\direct.exe")); var monitor = Monitor("C:\\Games\\direct.exe", Profile(ProfileRetentionPolicy.Once), display, new FakeProcesses(), launcher);
        monitor.Start(T0); Expect(!monitor.LaunchProfileApplication("C:\\Games\\direct.exe", T0).Success && launcher.DirectLaunches == 0, "Direct darf nicht vor erfolgreichem Batch starten.");

        var changed = Receipt(0, "PATH-A", true, 60, 120); var restoreDisplay = new FakeTargetedDisplay(); restoreDisplay.ApplyResults.Enqueue(Ok(changed));
        var failingLauncher = new FakeLauncher(Process("C:\\Games\\direct.exe")) { ThrowOnLaunch = true };
        var failing = Monitor("C:\\Games\\direct.exe", Profile(ProfileRetentionPolicy.Once), restoreDisplay, new FakeProcesses(), failingLauncher);
        failing.Start(T0); Expect(!failing.LaunchProfileApplication("C:\\Games\\direct.exe", T0).Success && restoreDisplay.Restores.Count == 1, "Startfehler muss Initialbelege wiederherstellen.");
    }

    public static void ParallelLaunchStartsOnce()
    {
        var path = "C:\\Games\\parallel.exe";
        var display = new FakeTargetedDisplay();
        var launcher = new FakeLauncher(Process(path));
        var monitor = Monitor(path, Profile(ProfileRetentionPolicy.Once), display, new FakeProcesses(), launcher);
        monitor.Start(T0);

        var results = Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => monitor.LaunchProfileApplication(path, T0)))).GetAwaiter().GetResult();
        Expect(results.Count(item => item.Success) == 1 && display.Applies.Count == 1 && launcher.DirectLaunches == 1,
            "Parallele Starts müssen als eine Apply-und-Launch-Operation serialisiert werden.");
    }

    public static void SteamPendingAndTimeoutRestore()
    {
        var receipt = Receipt(0, "PATH-A", true, 60, 120); var display = new FakeTargetedDisplay(); display.ApplyResults.Enqueue(Ok(receipt));
        var launcher = new FakeLauncher(Process("C:\\Games\\steam.exe")); var steam = new StaticPlanResolver(new LaunchPlan(LaunchStrategy.Steam, "C:\\Games\\steam.exe", "42", SteamLaunchInfo.CreateUri("42")));
        var monitor = Monitor("C:\\Games\\steam.exe", Profile(ProfileRetentionPolicy.Once), display, new FakeProcesses(), launcher, steam);
        monitor.Start(T0); var started = monitor.LaunchProfileApplication("C:\\Games\\steam.exe", T0);
        Expect(started.Success && launcher.SteamLaunches == 1 && monitor.Status.State == ProfileMonitorState.PendingLaunch, "Steam darf erst nach Batch in Pending gehen.");
        monitor.Poll(T0.AddSeconds(1)); monitor.Poll(T0.AddSeconds(120));
        Expect(display.Applies.Count >= 2 && display.Restores.Count == 1, "Steam Pending hält Ziele und stellt bei Timeout wieder her.");
    }

    public static void StopPendingDebtRemainsManaged()
    {
        var path = "C:\\Games\\steam-stop.exe";
        var first = Receipt(0, "PATH-A", true, 60, 100);
        var second = Receipt(1, "PATH-B", true, 60, 144);
        var debt = new DisplayRestoreDebt(second, Error(1, "PATH-B", "getrennt"));
        var display = new FakeTargetedDisplay();
        display.ApplyResults.Enqueue(Ok(first, second));
        display.RestoreResults.Enqueue(new TargetedDisplayRestoreResult([debt.Error], [debt]));
        display.RestoreResults.Enqueue(new TargetedDisplayRestoreResult([], []));
        var launcher = new FakeLauncher(Process(path));
        var steam = new StaticPlanResolver(new LaunchPlan(LaunchStrategy.Steam, path, "42", SteamLaunchInfo.CreateUri("42")));
        var monitor = Monitor(path, Multi(), display, new FakeProcesses(), launcher, steam);
        monitor.Start(T0); Expect(monitor.LaunchProfileApplication(path, T0).Success, "Steam Pending muss vorbereitet sein.");

        Expect(!monitor.Stop().Success && monitor.Status.State == ProfileMonitorState.Restoring,
            "Ein Stop mit Teilfehler muss die offene Schuld verwaltet lassen.");
        Expect(monitor.Stop().Success && monitor.Status.State == ProfileMonitorState.Stopped,
            "Ein weiterer Stop muss ausschließlich die offene Schuld erneut versuchen können.");
        Expect(display.Restores.Count == 2 && display.Restores[0].Count == 2 && display.Restores[1].Single().MonitorDevicePath == "PATH-B",
            "Bereits erfolgreich restaurierte Ziele dürfen beim Stop-Retry nicht erneut gesendet werden.");
    }

    public static void PendingTimeoutIsBounded()
    {
        var path = "C:\\Games\\timeout.exe";
        var display = new FakeTargetedDisplay();
        var processes = new FakeProcesses();
        try
        {
            _ = new TargetedProfileMonitor(
                new ConcurrentDictionary<string, DisplayProfile> { [path] = Profile(ProfileRetentionPolicy.Once) },
                display, processes, pendingLaunchTimeout: TimeSpan.FromSeconds(121));
            throw new InvalidOperationException("Ein Steam-Wartefenster über 120 Sekunden wurde akzeptiert.");
        }
        catch (ArgumentOutOfRangeException) { }
    }

    public static void UnchangedTargetsAreNotRestored()
    {
        var display = new FakeTargetedDisplay(); display.ApplyResults.Enqueue(Ok(Receipt(0, "PATH-A", false, 60, 120)));
        var process = Process("C:\\Games\\same.exe"); var processes = new FakeProcesses(process); var monitor = Monitor(process.ExecutablePath, Profile(ProfileRetentionPolicy.Once), display, processes);
        monitor.Start(T0); monitor.Poll(T0); processes.Items.Clear(); monitor.Poll(T0.AddSeconds(1));
        Expect(display.Restores.Count == 0, "ToolChanged=false darf nie restauriert werden.");
    }

    public static void DiagnosticsArePerTarget()
    {
        var display = new FakeTargetedDisplay(); display.ApplyResults.Enqueue(Ok(Receipt(0, "PATH-A", true, 60, 100), Receipt(1, "PATH-B", false, 60, 144)));
        var process = Process("C:\\Games\\diag.exe"); var monitor = Monitor(process.ExecutablePath, Multi(), display, new FakeProcesses(process)); monitor.Start(T0); monitor.Poll(T0);
        var status = monitor.Status; var report = DiagnosticReportFormatter.Format(DiagnosticReportFormatter.Create(status, monitor.DiagnosticEvents, null));
        Expect(status.Targets?.Count == 2 && status.Targets[0].MonitorDevicePath == "PATH-A" && status.Targets[1].MonitorDevicePath == "PATH-B", "Status muss konkrete per-target Pfade enthalten.");
        Expect(report.Contains("Monitorziele") && report.Contains("PATH-A") && report.Contains("PATH-B"), "Diagnose muss mehrere Ziele verständlich ausgeben.");
        Expect(report.Contains("Startart: Extern erkannt"), "Automatisch erkannte Prozesse dürfen nicht als kontrollierter Direktstart bezeichnet werden.");
        Expect(monitor.DiagnosticEvents.Any(item => item.Message.Contains("Profilziel aktiviert: A", StringComparison.Ordinal)
            && item.Message.Contains("Ausgang 1920x1080 @ 60Hz", StringComparison.Ordinal)
            && item.Message.Contains("Ziel 1920x1080 @ 100Hz", StringComparison.Ordinal)
            && item.Message.Contains("Ergebnis angewendet", StringComparison.Ordinal)), "Initiale Aktivierung muss das geänderte Ziel einzeln diagnostizieren.");
        Expect(monitor.DiagnosticEvents.Any(item => item.Message.Contains("Profilziel aktiviert: B", StringComparison.Ordinal)
            && item.Message.Contains("Ergebnis bereits aktiv", StringComparison.Ordinal)), "Auch bereits aktive Ziele müssen im initialen Batch nachvollziehbar sein.");
    }

    private static void RunInitial(DisplayProfile profile)
    {
        var display = new FakeTargetedDisplay(); var process = Process("C:\\Games\\one.exe"); var monitor = Monitor(process.ExecutablePath, profile, display, new FakeProcesses(process)); monitor.Start(T0); monitor.Poll(T0);
        Expect(display.Applies.Count == 1 && monitor.Status.State == ProfileMonitorState.Active, "Einzelziel muss via Target-Service aktiv werden.");
    }
    private static TargetedProfileMonitor Monitor(string path, DisplayProfile profile, FakeTargetedDisplay display, FakeProcesses processes, FakeLauncher? launcher = null, ILaunchPlanResolver? plans = null) => new(new ConcurrentDictionary<string, DisplayProfile>(StringComparer.OrdinalIgnoreCase) { [path] = profile }, display, processes, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1), launcher, _ => true, plans);
    private static DisplayProfile Profile(ProfileRetentionPolicy policy) => new(policy, [Target(new PrimaryMonitor(), 120)]);
    private static DisplayProfile Multi() => new(ProfileRetentionPolicy.Continuous, [Target(new SpecificMonitor("PATH-A", "A"), 100), Target(new SpecificMonitor("PATH-B", "B"), 144)]);
    private static DisplayProfileTarget Target(MonitorSelector selector, uint hz) => new(selector, new DisplayMode { Width = 1920, Height = 1080, Frequency = hz });
    private static ProcessIdentity Process(string path) => new(7, T0, path);
    private static TargetedDisplayReceipt Receipt(int index, string path, bool changed, uint original, uint target) => new(index, path, "DISPLAY" + index, Endpoint(original), Endpoint(target), changed);
    private static EndpointDisplayMode Endpoint(uint hz) => new(1920, 1080, hz, 32, 0, DisplayOrientation.Default, 0);
    private static TargetedDisplayError Error(int index, string path, string message) => new(index, path, TargetedDisplayStage.Apply, TargetedDisplayErrorCode.NativeChangeRejected, message);
    private static TargetedDisplayApplyResult Ok(params TargetedDisplayReceipt[] receipts) => new(true, receipts, [], []);
    private static TargetedDisplayApplyResult Fail(string message) => new(false, [], [Error(0, "PATH-A", message)], []);
    private static void Expect(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    public sealed class FakeTargetedDisplay : ITargetedDisplayService
    {
        public Queue<TargetedDisplayApplyResult> ApplyResults { get; } = new();
        public Queue<TargetedDisplayRestoreResult> RestoreResults { get; } = new();
        public List<IReadOnlyList<DisplayProfileTarget>> Applies { get; } = []; public List<IReadOnlyList<TargetedDisplayReceipt>> Restores { get; } = [];
        public TargetedDisplayApplyResult Apply(IReadOnlyList<DisplayProfileTarget> targets)
        {
            Applies.Add(targets.Select(item => new DisplayProfileTarget(item.MonitorSelector, item.Mode)).ToArray());
            if (ApplyResults.Count > 0) return ApplyResults.Dequeue();
            return Ok(targets.Select((_, index) => Receipt(index, $"PATH-{index}", true, 60, 120)).ToArray());
        }
        public TargetedDisplayRestoreResult Restore(IReadOnlyList<TargetedDisplayReceipt> receipts)
        {
            Restores.Add(receipts.ToArray()); return RestoreResults.Count > 0 ? RestoreResults.Dequeue() : new TargetedDisplayRestoreResult([], []);
        }
    }
    private sealed class FakeProcesses(params ProcessIdentity[] items) : IProcessProvider
    { public List<ProcessIdentity> Items { get; } = [.. items]; public IReadOnlyList<ProcessIdentity> GetCurrentSessionProcesses() => Items.ToArray(); }
    private sealed class FakeLauncher(ProcessIdentity identity) : IApplicationLauncher
    { public int DirectLaunches { get; private set; } public int SteamLaunches { get; private set; } public bool ThrowOnLaunch { get; set; }
      public ILaunchedApplication Launch(string path) { DirectLaunches++; if (ThrowOnLaunch) throw new InvalidOperationException("start failed"); return new Launched(identity); }
      public void LaunchSteam(string uri) => SteamLaunches++;
      private sealed class Launched(ProcessIdentity identity) : ILaunchedApplication { public ProcessIdentity Identity { get; } = identity; public void Dispose() { } } }
    private sealed class StaticPlanResolver(LaunchPlan plan) : ILaunchPlanResolver { public LaunchPlan Resolve(string executablePath) => plan; }
}
