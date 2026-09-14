using System.Collections.Concurrent;
using System.Diagnostics;

namespace DisplayModeSwitcher;

public sealed record ProcessIdentity(int Id, DateTime StartTimeUtc, string ExecutablePath);

public interface ILaunchedApplication : IDisposable
{
    ProcessIdentity Identity { get; }
}

public interface IApplicationLauncher
{
    ILaunchedApplication Launch(string executablePath);
    void LaunchSteam(string steamUri);
}

public sealed class SystemApplicationLauncher : IApplicationLauncher
{
    public ILaunchedApplication Launch(string executablePath)
    {
        var workingDirectory = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(workingDirectory))
            throw new InvalidOperationException("Das Arbeitsverzeichnis der Anwendung konnte nicht bestimmt werden.");

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true
        }) ?? throw new InvalidOperationException("Windows hat keine Prozessinstanz zurückgegeben.");

        try
        {
            return new SystemLaunchedApplication(process, new ProcessIdentity(
                process.Id,
                process.StartTime.ToUniversalTime(),
                ProcessMatcher.CanonicalizePath(executablePath)));
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    public void LaunchSteam(string steamUri)
    {
        // ShellExecute dispatches the official protocol. A returned Steam process is deliberately
        // neither identified nor monitored; only the expected game EXE may become the target.
        using var shellProcess = Process.Start(SteamLaunchInfo.Create(steamUri));
    }

    private sealed class SystemLaunchedApplication(Process process, ProcessIdentity identity) : ILaunchedApplication
    {
        public ProcessIdentity Identity { get; } = identity;
        public void Dispose() => process.Dispose();
    }
}

public sealed record ProfileLaunchResult(
    bool Success,
    string? Error = null,
    string? DisplayError = null,
    string? StartError = null,
    string? RollbackError = null,
    ProcessIdentity? Process = null,
    bool DisplayModeChanged = false)
{
    public static ProfileLaunchResult Started(ProcessIdentity process, bool displayModeChanged) =>
        new(true, Process: process, DisplayModeChanged: displayModeChanged);

    public static ProfileLaunchResult Failed(
        string error,
        string? displayError = null,
        string? startError = null,
        string? rollbackError = null,
        bool displayModeChanged = false) =>
        new(false, error, displayError, startError, rollbackError, DisplayModeChanged: displayModeChanged);
}

public interface IProcessProvider
{
    IReadOnlyList<ProcessIdentity> GetCurrentSessionProcesses();
}

public sealed class SystemProcessProvider : IProcessProvider
{
    public IReadOnlyList<ProcessIdentity> GetCurrentSessionProcesses()
    {
        using var current = Process.GetCurrentProcess();
        var currentSession = current.SessionId;
        var result = new List<ProcessIdentity>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.SessionId != currentSession)
                        continue;

                    var path = process.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(path))
                        continue;

                    result.Add(new ProcessIdentity(process.Id, process.StartTime.ToUniversalTime(), ProcessMatcher.CanonicalizePath(path)));
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    // Geschützte oder bereits beendete Prozesse sind für Profile nicht verwendbar.
                }
            }
        }

        return result;
    }
}

public static class ProcessMatcher
{
    public static string CanonicalizePath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));

    public static IReadOnlyList<ProcessIdentity> FindMatches(string profileProcess, IReadOnlyList<ProcessIdentity> processes)
    {
        if (string.IsNullOrWhiteSpace(profileProcess))
            return Array.Empty<ProcessIdentity>();

        if (Path.IsPathFullyQualified(profileProcess))
        {
            string expected;
            try { expected = CanonicalizePath(profileProcess); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return Array.Empty<ProcessIdentity>();
            }

            return processes.Where(process => string.Equals(process.ExecutablePath, expected, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var legacyName = Path.GetFileName(profileProcess);
        return processes.Where(process => string.Equals(Path.GetFileName(process.ExecutablePath), legacyName, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}

public enum ProfileMonitorState { Idle, Launching, PendingLaunch, Active, RetryPending, Restoring, Ambiguous, Error, Stopped }

public sealed record ProfileMonitorStatus(
    ProfileMonitorState State,
    string Message,
    string? ProfileProcess = null,
    DisplayMode? TargetMode = null,
    DateTime? LastChangedUtc = null,
    string? LastResult = null,
    int RetryCount = 0,
    LaunchStrategy? LaunchStrategy = null,
    ProfileRetentionPolicy? Policy = null,
    int ReapplyCount = 0,
    IReadOnlyList<ProfileMonitorTargetStatus>? Targets = null,
    bool ExternallyDetected = false)
{
    public static ProfileMonitorStatus Idle { get; } = new(ProfileMonitorState.Idle, "Wartet auf einen Profilprozess.");
}

/// <summary>Snapshot eines konkreten, bereits aufgelösten Profilziels. Der Pfad
/// ist Diagnoseinformation und wird hier niemals erneut auf einen Monitor gebunden.</summary>
public sealed record ProfileMonitorTargetStatus(
    string? MonitorDevicePath,
    string Selector,
    DisplayMode Target,
    DisplayMode? Original,
    bool ToolChanged,
    string? ApplyError = null,
    string? RestoreError = null);

/// <summary>
/// Legacy global-primary monitor retained only for the pre-targeted regression
/// seam. Production <see cref="ProfileManager"/> constructs
/// <see cref="TargetedProfileMonitor"/> exclusively.
/// </summary>
internal sealed class ProfileMonitor
{
    private static readonly TimeSpan MaximumPendingLaunchTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan StartupStabilizationWindow = TimeSpan.FromSeconds(30);
    private const int MaximumStartupReapplyCount = 3;
    private readonly ConcurrentDictionary<string, DisplayProfile> _profiles;
    private readonly IDisplayService _display;
    private readonly IProcessProvider _processes;
    private readonly IApplicationLauncher _launcher;
    private readonly ILaunchPlanResolver _launchPlans;
    private readonly Func<string, bool> _fileExists;
    private readonly TimeSpan _retryInterval;
    private readonly TimeSpan _verificationInterval;
    private readonly TimeSpan _pendingLaunchTimeout;
    private readonly TimeSpan _launchTimeTolerance;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private ActiveProfile? _active;
    private DateTime _nextActionUtc;
    private ProfileMonitorStatus _status = ProfileMonitorStatus.Idle;
    private readonly DiagnosticEventBuffer _events = new();
    private int _retryCount;
    private bool _stopped;
    private bool _stopRequested;

    private sealed class ActiveProfile
    {
        public required string ProfileProcess { get; init; }
        public ProcessIdentity? Process { get; set; }
        public required LaunchPlan LaunchPlan { get; init; }
        public required DisplayMode Target { get; init; }
        public required DisplayMode Original { get; init; }
        public required ProfileRetentionPolicy Policy { get; init; }
        public DateTime? LaunchRequestedUtc { get; init; }
        public DateTime? PendingUntilUtc { get; init; }
        public DateTime? ActivatedUtc { get; set; }
        public int ReapplyCount { get; set; }
        public int PendingReapplyCount { get; set; }
        public int PendingDisplayFailureCount { get; set; }
        public bool ToolChangedMode { get; set; }
        public bool ProcessExited { get; set; }
        public bool PolicySuppressionLogged { get; set; }
    }

    public ProfileMonitor(
        ConcurrentDictionary<string, DisplayProfile> profiles,
        IDisplayService display,
        IProcessProvider processes,
        TimeSpan? retryInterval = null,
        TimeSpan? verificationInterval = null,
        IApplicationLauncher? launcher = null,
        Func<string, bool>? fileExists = null,
        ILaunchPlanResolver? launchPlans = null,
        TimeSpan? pendingLaunchTimeout = null,
        TimeSpan? launchTimeTolerance = null)
    {
        _profiles = profiles;
        _display = display;
        _processes = processes;
        _launcher = launcher ?? new SystemApplicationLauncher();
        _launchPlans = launchPlans ?? new SteamAwareLaunchPlanResolver();
        _fileExists = fileExists ?? File.Exists;
        _retryInterval = retryInterval ?? TimeSpan.FromSeconds(5);
        _verificationInterval = verificationInterval ?? TimeSpan.FromSeconds(5);
        _pendingLaunchTimeout = pendingLaunchTimeout ?? MaximumPendingLaunchTimeout;
        if (_pendingLaunchTimeout <= TimeSpan.Zero || _pendingLaunchTimeout > MaximumPendingLaunchTimeout)
            throw new ArgumentOutOfRangeException(nameof(pendingLaunchTimeout), "Das Steam-Wartefenster muss größer als null und darf höchstens 120 Sekunden lang sein.");
        _launchTimeTolerance = launchTimeTolerance ?? TimeSpan.FromSeconds(2);
    }

    public ProfileMonitorStatus Status
    {
        get { return Volatile.Read(ref _status); }
    }

    public IReadOnlyList<ProfileMonitorDiagnosticEvent> DiagnosticEvents => _events.Snapshot();

    public LaunchPlan ResolveLaunchPlan(string executablePath) => _launchPlans.Resolve(executablePath);

    public ProfileLaunchResult LaunchProfileApplication(string profileProcess, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(profileProcess) || !Path.IsPathFullyQualified(profileProcess))
            return ProfileLaunchResult.Failed("Legacy-Profile ohne vollständigen EXE-Pfad können nicht gestartet werden.");

        string canonicalPath;
        try { canonicalPath = ProcessMatcher.CanonicalizePath(profileProcess); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ProfileLaunchResult.Failed($"Der Profilpfad ist ungültig: {ex.Message}");
        }

        _operationGate.Wait();
        try
        {
            if (_stopped)
                return ProfileLaunchResult.Failed("Die Profilüberwachung wurde bereits beendet.");
            if (!_profiles.TryGetValue(canonicalPath, out var profile))
                return ProfileLaunchResult.Failed("Das ausgewählte Profil ist nicht mehr vorhanden.");
            if (!DisplayProfileCompatibility.TryGetCurrentPrimaryTarget(profile, out var primaryTarget))
            {
                SetStatus(ProfileMonitorState.Error, DisplayProfileCompatibility.UnsupportedTargetsMessage,
                    canonicalPath, null, DisplayProfileCompatibility.UnsupportedTargetsMessage, utcNow, policy: profile.Policy);
                AddEvent(utcNow, $"Profilstart abgelehnt: {DisplayProfileCompatibility.UnsupportedTargetsMessage}");
                return ProfileLaunchResult.Failed(DisplayProfileCompatibility.UnsupportedTargetsMessage);
            }
            var target = primaryTarget!.Mode;
            if (!_fileExists(canonicalPath))
                return ProfileLaunchResult.Failed("Die EXE-Datei des ausgewählten Profils wurde nicht gefunden.");
            if (_active is not null)
                return ProfileLaunchResult.Failed("Es ist bereits ein Profil aktiv. Beenden Sie zuerst dessen Anwendung.");

            IReadOnlyList<ProcessIdentity> running;
            try { running = _processes.GetCurrentSessionProcesses(); }
            catch (Exception ex)
            {
                return ProfileLaunchResult.Failed($"Laufende Profilprozesse konnten nicht geprüft werden: {ex.Message}");
            }
            if (_profiles.Any(profile => ProcessMatcher.FindMatches(profile.Key, running).Count > 0))
                return ProfileLaunchResult.Failed("Es läuft bereits eine Anwendung mit Profil. Beenden Sie diese zuerst.");

            LaunchPlan launchPlan;
            try { launchPlan = _launchPlans.Resolve(canonicalPath); }
            catch
            {
                launchPlan = LaunchPlan.Direct(canonicalPath);
            }
            if (!string.Equals(launchPlan.ExpectedExecutablePath, canonicalPath, StringComparison.OrdinalIgnoreCase) ||
                launchPlan.Strategy is not (LaunchStrategy.Direct or LaunchStrategy.Steam) ||
                launchPlan.Strategy == LaunchStrategy.Steam && !IsValidSteamLaunchPlan(launchPlan))
                launchPlan = LaunchPlan.Direct(canonicalPath);
            AddEvent(utcNow, $"Startstrategie gewählt: {launchPlan.DisplayName}.");
            if (launchPlan.Strategy == LaunchStrategy.Steam)
                AddEvent(utcNow, $"Steam-App-ID: {launchPlan.SteamAppId}.");

            DisplayMode? original;
            try { original = _display.GetCurrentDisplayMode(); }
            catch (Exception ex)
            {
                AddEvent(utcNow, $"Vorabmodus konnte nicht gelesen werden: {ex.Message}");
                return FailLaunchState(canonicalPath, target, profile.Policy, "Der aktuelle Anzeigemodus konnte nicht gelesen werden.", ex.Message, utcNow);
            }
            if (original is null)
            {
                AddEvent(utcNow, "Vorabmodus konnte nicht gelesen werden.");
                return FailLaunchState(canonicalPath, target, profile.Policy, "Der aktuelle Anzeigemodus konnte nicht gelesen werden.", "Aktueller Anzeigemodus nicht lesbar", utcNow);
            }

            SetStatus(ProfileMonitorState.Launching, $"Profilmodus für '{canonicalPath}' wird vorbereitet.", canonicalPath, target, null, utcNow, launchPlan.Strategy, profile.Policy);
            _retryCount = 0;
            AddEvent(utcNow, $"Vorabmodus vor Anwendungsstart: {DiagnosticReportFormatter.FormatMode(original)}.");

            var changedMode = !DisplayModeEquivalence.AreEquivalent(original, target);
            if (changedMode)
            {
                OperationResult setResult;
                try { setResult = _display.SetDisplayMode(target); }
                catch (Exception ex) { setResult = OperationResult.Fail(ex.Message); }
                if (!setResult.Success)
                {
                    var error = setResult.Error ?? "Unbekannter Fehler beim Setzen des Profilmodus.";
                    AddEvent(utcNow, $"Modusabweichung vor Anwendungsstart: beobachtet {DiagnosticReportFormatter.FormatMode(original)}, Ziel {DiagnosticReportFormatter.FormatMode(target)}, Richtlinie {ProfileRetentionPolicyText.ToDisplayName(profile.Policy)}, initiale Aktivierung – fehlgeschlagen: {error}");
                    return FailLaunchState(canonicalPath, target, profile.Policy, $"Der Profilmodus konnte nicht gesetzt werden: {error}", error, utcNow);
                }

                DisplayMode? confirmed;
                try { confirmed = _display.GetCurrentDisplayMode(); }
                catch (Exception ex)
                {
                    return FailAfterUnconfirmedSet(canonicalPath, target, original, profile.Policy, $"Bestätigung des Profilmodus fehlgeschlagen: {ex.Message}", utcNow);
                }
                if (!DisplayModeEquivalence.AreEquivalent(confirmed, target))
                    return FailAfterUnconfirmedSet(canonicalPath, target, original, profile.Policy, "Der gesetzte Profilmodus konnte nicht bestätigt werden.", utcNow);

                AddEvent(utcNow, $"Modusabweichung vor Anwendungsstart: beobachtet {DiagnosticReportFormatter.FormatMode(original)}, Ziel {DiagnosticReportFormatter.FormatMode(target)}, Richtlinie {ProfileRetentionPolicyText.ToDisplayName(profile.Policy)}, initiale Aktivierung – erfolgreich.");
            }
            else
            {
                AddEvent(utcNow, "Profilmodus war vor Anwendungsstart bereits aktiv.");
            }

            return launchPlan.Strategy == LaunchStrategy.Steam
                ? StartSteamLaunch(launchPlan, target, original, profile.Policy, changedMode, utcNow)
                : StartDirectLaunch(launchPlan, target, original, profile.Policy, changedMode, utcNow);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private ProfileLaunchResult StartDirectLaunch(
        LaunchPlan launchPlan,
        DisplayMode target,
        DisplayMode original,
        ProfileRetentionPolicy policy,
        bool changedMode,
        DateTime utcNow)
    {
        ILaunchedApplication? launched = null;
        try
        {
            launched = _launcher.Launch(launchPlan.ExpectedExecutablePath);
            var identity = launched.Identity;
            if (identity.Id <= 0 || identity.StartTimeUtc == default)
                throw new InvalidOperationException("Windows hat keine eindeutig überwachbare Prozessinstanz zurückgegeben.");
            if (!string.Equals(ProcessMatcher.CanonicalizePath(identity.ExecutablePath), launchPlan.ExpectedExecutablePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Die gestartete Prozessinstanz gehört nicht zur ausgewählten EXE.");

            _active = new ActiveProfile
            {
                ProfileProcess = launchPlan.ExpectedExecutablePath,
                Process = identity,
                LaunchPlan = launchPlan,
                Target = target,
                Original = original,
                Policy = policy,
                ActivatedUtc = utcNow,
                ToolChangedMode = changedMode
            };
            _retryCount = 0;
            _nextActionUtc = utcNow + _verificationInterval;
            SetStatus(ProfileMonitorState.Active, $"Profilmodus für '{launchPlan.ExpectedExecutablePath}' ist aktiv.",
                launchPlan.ExpectedExecutablePath, target, $"Anwendung gestartet (PID {identity.Id})", utcNow, launchPlan.Strategy);
            AddEvent(utcNow, $"Anwendung direkt im Profilmodus gestartet: PID {identity.Id}.");
            return ProfileLaunchResult.Started(identity, changedMode);
        }
        catch (Exception ex)
        {
            return FailApplicationStart(launchPlan, target, original, policy, changedMode, ex, utcNow);
        }
        finally
        {
            try { launched?.Dispose(); }
            catch { }
        }
    }

    private ProfileLaunchResult StartSteamLaunch(
        LaunchPlan launchPlan,
        DisplayMode target,
        DisplayMode original,
        ProfileRetentionPolicy policy,
        bool changedMode,
        DateTime utcNow)
    {
        try
        {
            if (!IsValidSteamLaunchPlan(launchPlan))
                throw new InvalidOperationException("Der Steam-Startplan ist unvollständig.");

            _launcher.LaunchSteam(launchPlan.SteamUri!);
            _active = new ActiveProfile
            {
                ProfileProcess = launchPlan.ExpectedExecutablePath,
                LaunchPlan = launchPlan,
                Target = target,
                Original = original,
                Policy = policy,
                LaunchRequestedUtc = utcNow,
                PendingUntilUtc = utcNow + _pendingLaunchTimeout,
                ToolChangedMode = changedMode
            };
            _retryCount = 0;
            _nextActionUtc = utcNow;
            SetStatus(ProfileMonitorState.PendingLaunch,
                $"Steam-Start ausgelöst; wartet auf '{launchPlan.ExpectedExecutablePath}'.",
                launchPlan.ExpectedExecutablePath, target, "Wartet auf Zielprozess", utcNow, launchPlan.Strategy);
            AddEvent(utcNow, "Steam-Start über das offizielle URI-Protokoll ausgelöst.");
            AddEvent(utcNow, $"Wartet auf den eindeutigen Zielprozess: {launchPlan.ExpectedExecutablePath}.");
            return new ProfileLaunchResult(true, Process: null, DisplayModeChanged: changedMode);
        }
        catch (Exception ex)
        {
            return FailApplicationStart(launchPlan, target, original, policy, changedMode, ex, utcNow);
        }
    }

    private static bool IsValidSteamLaunchPlan(LaunchPlan launchPlan)
    {
        if (string.IsNullOrEmpty(launchPlan.SteamAppId) ||
            launchPlan.SteamAppId.Any(character => !char.IsAsciiDigit(character)))
            return false;

        return string.Equals(
            launchPlan.SteamUri,
            SteamLaunchInfo.CreateUri(launchPlan.SteamAppId),
            StringComparison.Ordinal);
    }

    private ProfileLaunchResult FailApplicationStart(
        LaunchPlan launchPlan,
        DisplayMode target,
        DisplayMode original,
        ProfileRetentionPolicy policy,
        bool changedMode,
        Exception exception,
        DateTime utcNow)
    {
        AddEvent(utcNow, $"Anwendungsstart fehlgeschlagen: {exception.Message}");
        string? rollbackError = null;
        if (changedMode)
        {
            rollbackError = TryRestoreMode(original);
            AddEvent(utcNow, rollbackError is null
                ? "Originalmodus nach Startfehler wiederhergestellt."
                : $"Wiederherstellung nach Startfehler fehlgeschlagen: {rollbackError}");
        }

        var error = rollbackError is null
            ? $"Die Anwendung konnte nicht gestartet werden: {exception.Message}"
            : $"Die Anwendung konnte nicht gestartet werden: {exception.Message} Der Originalmodus konnte ebenfalls nicht wiederhergestellt werden: {rollbackError}";
        if (rollbackError is null)
            SetStatus(ProfileMonitorState.Error, error, launchPlan.ExpectedExecutablePath, target, exception.Message, utcNow, launchPlan.Strategy, policy);
        else
            QueuePendingRestore(launchPlan, target, original, policy, rollbackError, utcNow);
        return ProfileLaunchResult.Failed(error, startError: exception.Message, rollbackError: rollbackError, displayModeChanged: changedMode);
    }

    public void Start(DateTime utcNow)
    {
        _operationGate.Wait();
        try
        {
            _stopped = false;
            _stopRequested = false;
            SetStatus(ProfileMonitorState.Idle, "Wartet auf einen Profilprozess.", null, null, null, utcNow);
            AddEvent(utcNow, "Profilüberwachung gestartet.");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public void Poll(DateTime utcNow)
    {
        _operationGate.Wait();
        try
        {
            if (utcNow < _nextActionUtc)
                return;

            var running = _processes.GetCurrentSessionProcesses();
            if (_active is null)
            {
                TryActivate(running, utcNow);
                return;
            }


            if (IsPendingLaunch(_active))
            {
                PollPendingLaunch(running, utcNow);
                return;
            }

            if (_active.ProcessExited || _active.Process is null ||
                !running.Any(process => process.Id == _active.Process.Id &&
                    process.StartTimeUtc == _active.Process.StartTimeUtc &&
                    string.Equals(process.ExecutablePath, _active.Process.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
            {
                if (!_active.ProcessExited)
                    AddEvent(utcNow, $"Profilprozess beendet: {_active.ProfileProcess}.");
                _active.ProcessExited = true;
                TryRestore(utcNow);
                return;
            }

            EnsureTargetMode(utcNow);
        }
        catch (Exception ex)
        {
            _nextActionUtc = utcNow + _retryInterval;
            SetStatus(ProfileMonitorState.Error, $"Profilüberwachung fehlgeschlagen: {ex.Message}", _active?.ProfileProcess, _active?.Target, ex.Message, utcNow, _active?.LaunchPlan.Strategy);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private static bool IsPendingLaunch(ActiveProfile? active) =>
        active is { Process: null, ProcessExited: false, LaunchRequestedUtc: not null, PendingUntilUtc: not null };

    private void PollPendingLaunch(IReadOnlyList<ProcessIdentity> running, DateTime utcNow)
    {
        var active = _active ?? throw new InvalidOperationException("Ein ausstehender Profilstart wurde erwartet.");
        var earliestStart = active.LaunchRequestedUtc!.Value - _launchTimeTolerance;
        var latestPlausibleStart = utcNow + _launchTimeTolerance;
        var matches = ProcessMatcher.FindMatches(active.ProfileProcess, running)
            .Where(process => process.Id > 0 && process.StartTimeUtc != default &&
                process.StartTimeUtc >= earliestStart && process.StartTimeUtc <= latestPlausibleStart)
            .ToList();

        if (matches.Count == 1)
        {
            active.Process = matches[0];
            // Die aktive 30-Sekunden-Startphase beginnt erst mit Übernahme des echten
            // Spielprozesses. Nachsetzungen aus der Steam-Wartephase zählen nicht mit.
            active.ActivatedUtc = utcNow;
            active.ReapplyCount = 0;
            active.PolicySuppressionLogged = false;
            active.PendingDisplayFailureCount = 0;
            _retryCount = 0;
            _nextActionUtc = utcNow + _verificationInterval;
            SetStatus(ProfileMonitorState.Active,
                $"Profilmodus für '{active.ProfileProcess}' ist aktiv.",
                active.ProfileProcess, active.Target, $"Steam-Zielprozess übernommen (PID {active.Process.Id})", utcNow, active.LaunchPlan.Strategy);
            AddEvent(utcNow, $"Steam-Zielprozess übernommen: PID {active.Process.Id}.");
            return;
        }

        if (utcNow >= active.PendingUntilUtc)
        {
            AbortPendingLaunch("Zeitüberschreitung: Der Steam-Zielprozess wurde innerhalb von 120 Sekunden nicht erkannt.", utcNow);
            return;
        }

        DisplayMode? current;
        try { current = _display.GetCurrentDisplayMode(); }
        catch (Exception ex)
        {
            HandlePendingDisplayFailure($"Profilmodus konnte während des Steam-Starts nicht geprüft werden: {ex.Message}", utcNow);
            return;
        }

        if (DisplayModeEquivalence.AreEquivalent(current, active.Target))
        {
            active.PendingDisplayFailureCount = 0;
            _nextActionUtc = utcNow + _verificationInterval;
            SetStatus(ProfileMonitorState.PendingLaunch,
                $"Steam-Start ausgelöst; wartet auf '{active.ProfileProcess}'.",
                active.ProfileProcess, active.Target, "Wartet auf Zielprozess", utcNow, active.LaunchPlan.Strategy);
            return;
        }

        OperationResult result;
        active.PendingReapplyCount++;
        try { result = _display.SetDisplayMode(active.Target); }
        catch (Exception ex) { result = OperationResult.Fail(ex.Message); }
        if (result.Success)
        {
            active.ToolChangedMode = true;
            active.PendingDisplayFailureCount = 0;
            _retryCount = 0;
            _nextActionUtc = utcNow + _verificationInterval;
            SetStatus(ProfileMonitorState.PendingLaunch,
                $"Steam-Start ausgelöst; Zielmodus wurde während des Wartens erneut angewendet.",
                active.ProfileProcess, active.Target, "Zielmodus erneut angewendet", utcNow, active.LaunchPlan.Strategy);
            AddEvent(utcNow, FormatPendingDeviation(current, active, "erfolgreich"));
            return;
        }

        HandlePendingDisplayFailure(result.Error ?? "Unbekannter Anzeigemodusfehler.", utcNow, current, wasSetAttempted: true);
    }

    private void HandlePendingDisplayFailure(string error, DateTime utcNow, DisplayMode? observed = null, bool wasSetAttempted = false)
    {
        var active = _active ?? throw new InvalidOperationException("Ein ausstehender Profilstart wurde erwartet.");
        active.PendingDisplayFailureCount++;
        _retryCount++;
        AddEvent(utcNow, wasSetAttempted
            ? $"{FormatPendingDeviation(observed, active, "fehlgeschlagen")}: {error}"
            : $"Steam-Wartephase: Zielmodus konnte nicht geprüft werden; Richtlinie {ProfileRetentionPolicyText.ToDisplayName(active.Policy)}: {error}");
        if (active.PendingDisplayFailureCount >= 3)
        {
            AbortPendingLaunch($"Steam-Start abgebrochen: Der Zielmodus konnte dauerhaft nicht gehalten werden ({error}).", utcNow);
            return;
        }

        _nextActionUtc = utcNow + _retryInterval;
        SetStatus(ProfileMonitorState.PendingLaunch,
            $"Wartet auf Steam-Zielprozess; Zielmodus konnte nicht gehalten werden: {error}",
            active.ProfileProcess, active.Target, error, utcNow, active.LaunchPlan.Strategy);
    }

    private void AbortPendingLaunch(string reason, DateTime utcNow)
    {
        var active = _active ?? throw new InvalidOperationException("Ein ausstehender Profilstart wurde erwartet.");
        AddEvent(utcNow, reason);
        active.ProcessExited = true;
        TryRestore(utcNow);
    }

    public OperationResult Stop()
    {
        _operationGate.Wait();
        try
        {
            _stopRequested = true;
            if (_active is null || !_active.ToolChangedMode)
            {
                _active = null;
                SetStopped(DateTime.UtcNow);
                return OperationResult.Ok();
            }

            _active.ProcessExited = true;
            OperationResult result;
            try
            {
                result = RestoreOriginal();
            }
            catch (Exception ex)
            {
                result = OperationResult.Fail(ex.Message);
            }
            if (result.Success)
            {
                _active = null;
                SetStopped(DateTime.UtcNow);
            }
            else
            {
                _retryCount++;
                _nextActionUtc = DateTime.UtcNow + _retryInterval;
                SetStatus(ProfileMonitorState.Restoring, $"Originalmodus konnte beim Beenden nicht wiederhergestellt werden: {result.Error} Neuer Versuch folgt.", _active.ProfileProcess, _active.Target, result.Error, DateTime.UtcNow, _active.LaunchPlan.Strategy);
                AddEvent(DateTime.UtcNow, $"Wiederherstellung beim Beenden fehlgeschlagen: {result.Error}");
            }

            return result;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private void TryActivate(IReadOnlyList<ProcessIdentity> running, DateTime utcNow)
    {
        var candidates = new List<(string Profile, DisplayProfile Definition, DisplayProfileTarget Target, ProcessIdentity Process)>();
        foreach (var profile in _profiles.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            var matches = ProcessMatcher.FindMatches(profile.Key, running);
            DisplayProfileTarget? supportedTarget = null;
            if (matches.Count > 0 && !DisplayProfileCompatibility.TryGetCurrentPrimaryTarget(profile.Value, out supportedTarget))
            {
                var alreadyUnsupported = _status.State == ProfileMonitorState.Error &&
                    string.Equals(_status.ProfileProcess, profile.Key, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(_status.LastResult, DisplayProfileCompatibility.UnsupportedTargetsMessage, StringComparison.Ordinal);
                SetStatus(ProfileMonitorState.Error, DisplayProfileCompatibility.UnsupportedTargetsMessage,
                    profile.Key, null, DisplayProfileCompatibility.UnsupportedTargetsMessage, utcNow, policy: profile.Value.Policy);
                if (!alreadyUnsupported)
                    AddEvent(utcNow, $"Automatische Profilaktivierung abgelehnt: {DisplayProfileCompatibility.UnsupportedTargetsMessage}");
                return;
            }
            if (matches.Count > 1)
            {
                var alreadyAmbiguous = _status.State == ProfileMonitorState.Ambiguous &&
                    string.Equals(_status.ProfileProcess, profile.Key, StringComparison.OrdinalIgnoreCase);
                SetStatus(ProfileMonitorState.Ambiguous,
                    $"Profil '{profile.Key}' passt zu mehreren laufenden Prozessen; es wird nicht automatisch geschaltet.", profile.Key, supportedTarget!.Mode, null, utcNow, policy: profile.Value.Policy);
                if (!alreadyAmbiguous)
                    AddEvent(utcNow, $"Mehrdeutiges Legacy-Profil erkannt: {profile.Key}.");
                return;
            }
            if (matches.Count == 1)
                candidates.Add((profile.Key, profile.Value, supportedTarget!, matches[0]));
        }

        if (candidates.Count == 0)
        {
            SetStatus(ProfileMonitorState.Idle, "Wartet auf einen Profilprozess.", null, null, null, utcNow);
            return;
        }

        if (candidates.Count > 1)
        {
            SetStatus(ProfileMonitorState.Ambiguous, "Mehrere Profile sind gleichzeitig aktiv; es wird nicht automatisch geschaltet.", null, null, null, utcNow);
            return;
        }

        var candidate = candidates[0];
        DisplayMode? original;
        try { original = _display.GetCurrentDisplayMode(); }
        catch (Exception ex)
        {
            _nextActionUtc = utcNow + _retryInterval;
            _retryCount++;
            SetStatus(ProfileMonitorState.Error, $"Der aktuelle Anzeigemodus konnte nicht gelesen werden: {ex.Message}",
                candidate.Profile, candidate.Target.Mode, ex.Message, utcNow, policy: candidate.Definition.Policy);
            AddEvent(utcNow, $"Aktueller Anzeigemodus für Profil '{candidate.Profile}' konnte nicht gelesen werden: {ex.Message}");
            return;
        }
        if (original is null)
        {
            _nextActionUtc = utcNow + _retryInterval;
            SetStatus(ProfileMonitorState.Error, "Der aktuelle Anzeigemodus konnte nicht gelesen werden.", candidate.Profile, candidate.Target.Mode, "Aktueller Anzeigemodus nicht lesbar", utcNow, policy: candidate.Definition.Policy);
            return;
        }

        _active = new ActiveProfile
        {
            ProfileProcess = candidate.Profile,
            Process = candidate.Process,
            LaunchPlan = LaunchPlan.Direct(candidate.Profile),
            Target = candidate.Target.Mode,
            Original = original,
            Policy = candidate.Definition.Policy
        };
        AddEvent(utcNow, $"Profilprozess erkannt: {candidate.Profile}.");
        EnsureTargetMode(utcNow);
    }

    private void EnsureTargetMode(DateTime utcNow)
    {
        var active = _active ?? throw new InvalidOperationException("Ein aktives Profil wurde erwartet.");
        DisplayMode? current;
        try { current = _display.GetCurrentDisplayMode(); }
        catch (Exception ex)
        {
            _nextActionUtc = utcNow + _retryInterval;
            _retryCount++;
            SetStatus(ProfileMonitorState.RetryPending,
                $"Profilmodus für '{active.ProfileProcess}' konnte nicht geprüft werden: {ex.Message} Neuer Versuch folgt.",
                active.ProfileProcess, active.Target, ex.Message, utcNow, active.LaunchPlan.Strategy);
            AddEvent(utcNow, $"Profilmodus konnte nicht geprüft werden; Ziel {DiagnosticReportFormatter.FormatMode(active.Target)}, Richtlinie {ProfileRetentionPolicyText.ToDisplayName(active.Policy)}: {ex.Message}");
            return;
        }
        if (DisplayModeEquivalence.AreEquivalent(current, active.Target))
        {
            active.ActivatedUtc ??= utcNow;
            _nextActionUtc = utcNow + _verificationInterval;
            SetStatus(ProfileMonitorState.Active, $"Profilmodus für '{active.ProfileProcess}' ist aktiv.", active.ProfileProcess, active.Target, "Zielmodus aktiv", utcNow, active.LaunchPlan.Strategy);
            return;
        }

        var isReapply = active.ActivatedUtc is not null;
        if (isReapply && !MayReapply(active, utcNow, out var suppressionReason))
        {
            _nextActionUtc = utcNow + _verificationInterval;
            SetStatus(ProfileMonitorState.Active,
                $"Profil '{active.ProfileProcess}' bleibt aktiv; der abweichende Modus wird gemäß Richtlinie nicht nachgesetzt.",
                active.ProfileProcess, active.Target, suppressionReason, utcNow, active.LaunchPlan.Strategy);
            if (!active.PolicySuppressionLogged)
            {
                active.PolicySuppressionLogged = true;
                AddEvent(utcNow, $"Modusabweichung nicht nachgesetzt: beobachtet {DiagnosticReportFormatter.FormatMode(current)}, Ziel {DiagnosticReportFormatter.FormatMode(active.Target)}, Richtlinie {ProfileRetentionPolicyText.ToDisplayName(active.Policy)}, Nachsetzungen {FormatReapplyCount(active)}; {suppressionReason}");
            }
            return;
        }

        if (isReapply)
            active.ReapplyCount++;

        OperationResult result;
        try { result = _display.SetDisplayMode(active.Target); }
        catch (Exception ex) { result = OperationResult.Fail(ex.Message); }
        if (result.Success)
        {
            active.ToolChangedMode = true;
            active.ActivatedUtc ??= utcNow;
            _nextActionUtc = utcNow + _verificationInterval;
            _retryCount = 0;
            SetStatus(ProfileMonitorState.Active, $"Profilmodus für '{active.ProfileProcess}' wurde angewendet.", active.ProfileProcess, active.Target, "Anwenden erfolgreich", utcNow, active.LaunchPlan.Strategy);
            AddEvent(utcNow, FormatDeviationDecision(current, active, isReapply, "erfolgreich"));
        }
        else
        {
            _nextActionUtc = utcNow + _retryInterval;
            _retryCount++;
            SetStatus(ProfileMonitorState.RetryPending,
                $"Profilmodus für '{active.ProfileProcess}' konnte nicht angewendet werden: {result.Error} Neuer Versuch folgt.", active.ProfileProcess, active.Target, result.Error, utcNow, active.LaunchPlan.Strategy);
            AddEvent(utcNow, $"{FormatDeviationDecision(current, active, isReapply, "fehlgeschlagen")}: {result.Error}");
        }
    }

    private static bool MayReapply(ActiveProfile active, DateTime utcNow, out string reason)
    {
        switch (active.Policy)
        {
            case ProfileRetentionPolicy.Once:
                reason = "Einmalige Aktivierung abgeschlossen.";
                return false;
            case ProfileRetentionPolicy.Startup when utcNow >= active.ActivatedUtc!.Value + StartupStabilizationWindow:
                reason = "Die 30-Sekunden-Startphase ist beendet.";
                return false;
            case ProfileRetentionPolicy.Startup when active.ReapplyCount >= MaximumStartupReapplyCount:
                reason = "Das Limit von 3 Nachsetzungen ist erreicht.";
                return false;
            case ProfileRetentionPolicy.Startup:
            case ProfileRetentionPolicy.Continuous:
                reason = string.Empty;
                return true;
            default:
                throw new InvalidOperationException("Das aktive Profil besitzt eine unbekannte Richtlinie.");
        }
    }

    private static string FormatDeviationDecision(DisplayMode? observed, ActiveProfile active, bool isReapply, string result)
    {
        var action = isReapply ? $"Nachsetzung {FormatReapplyCount(active)}" : "initiale Aktivierung";
        return $"Modusabweichung: beobachtet {DiagnosticReportFormatter.FormatMode(observed)}, Ziel {DiagnosticReportFormatter.FormatMode(active.Target)}, Richtlinie {ProfileRetentionPolicyText.ToDisplayName(active.Policy)}, {action} – {result}";
    }

    private static string FormatPendingDeviation(DisplayMode? observed, ActiveProfile active, string result) =>
        $"Modusabweichung während Steam-Wartephase: beobachtet {DiagnosticReportFormatter.FormatMode(observed)}, Ziel {DiagnosticReportFormatter.FormatMode(active.Target)}, Richtlinie {ProfileRetentionPolicyText.ToDisplayName(active.Policy)} (aktive Richtlinie beginnt nach Prozessübernahme), Nachsetzung {active.PendingReapplyCount}/unbegrenzt (maximal 3 aufeinanderfolgende Displayfehler) – {result}";

    private static string FormatReapplyCount(ActiveProfile active) => active.Policy switch
    {
        ProfileRetentionPolicy.Startup => $"{active.ReapplyCount}/{MaximumStartupReapplyCount}",
        ProfileRetentionPolicy.Once => $"{active.ReapplyCount}/0",
        ProfileRetentionPolicy.Continuous => $"{active.ReapplyCount}/unbegrenzt",
        _ => active.ReapplyCount.ToString()
    };

    private void TryRestore(DateTime utcNow)
    {
        var active = _active ?? throw new InvalidOperationException("Ein aktives Profil wurde erwartet.");
        if (!active.ToolChangedMode)
        {
            _active = null;
            SetStatus(ProfileMonitorState.Idle, "Wartet auf einen Profilprozess.", null, null, "Keine Wiederherstellung erforderlich", utcNow);
            return;
        }

        var result = RestoreOriginal();
        if (result.Success)
        {
            _active = null;
            AddEvent(utcNow, $"Originalmodus wiederhergestellt: {active.ProfileProcess}.");
            if (_stopRequested)
                SetStopped(utcNow);
            else
                SetStatus(ProfileMonitorState.Idle, "Wartet auf einen Profilprozess.", null, null, "Wiederherstellung erfolgreich", utcNow);
        }
        else
        {
            _nextActionUtc = utcNow + _retryInterval;
            _retryCount++;
            SetStatus(ProfileMonitorState.Restoring, $"Originalmodus konnte nicht wiederhergestellt werden: {result.Error} Neuer Versuch folgt.", active.ProfileProcess, active.Target, result.Error, utcNow, active.LaunchPlan.Strategy);
            AddEvent(utcNow, $"Wiederherstellung fehlgeschlagen: {result.Error}");
        }
    }

    private ProfileLaunchResult FailAfterUnconfirmedSet(
        string profileProcess,
        DisplayMode target,
        DisplayMode original,
        ProfileRetentionPolicy policy,
        string displayError,
        DateTime utcNow)
    {
        AddEvent(utcNow, $"Modusabweichung vor Anwendungsstart: beobachtet {DiagnosticReportFormatter.FormatMode(original)}, Ziel {DiagnosticReportFormatter.FormatMode(target)}, Richtlinie {ProfileRetentionPolicyText.ToDisplayName(policy)}, initiale Aktivierung – Bestätigung fehlgeschlagen: {displayError}");
        var rollbackError = TryRestoreMode(original);
        AddEvent(utcNow, rollbackError is null
            ? "Originalmodus nach fehlgeschlagener Modusbestätigung wiederhergestellt."
            : $"Wiederherstellung nach fehlgeschlagener Modusbestätigung fehlgeschlagen: {rollbackError}");
        var error = rollbackError is null
            ? displayError
            : $"{displayError} Der Originalmodus konnte ebenfalls nicht wiederhergestellt werden: {rollbackError}";
        if (rollbackError is null)
            SetStatus(ProfileMonitorState.Error, error, profileProcess, target, displayError, utcNow, policy: policy);
        else
            QueuePendingRestore(LaunchPlan.Direct(profileProcess), target, original, policy, rollbackError, utcNow);
        return ProfileLaunchResult.Failed(error, displayError: displayError, rollbackError: rollbackError, displayModeChanged: true);
    }

    private void QueuePendingRestore(
        LaunchPlan launchPlan,
        DisplayMode target,
        DisplayMode original,
        ProfileRetentionPolicy policy,
        string rollbackError,
        DateTime utcNow)
    {
        _active = new ActiveProfile
        {
            ProfileProcess = launchPlan.ExpectedExecutablePath,
            LaunchPlan = launchPlan,
            Target = target,
            Original = original,
            Policy = policy,
            ToolChangedMode = true,
            ProcessExited = true
        };
        _retryCount++;
        _nextActionUtc = utcNow + _retryInterval;
        SetStatus(ProfileMonitorState.Restoring,
            $"Originalmodus konnte nicht wiederhergestellt werden: {rollbackError} Neuer Versuch folgt.",
            launchPlan.ExpectedExecutablePath, target, rollbackError, utcNow, launchPlan.Strategy);
    }

    private ProfileLaunchResult FailLaunchState(
        string profileProcess,
        DisplayMode target,
        ProfileRetentionPolicy policy,
        string error,
        string displayError,
        DateTime utcNow)
    {
        SetStatus(ProfileMonitorState.Error, error, profileProcess, target, displayError, utcNow, policy: policy);
        return ProfileLaunchResult.Failed(error, displayError: displayError);
    }

    private string? TryRestoreMode(DisplayMode original)
    {
        try
        {
            var current = _display.GetCurrentDisplayMode();
            if (DisplayModeEquivalence.AreEquivalent(current, original))
                return null;
            var result = _display.SetDisplayMode(original);
            return result.Success ? null : result.Error ?? "Unbekannter Wiederherstellungsfehler.";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private OperationResult RestoreOriginal()
    {
        var active = _active ?? throw new InvalidOperationException("Ein aktives Profil wurde erwartet.");
        var current = _display.GetCurrentDisplayMode();
        return DisplayModeEquivalence.AreEquivalent(current, active.Original)
            ? OperationResult.Ok()
            : _display.SetDisplayMode(active.Original);
    }

    private void SetStopped(DateTime utcNow)
    {
        _stopped = true;
        _active = null;
        SetStatus(ProfileMonitorState.Stopped, "Profilüberwachung wurde gestoppt.", null, null, null, utcNow);
        AddEvent(utcNow, "Profilüberwachung gestoppt.");
    }

    private void SetStatus(ProfileMonitorState state, string message, string? profileProcess, DisplayMode? targetMode, string? result, DateTime utcNow, LaunchStrategy? launchStrategy = null, ProfileRetentionPolicy? policy = null)
    {
        var next = new ProfileMonitorStatus(state, message, profileProcess, targetMode, utcNow, result, _retryCount, launchStrategy,
            policy ?? _active?.Policy, IsPendingLaunch(_active) ? _active!.PendingReapplyCount : _active?.ReapplyCount ?? 0);
        var current = Volatile.Read(ref _status);
        if (current.State == next.State && current.Message == next.Message && current.ProfileProcess == next.ProfileProcess &&
            Equals(current.TargetMode, next.TargetMode) && current.LastResult == next.LastResult && current.RetryCount == next.RetryCount &&
            current.LaunchStrategy == next.LaunchStrategy && current.Policy == next.Policy && current.ReapplyCount == next.ReapplyCount)
            return;
        Volatile.Write(ref _status, next);
    }

    private void AddEvent(DateTime utcNow, string message) => _events.Add(utcNow, message);
}

/// <summary>
/// Target-aware runtime used by the production manager.  It deliberately never
/// reads or writes the implicit primary display: every change goes through a
/// receipt-bearing targeted transaction.
/// </summary>
public sealed class TargetedProfileMonitor
{
    private static readonly TimeSpan StartupWindow = TimeSpan.FromSeconds(30);
    private const int MaximumStartupReapplies = 3;
    private readonly ConcurrentDictionary<string, DisplayProfile> _profiles;
    private readonly ITargetedDisplayService _display;
    private readonly IProcessProvider _processes;
    private readonly IApplicationLauncher _launcher;
    private readonly ILaunchPlanResolver _launchPlans;
    private readonly Func<string, bool> _fileExists;
    private readonly TimeSpan _retryInterval;
    private readonly TimeSpan _verificationInterval;
    private readonly TimeSpan _pendingLaunchTimeout;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly DiagnosticEventBuffer _events = new();
    private Active? _active;
    private DateTime _nextActionUtc;
    private int _retryCount;
    private bool _stopped;
    private bool _stopRequested;
    private ProfileMonitorStatus _status = ProfileMonitorStatus.Idle;

    private sealed class Active
    {
        public required string ProfileProcess { get; init; }
        public required DisplayProfile Profile { get; init; }
        public required LaunchPlan LaunchPlan { get; init; }
        public ProcessIdentity? Process { get; set; }
        public DateTime? LaunchRequestedUtc { get; set; }
        public DateTime? PendingUntilUtc { get; set; }
        public DateTime? ActivatedUtc { get; set; }
        public int ReapplyCount { get; set; }
        public int PendingFailures { get; set; }
        public bool ProcessExited { get; set; }
        public bool RestoreInitialAfterDebts { get; set; }
        public bool InitialApplied { get; set; }
        public IReadOnlyList<TargetedDisplayReceipt> InitialReceipts { get; set; } = [];
        public IReadOnlyList<DisplayRestoreDebt> RestoreDebts { get; set; } = [];
        public IReadOnlyList<TargetedDisplayError> LastApplyErrors { get; set; } = [];
        public bool ExternallyDetected { get; init; }
    }

    public TargetedProfileMonitor(
        ConcurrentDictionary<string, DisplayProfile> profiles,
        ITargetedDisplayService display,
        IProcessProvider processes,
        TimeSpan? retryInterval = null,
        TimeSpan? verificationInterval = null,
        IApplicationLauncher? launcher = null,
        Func<string, bool>? fileExists = null,
        ILaunchPlanResolver? launchPlans = null,
        TimeSpan? pendingLaunchTimeout = null)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _display = display ?? throw new ArgumentNullException(nameof(display));
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
        _launcher = launcher ?? new SystemApplicationLauncher();
        _launchPlans = launchPlans ?? new SteamAwareLaunchPlanResolver();
        _fileExists = fileExists ?? File.Exists;
        _retryInterval = retryInterval ?? TimeSpan.FromSeconds(5);
        _verificationInterval = verificationInterval ?? TimeSpan.FromSeconds(1);
        _pendingLaunchTimeout = pendingLaunchTimeout ?? TimeSpan.FromSeconds(120);
        if (_retryInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retryInterval));
        if (_verificationInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(verificationInterval));
        if (_pendingLaunchTimeout <= TimeSpan.Zero || _pendingLaunchTimeout > TimeSpan.FromSeconds(120))
            throw new ArgumentOutOfRangeException(nameof(pendingLaunchTimeout), "Das Steam-Wartefenster muss größer als null und darf höchstens 120 Sekunden lang sein.");
    }

    public ProfileMonitorStatus Status => Volatile.Read(ref _status);
    public IReadOnlyList<ProfileMonitorDiagnosticEvent> DiagnosticEvents => _events.Snapshot();
    internal void RecordDiagnosticEvent(DateTime timestampUtc, string message) => _events.Add(timestampUtc, message);
    public LaunchPlan ResolveLaunchPlan(string executablePath) => _launchPlans.Resolve(executablePath);

    public void Start(DateTime utcNow)
    {
        _operationGate.Wait();
        try
        {
            // Never discard receipt/debt ownership if Start is called again while
            // an activation or restoration is still managed.
            if (_active is not null) return;
            _stopped = false; _stopRequested = false; _active = null; _retryCount = 0;
            SetStatus(ProfileMonitorState.Idle, "Wartet auf einen Profilprozess.", null, null, null, utcNow);
            AddEvent(utcNow, "Profilüberwachung gestartet (gezielte Monitorziele).");
        }
        finally { _operationGate.Release(); }
    }

    public ProfileLaunchResult LaunchProfileApplication(string profileProcess, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(profileProcess) || !Path.IsPathFullyQualified(profileProcess))
            return ProfileLaunchResult.Failed("Legacy-Profile ohne vollständigen EXE-Pfad können nicht gestartet werden.");
        string path;
        try { path = ProcessMatcher.CanonicalizePath(profileProcess); }
        catch (Exception ex) { return ProfileLaunchResult.Failed($"Der Profilpfad ist ungültig: {ex.Message}"); }

        _operationGate.Wait();
        try
        {
            if (_stopped) return ProfileLaunchResult.Failed("Die Profilüberwachung wurde bereits beendet.");
            if (_retryCount > 0 && utcNow < _nextActionUtc)
                return ProfileLaunchResult.Failed("Der letzte Displayversuch ist fehlgeschlagen; der nächste Versuch ist noch nicht freigegeben.");
            if (!_profiles.TryGetValue(path, out var profile)) return ProfileLaunchResult.Failed("Das ausgewählte Profil ist nicht mehr vorhanden.");
            if (!_fileExists(path)) return ProfileLaunchResult.Failed("Die EXE-Datei des ausgewählten Profils wurde nicht gefunden.");
            if (_active is not null) return ProfileLaunchResult.Failed("Es ist bereits ein Profil aktiv oder wird wiederhergestellt.");
            try
            {
                if (_profiles.Any(item => ProcessMatcher.FindMatches(item.Key, _processes.GetCurrentSessionProcesses()).Count > 0))
                    return ProfileLaunchResult.Failed("Es läuft bereits eine Anwendung mit Profil. Beenden Sie diese zuerst.");
            }
            catch (Exception ex) { return ProfileLaunchResult.Failed($"Laufende Profilprozesse konnten nicht geprüft werden: {ex.Message}"); }

            var plan = SafePlan(path);
            SetStatus(ProfileMonitorState.Launching, $"Profilziele für '{path}' werden vorbereitet.", path, PrimaryMode(profile), null, utcNow, plan.Strategy, profile.Policy);
            var apply = Apply(profile.Targets);
            if (!apply.Success)
            {
                var error = DescribeErrors(apply.Errors);
                if (apply.RestoreDebts.Count > 0)
                    QueueRestoreOnly(path, profile, plan, apply, utcNow);
                else
                {
                    _retryCount++;
                    _nextActionUtc = utcNow + _retryInterval;
                    SetStatus(ProfileMonitorState.Error, $"Profilziele konnten nicht angewendet werden: {error}", path, PrimaryMode(profile), error, utcNow, plan.Strategy, profile.Policy, apply.Errors);
                }
                AddEvent(utcNow, $"Profilstart abgelehnt; Ziel-Batch fehlgeschlagen: {error}");
                return ProfileLaunchResult.Failed($"Profilziele konnten nicht angewendet werden: {error}", displayError: error,
                    rollbackError: apply.RestoreDebts.Count == 0 ? null : DescribeDebts(apply.RestoreDebts), displayModeChanged: apply.Receipts.Any(item => item.ToolChanged));
            }

            var active = NewActive(path, profile, plan, apply, null);
            _retryCount = 0;
            _active = active;
            return plan.Strategy == LaunchStrategy.Steam
                ? StartSteam(active, utcNow)
                : StartDirect(active, utcNow);
        }
        finally { _operationGate.Release(); }
    }

    public void Poll(DateTime utcNow)
    {
        _operationGate.Wait();
        try
        {
            if (_stopped || utcNow < _nextActionUtc) return;
            if (_active is null) { TryActivate(utcNow); return; }
            if (_active.RestoreDebts.Count > 0 || _active.ProcessExited) { TryRestore(utcNow); return; }
            var running = _processes.GetCurrentSessionProcesses();
            if (IsPending(_active)) { PollPending(running, utcNow); return; }
            if (_active.Process is null || !Contains(running, _active.Process))
            {
                _active.ProcessExited = true;
                AddEvent(utcNow, $"Profilprozess beendet: {_active.ProfileProcess}.");
                TryRestore(utcNow); return;
            }
            EnsureTargets(utcNow, pending: false);
        }
        catch (Exception ex)
        {
            _nextActionUtc = utcNow + _retryInterval; _retryCount++;
            SetStatus(ProfileMonitorState.Error, $"Profilüberwachung fehlgeschlagen: {ex.Message}", _active?.ProfileProcess, _active is null ? null : PrimaryMode(_active.Profile), ex.Message, utcNow, _active?.LaunchPlan.Strategy);
        }
        finally { _operationGate.Release(); }
    }

    public OperationResult Stop()
    {
        _operationGate.Wait();
        try
        {
            _stopRequested = true;
            if (_active is null) { SetStopped(DateTime.UtcNow); return OperationResult.Ok(); }
            _active.ProcessExited = true;
            return TryRestore(DateTime.UtcNow);
        }
        finally { _operationGate.Release(); }
    }

    private void TryActivate(DateTime utcNow)
    {
        IReadOnlyList<ProcessIdentity> running;
        try { running = _processes.GetCurrentSessionProcesses(); }
        catch (Exception ex) { _nextActionUtc = utcNow + _retryInterval; SetStatus(ProfileMonitorState.Error, $"Profilprozesse konnten nicht gelesen werden: {ex.Message}", null, null, ex.Message, utcNow); return; }
        var candidates = _profiles.Select(item => (item.Key, item.Value, Matches: ProcessMatcher.FindMatches(item.Key, running)))
            .Where(item => item.Matches.Count > 0).ToArray();
        if (candidates.Length == 0) { SetStatus(ProfileMonitorState.Idle, "Wartet auf einen Profilprozess.", null, null, null, utcNow); return; }
        if (candidates.Length != 1 || candidates[0].Matches.Count != 1)
        { SetStatus(ProfileMonitorState.Ambiguous, "Mehrere Profile oder Prozesse sind gleichzeitig aktiv; es wird nicht geschaltet.", null, null, null, utcNow); return; }
        var item = candidates[0];
        var plan = LaunchPlan.Direct(item.Key);
        var active = NewActive(item.Key, item.Value, plan, null, item.Matches[0]);
        _active = active;
        AddEvent(utcNow, $"Profilprozess erkannt: {item.Key}; Ziel-Batch wird vor dem Aktivieren geprüft.");
        EnsureTargets(utcNow, pending: false);
    }

    private void EnsureTargets(DateTime utcNow, bool pending)
    {
        var active = _active!;
        if (active.RestoreDebts.Count > 0) { TryRestore(utcNow); return; }
        var reapply = active.InitialApplied;
        if (reapply && !MayReapply(active, utcNow, pending, out var reason))
        {
            _nextActionUtc = utcNow + _verificationInterval;
            SetStatus(pending ? ProfileMonitorState.PendingLaunch : ProfileMonitorState.Active, "Profil bleibt aktiv; Ziel-Batch wird gemäß Richtlinie nicht nachgesetzt.", active.ProfileProcess, PrimaryMode(active.Profile), reason, utcNow, active.LaunchPlan.Strategy);
            return;
        }
        // Once resolved, even an originally dynamic PrimaryMonitor selector is
        // pinned to its concrete physical path. A disconnect must fail instead
        // of silently rebinding a later apply to another monitor.
        var result = Apply(reapply ? CreateBoundTargets(active) : active.Profile.Targets);
        active.LastApplyErrors = result.Errors;
        var targetWasChanged = result.Receipts.Any(item => item.ToolChanged);
        // Erfolgreiche Prüf-Batches ohne Displayänderung verbrauchen das
        // begrenzte Startup-Budget nicht. Ein echter oder fehlgeschlagener
        // Korrekturversuch behält dagegen die bisherige Zählsemantik.
        if (reapply && !pending && (!result.Success || targetWasChanged))
            active.ReapplyCount++;
        if (result.Success)
        {
            var wasInitialActivation = !active.InitialApplied;
            if (!active.InitialApplied)
            {
                // Only the first successful batch owns restoration. Reapply receipts
                // intentionally never replace these originals.
                active.InitialReceipts = CloneReceipts(result.Receipts);
                active.InitialApplied = true;
                active.ActivatedUtc ??= utcNow;
            }
            else
            {
                // A later batch may be the first one that actually writes a
                // target. Preserve its initial original mode, but remember that
                // this target now requires restoration.
                MarkInitialReceiptsChanged(active, result.Receipts.Where(item => item.ToolChanged));
            }
            _retryCount = 0; active.PendingFailures = 0; _nextActionUtc = utcNow + _verificationInterval;
            SetStatus(pending ? ProfileMonitorState.PendingLaunch : ProfileMonitorState.Active,
                pending ? "Steam-Start wartet auf den Zielprozess; Ziel-Batch ist aktiv." : $"Profilziele für '{active.ProfileProcess}' sind aktiv.",
                active.ProfileProcess, PrimaryMode(active.Profile), reapply
                    ? targetWasChanged ? "Ziel-Batch nachgesetzt" : "Ziel-Batch geprüft; keine Abweichung"
                    : "Ziel-Batch angewendet", utcNow, active.LaunchPlan.Strategy);
            if (wasInitialActivation)
            {
                foreach (var receipt in result.Receipts.OrderBy(item => item.TargetIndex))
                    AddEvent(utcNow, FormatTargetChange(active, receipt, "Profilziel aktiviert"));
            }
            if (reapply && targetWasChanged)
            {
                AddEvent(utcNow, $"Abweichende Profilziele nachgesetzt ({FormatReapplies(active)}).");
                foreach (var receipt in result.Receipts.Where(item => item.ToolChanged).OrderBy(item => item.TargetIndex))
                    AddEvent(utcNow, FormatTargetChange(active, receipt, "Profilziel nachgesetzt"));
            }
            return;
        }
        var error = DescribeErrors(result.Errors);
        if (result.RestoreDebts.Count > 0)
        {
            // A failed reapply may have written a target whose first successful
            // receipt was initially equivalent. Once its rollback debt clears,
            // that target still belongs to the initial restore set.
            if (active.InitialApplied)
                MarkInitialReceiptsChanged(active, result.RestoreDebts.Select(item => item.Receipt));
            active.RestoreDebts = CloneDebts(result.RestoreDebts);
            active.RestoreInitialAfterDebts = active.InitialApplied;
            active.ProcessExited = true; // debt has priority over every future apply/launch
            AddEvent(utcNow, $"Ziel-Batch fehlgeschlagen; Teilrollback hinterließ Wiederherstellungsschulden: {DescribeDebts(active.RestoreDebts)}.");
            TryRestore(utcNow); return;
        }
        _retryCount++; if (pending) active.PendingFailures++;
        if (pending && active.PendingFailures >= 3) { active.ProcessExited = true; AddEvent(utcNow, "Steam-Wartephase wegen drei Displayfehlern abgebrochen."); TryRestore(utcNow); return; }
        _nextActionUtc = utcNow + _retryInterval;
        SetStatus(pending ? ProfileMonitorState.PendingLaunch : ProfileMonitorState.RetryPending,
            $"Ziel-Batch konnte nicht angewendet werden: {error} Neuer Versuch folgt.", active.ProfileProcess, PrimaryMode(active.Profile), error, utcNow, active.LaunchPlan.Strategy, active.Profile.Policy, result.Errors);
    }

    private void PollPending(IReadOnlyList<ProcessIdentity> running, DateTime utcNow)
    {
        var active = _active!;
        var matches = ProcessMatcher.FindMatches(active.ProfileProcess, running).Where(item => item.Id > 0 && item.StartTimeUtc != default && item.StartTimeUtc >= active.LaunchRequestedUtc!.Value - TimeSpan.FromSeconds(2)).ToArray();
        if (matches.Length == 1)
        { active.Process = matches[0]; active.ActivatedUtc = utcNow; active.ReapplyCount = 0; _nextActionUtc = utcNow + _verificationInterval; SetStatus(ProfileMonitorState.Active, $"Profilziele für '{active.ProfileProcess}' sind aktiv.", active.ProfileProcess, PrimaryMode(active.Profile), $"Steam-Zielprozess übernommen (PID {matches[0].Id})", utcNow, active.LaunchPlan.Strategy); return; }
        if (utcNow >= active.PendingUntilUtc) { active.ProcessExited = true; AddEvent(utcNow, "Steam-Start abgebrochen: Zielprozess wurde nicht rechtzeitig erkannt."); TryRestore(utcNow); return; }
        EnsureTargets(utcNow, pending: true);
    }

    private ProfileLaunchResult StartDirect(Active active, DateTime utcNow)
    {
        ILaunchedApplication? launched = null;
        try
        {
            launched = _launcher.Launch(active.LaunchPlan.ExpectedExecutablePath);
            var identity = launched.Identity;
            if (identity.Id <= 0 || identity.StartTimeUtc == default || !string.Equals(ProcessMatcher.CanonicalizePath(identity.ExecutablePath), active.LaunchPlan.ExpectedExecutablePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Windows hat keine eindeutig überwachbare Prozessinstanz für die ausgewählte EXE geliefert.");
            active.Process = identity; active.ActivatedUtc = utcNow; _nextActionUtc = utcNow + _verificationInterval;
            SetStatus(ProfileMonitorState.Active, $"Profilziele für '{active.ProfileProcess}' sind aktiv.", active.ProfileProcess, PrimaryMode(active.Profile), $"Anwendung gestartet (PID {identity.Id})", utcNow, active.LaunchPlan.Strategy);
            AddEvent(utcNow, $"Anwendung erst nach erfolgreichem Ziel-Batch gestartet: PID {identity.Id}.");
            return ProfileLaunchResult.Started(identity, active.InitialReceipts.Any(item => item.ToolChanged));
        }
        catch (Exception ex) { return FailStart(active, ex, utcNow); }
        finally { try { launched?.Dispose(); } catch { } }
    }

    private ProfileLaunchResult StartSteam(Active active, DateTime utcNow)
    {
        try
        {
            if (!IsValidSteamLaunchPlan(active.LaunchPlan)) throw new InvalidOperationException("Der Steam-Startplan ist unvollständig.");
            _launcher.LaunchSteam(active.LaunchPlan.SteamUri!);
            active.LaunchRequestedUtc = utcNow; active.PendingUntilUtc = utcNow + _pendingLaunchTimeout; _nextActionUtc = utcNow + _verificationInterval;
            SetStatus(ProfileMonitorState.PendingLaunch, $"Steam-Start ausgelöst; wartet auf '{active.ProfileProcess}'.", active.ProfileProcess, PrimaryMode(active.Profile), "Wartet auf Zielprozess", utcNow, active.LaunchPlan.Strategy);
            AddEvent(utcNow, "Steam-URI erst nach erfolgreichem Ziel-Batch ausgelöst.");
            return new ProfileLaunchResult(true, DisplayModeChanged: active.InitialReceipts.Any(item => item.ToolChanged));
        }
        catch (Exception ex) { return FailStart(active, ex, utcNow); }
    }

    private ProfileLaunchResult FailStart(Active active, Exception exception, DateTime utcNow)
    {
        active.ProcessExited = true; AddEvent(utcNow, $"Anwendungsstart fehlgeschlagen: {exception.Message}");
        var restore = TryRestore(utcNow);
        return ProfileLaunchResult.Failed($"Die Anwendung konnte nicht gestartet werden: {exception.Message}", startError: exception.Message,
            rollbackError: restore.Success ? null : restore.Error, displayModeChanged: active.InitialReceipts.Any(item => item.ToolChanged));
    }

    private OperationResult TryRestore(DateTime utcNow)
    {
        var active = _active!;
        var receipts = (active.RestoreDebts.Count > 0 ? active.RestoreDebts.Select(item => item.Receipt) : active.InitialReceipts)
            .Where(item => item.ToolChanged).ToArray();
        if (receipts.Length == 0) { FinishRestore(utcNow); return OperationResult.Ok(); }
        TargetedDisplayRestoreResult result;
        try { result = _display.Restore(CloneReceipts(receipts)); }
        catch (Exception ex)
        {
            _retryCount++; _nextActionUtc = utcNow + _retryInterval;
            SetStatus(ProfileMonitorState.Restoring, $"Originalziele konnten nicht wiederhergestellt werden: {ex.Message} Neuer Versuch folgt.", active.ProfileProcess, PrimaryMode(active.Profile), ex.Message, utcNow, active.LaunchPlan.Strategy);
            return OperationResult.Fail(ex.Message);
        }
        if (result.Success && active.RestoreDebts.Count > 0 && active.RestoreInitialAfterDebts)
        {
            // A failed rollback is restored to its safe intermediate state first;
            // the immutable initial receipts still have to be restored afterwards.
            active.RestoreDebts = []; active.RestoreInitialAfterDebts = false;
            return TryRestore(utcNow);
        }
        if (result.Success) { active.RestoreDebts = []; FinishRestore(utcNow); return OperationResult.Ok(); }
        active.RestoreDebts = CloneDebts(result.RemainingDebts); _retryCount++; _nextActionUtc = utcNow + _retryInterval;
        var error = DescribeDebts(active.RestoreDebts);
        SetStatus(ProfileMonitorState.Restoring, $"Originalziele konnten nicht vollständig wiederhergestellt werden: {error} Neuer Versuch folgt.", active.ProfileProcess, PrimaryMode(active.Profile), error, utcNow, active.LaunchPlan.Strategy, active.Profile.Policy, result.Errors);
        AddEvent(utcNow, $"Wiederherstellungsschulden verbleiben: {error}.");
        return OperationResult.Fail(error);
    }

    private void FinishRestore(DateTime utcNow)
    {
        var name = _active?.ProfileProcess;
        _active = null; _retryCount = 0;
        if (_stopRequested) SetStopped(utcNow);
        else { SetStatus(ProfileMonitorState.Idle, "Wartet auf einen Profilprozess.", null, null, "Originalziele wiederhergestellt", utcNow); AddEvent(utcNow, $"Originalziele wiederhergestellt: {name}."); }
    }

    private void QueueRestoreOnly(string path, DisplayProfile profile, LaunchPlan plan, TargetedDisplayApplyResult apply, DateTime utcNow)
    {
        _active = NewActive(path, profile, plan, apply, null);
        _active.ProcessExited = true; _active.RestoreDebts = CloneDebts(apply.RestoreDebts); _nextActionUtc = utcNow + _retryInterval;
        var error = DescribeDebts(_active.RestoreDebts);
        SetStatus(ProfileMonitorState.Restoring, $"Teilrollback muss zuerst wiederhergestellt werden: {error}", path, PrimaryMode(profile), error, utcNow, plan.Strategy, profile.Policy, apply.Errors);
    }

    private Active NewActive(string process, DisplayProfile profile, LaunchPlan plan, TargetedDisplayApplyResult? apply, ProcessIdentity? identity) => new()
    {
        ProfileProcess = process, Profile = profile.DeepCopy(), LaunchPlan = plan, Process = identity,
        ExternallyDetected = identity is not null,
        InitialApplied = apply?.Success == true, InitialReceipts = apply is null ? [] : CloneReceipts(apply.Receipts),
        RestoreDebts = apply is null ? [] : CloneDebts(apply.RestoreDebts), LastApplyErrors = apply is null ? [] : apply.Errors.ToArray()
    };

    private TargetedDisplayApplyResult Apply(IReadOnlyList<DisplayProfileTarget> targets)
    {
        try { return _display.Apply(targets); }
        catch (Exception ex) { return new TargetedDisplayApplyResult(false, [], [new TargetedDisplayError(null, null, TargetedDisplayStage.Apply, TargetedDisplayErrorCode.NativeChangeRejected, ex.Message)], []); }
    }

    private static IReadOnlyList<DisplayProfileTarget> CreateBoundTargets(Active active)
    {
        var receipts = active.InitialReceipts.ToDictionary(item => item.TargetIndex);
        if (receipts.Count != active.Profile.Targets.Count)
            throw new InvalidOperationException("Die initialen Zielbelege sind unvollständig; es wird kein weiterer Display-Batch ausgeführt.");

        return active.Profile.Targets.Select((target, index) =>
        {
            if (!receipts.TryGetValue(index, out var receipt) || string.IsNullOrWhiteSpace(receipt.MonitorDevicePath))
                throw new InvalidOperationException("Ein initialer Zielbeleg besitzt keinen konkreten Monitor-Gerätepfad.");
            var friendlyName = target.MonitorSelector is SpecificMonitor specific && !string.IsNullOrWhiteSpace(specific.FriendlyNameSnapshot)
                ? specific.FriendlyNameSnapshot
                : receipt.MonitorDevicePath;
            return new DisplayProfileTarget(
                new SpecificMonitor(receipt.MonitorDevicePath, friendlyName),
                target.Mode);
        }).ToArray();
    }

    private static void MarkInitialReceiptsChanged(Active active, IEnumerable<TargetedDisplayReceipt> changedReceipts)
    {
        var changed = changedReceipts.Where(item => item.ToolChanged).ToDictionary(item => item.TargetIndex);
        active.InitialReceipts = active.InitialReceipts.Select(initial =>
            changed.TryGetValue(initial.TargetIndex, out var current) && string.Equals(
                initial.MonitorDevicePath, current.MonitorDevicePath, StringComparison.OrdinalIgnoreCase)
                ? initial with { ToolChanged = true }
                : initial).ToArray();
    }

    private LaunchPlan SafePlan(string path)
    {
        try
        {
            var plan = _launchPlans.Resolve(path);
            return string.Equals(plan.ExpectedExecutablePath, path, StringComparison.OrdinalIgnoreCase) && plan.Strategy is LaunchStrategy.Direct or LaunchStrategy.Steam && (plan.Strategy != LaunchStrategy.Steam || IsValidSteamLaunchPlan(plan)) ? plan : LaunchPlan.Direct(path);
        }
        catch { return LaunchPlan.Direct(path); }
    }

    private static bool IsPending(Active active) => active.Process is null && !active.ProcessExited && active.LaunchRequestedUtc is not null && active.PendingUntilUtc is not null;
    private static bool Contains(IReadOnlyList<ProcessIdentity> processes, ProcessIdentity process) => processes.Any(item => item.Id == process.Id && item.StartTimeUtc == process.StartTimeUtc && string.Equals(item.ExecutablePath, process.ExecutablePath, StringComparison.OrdinalIgnoreCase));
    private static bool IsValidSteamLaunchPlan(LaunchPlan plan) => !string.IsNullOrEmpty(plan.SteamAppId) && plan.SteamAppId.All(char.IsAsciiDigit) && string.Equals(plan.SteamUri, SteamLaunchInfo.CreateUri(plan.SteamAppId), StringComparison.Ordinal);
    private static bool MayReapply(Active active, DateTime now, bool pending, out string reason)
    {
        if (pending) { reason = string.Empty; return true; }
        if (active.Profile.Policy == ProfileRetentionPolicy.Once) { reason = "Einmalige Aktivierung abgeschlossen."; return false; }
        if (active.Profile.Policy == ProfileRetentionPolicy.Startup && (now >= active.ActivatedUtc!.Value + StartupWindow || active.ReapplyCount >= MaximumStartupReapplies)) { reason = "Die Startphasen-Richtlinie erlaubt keine weitere Nachsetzung."; return false; }
        reason = string.Empty; return true;
    }
    private static DisplayMode? PrimaryMode(DisplayProfile profile) => profile.Targets.FirstOrDefault()?.Mode;
    private static string FormatReapplies(Active active) => active.Profile.Policy == ProfileRetentionPolicy.Startup ? $"{active.ReapplyCount}/{MaximumStartupReapplies}" : active.Profile.Policy == ProfileRetentionPolicy.Continuous ? "fortlaufend" : "0";
    private static string FormatTargetChange(Active active, TargetedDisplayReceipt receipt, string action)
    {
        var selector = receipt.TargetIndex >= 0 && receipt.TargetIndex < active.Profile.Targets.Count
            ? DisplayProfilePresentation.DescribeTargets(new DisplayProfile(ProfileRetentionPolicy.Once, [active.Profile.Targets[receipt.TargetIndex]]))
            : "Unbekanntes Monitorziel";
        var result = receipt.ToolChanged ? "angewendet" : "bereits aktiv";
        return $"{action}: {selector}; Pfad {DiagnosticReportFormatter.FormatDevicePathShort(receipt.MonitorDevicePath)}; Ausgang {DiagnosticReportFormatter.FormatMode(ToDisplayMode(receipt.OriginalMode))}; Ziel {DiagnosticReportFormatter.FormatMode(ToDisplayMode(receipt.TargetMode))}; Ergebnis {result}.";
    }
    private static string DescribeErrors(IReadOnlyList<TargetedDisplayError> errors) => errors.Count == 0 ? "Unbekannter Displayfehler." : string.Join(" | ", errors.Select(item => item.Message).Distinct());
    private static string DescribeDebts(IReadOnlyList<DisplayRestoreDebt> debts) => debts.Count == 0 ? "–" : string.Join(" | ", debts.Select(item => $"{item.Receipt.MonitorDevicePath}: {item.Error.Message}").Distinct());
    private static IReadOnlyList<TargetedDisplayReceipt> CloneReceipts(IEnumerable<TargetedDisplayReceipt> receipts) => receipts.Select(item => item with { OriginalMode = item.OriginalMode with { }, TargetMode = item.TargetMode with { } }).ToArray();
    private static IReadOnlyList<DisplayRestoreDebt> CloneDebts(IEnumerable<DisplayRestoreDebt> debts) => debts.Select(item => new DisplayRestoreDebt(CloneReceipts([item.Receipt])[0], item.Error with { })).ToArray();
    private void SetStopped(DateTime now) { _stopped = true; _active = null; SetStatus(ProfileMonitorState.Stopped, "Profilüberwachung wurde gestoppt.", null, null, null, now); AddEvent(now, "Profilüberwachung gestoppt."); }
    private void SetStatus(ProfileMonitorState state, string message, string? process, DisplayMode? target, string? result, DateTime now, LaunchStrategy? strategy = null, ProfileRetentionPolicy? policy = null, IReadOnlyList<TargetedDisplayError>? errors = null)
    {
        var active = _active;
        var receiptByTarget = active?.InitialReceipts.ToDictionary(item => item.TargetIndex) ?? new Dictionary<int, TargetedDisplayReceipt>();
        var debtByPath = active?.RestoreDebts.GroupBy(item => item.Receipt.MonitorDevicePath, StringComparer.OrdinalIgnoreCase).ToDictionary(item => item.Key, item => item.Last().Error.Message, StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var targetStatuses = active?.Profile.Targets.Select((item, index) =>
        {
            receiptByTarget.TryGetValue(index, out var receipt);
            var currentError = errors?.FirstOrDefault(error => error.TargetIndex == index);
            var apply = currentError is not null && currentError.Stage != TargetedDisplayStage.Restore
                ? currentError.Message
                : active.LastApplyErrors.FirstOrDefault(error => error.TargetIndex == index)?.Message;
            var restore = receipt is not null && debtByPath.TryGetValue(receipt.MonitorDevicePath, out var debt) ? debt : null;
            return new ProfileMonitorTargetStatus(receipt?.MonitorDevicePath ?? currentError?.MonitorDevicePath, DisplayProfilePresentation.DescribeTargets(new DisplayProfile(ProfileRetentionPolicy.Once, [item])), item.Mode, receipt is null ? null : ToDisplayMode(receipt.OriginalMode), receipt?.ToolChanged == true, apply, restore);
        }).ToArray();
        Volatile.Write(ref _status, new ProfileMonitorStatus(state, message, process, target, now, result, _retryCount, strategy, policy ?? active?.Profile.Policy, active?.ReapplyCount ?? 0, targetStatuses, active?.ExternallyDetected == true));
    }
    private static DisplayMode ToDisplayMode(EndpointDisplayMode mode) => new() { Width = mode.Width, Height = mode.Height, Frequency = mode.Frequency, Label = mode.Label };
    private void AddEvent(DateTime now, string message) => _events.Add(now, message);
}

public sealed class ProfileManager : IDisposable
{
    private readonly TargetedProfileMonitor _monitor;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Thread _thread;
    private readonly object _lifecycleSync = new();
    private bool _started;
    private bool _disposed;

    public ProfileManager(
        ConcurrentDictionary<string, DisplayProfile> profiles,
        ITargetedDisplayService targetedDisplay,
        IProcessProvider? processes = null,
        IApplicationLauncher? launcher = null,
        Func<string, bool>? fileExists = null,
        ILaunchPlanResolver? launchPlans = null)
    {
        _monitor = new TargetedProfileMonitor(profiles, targetedDisplay, processes ?? new SystemProcessProvider(), launcher: launcher, fileExists: fileExists, launchPlans: launchPlans);
        _thread = new Thread(Monitor) { IsBackground = true, Name = "DisplayModeSwitcher.ProfileMonitor" };
    }

    public ProfileMonitorStatus Status => _monitor.Status;
    public IReadOnlyList<ProfileMonitorDiagnosticEvent> DiagnosticEvents => _monitor.DiagnosticEvents;
    internal void RecordDiagnosticEvent(DateTime timestampUtc, string message) =>
        _monitor.RecordDiagnosticEvent(timestampUtc, message);

    public Task<LaunchPlan> ResolveLaunchPlanAsync(string executablePath)
    {
        lock (_lifecycleSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        return Task.Run(() => _monitor.ResolveLaunchPlan(executablePath));
    }

    public Task<ProfileLaunchResult> LaunchProfileApplicationAsync(string profileProcess)
    {
        lock (_lifecycleSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        return Task.Run(() => _monitor.LaunchProfileApplication(profileProcess, DateTime.UtcNow));
    }

    public void Start()
    {
        lock (_lifecycleSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) return;
            _started = true;
            _monitor.Start(DateTime.UtcNow);
            _thread.Start();
        }
    }

    private void Monitor()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            _monitor.Poll(DateTime.UtcNow);
            if (_cancellation.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(1)))
                break;
        }
    }

    public void Dispose()
    {
        bool joinThread;
        lock (_lifecycleSync)
        {
            if (_disposed) return;
            _disposed = true;
            _cancellation.Cancel();
            joinThread = _started && Thread.CurrentThread != _thread;
        }

        if (joinThread)
            _thread.Join();
        _monitor.Stop();
        _cancellation.Dispose();
    }
}
