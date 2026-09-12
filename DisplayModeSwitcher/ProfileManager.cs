using System.Collections.Concurrent;
using System.Diagnostics;

namespace DisplayModeSwitcher;

public sealed record ProcessIdentity(int Id, DateTime StartTimeUtc, string ExecutablePath);

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

public enum ProfileMonitorState { Idle, Active, RetryPending, Restoring, Ambiguous, Error }

public sealed record ProfileMonitorStatus(ProfileMonitorState State, string Message, string? ProfileProcess = null)
{
    public static ProfileMonitorStatus Idle { get; } = new(ProfileMonitorState.Idle, "Kein Profilprozess aktiv.");
}

public sealed class ProfileMonitor
{
    private readonly ConcurrentDictionary<string, DisplayMode> _profiles;
    private readonly IDisplayService _display;
    private readonly IProcessProvider _processes;
    private readonly TimeSpan _retryInterval;
    private readonly TimeSpan _verificationInterval;
    private readonly object _sync = new();
    private ActiveProfile? _active;
    private DateTime _nextActionUtc;
    private ProfileMonitorStatus _status = ProfileMonitorStatus.Idle;

    private sealed class ActiveProfile
    {
        public required string ProfileProcess { get; init; }
        public required ProcessIdentity Process { get; init; }
        public required DisplayMode Target { get; init; }
        public required DisplayMode Original { get; init; }
        public bool ToolChangedMode { get; set; }
        public bool ProcessExited { get; set; }
    }

    public ProfileMonitor(
        ConcurrentDictionary<string, DisplayMode> profiles,
        IDisplayService display,
        IProcessProvider processes,
        TimeSpan? retryInterval = null,
        TimeSpan? verificationInterval = null)
    {
        _profiles = profiles;
        _display = display;
        _processes = processes;
        _retryInterval = retryInterval ?? TimeSpan.FromSeconds(5);
        _verificationInterval = verificationInterval ?? TimeSpan.FromSeconds(5);
    }

    public ProfileMonitorStatus Status
    {
        get { return Volatile.Read(ref _status); }
    }

    public void Poll(DateTime utcNow)
    {
        lock (_sync)
        {
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

                if (_active.ProcessExited || !running.Any(process => process.Id == _active.Process.Id &&
                        process.StartTimeUtc == _active.Process.StartTimeUtc &&
                        string.Equals(process.ExecutablePath, _active.Process.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
                {
                    _active.ProcessExited = true;
                    TryRestore(utcNow);
                    return;
                }

                EnsureTargetMode(utcNow);
            }
            catch (Exception ex)
            {
                _nextActionUtc = utcNow + _retryInterval;
                _status = new(ProfileMonitorState.Error, $"Profilüberwachung fehlgeschlagen: {ex.Message}", _active?.ProfileProcess);
            }
        }
    }

    public OperationResult Stop()
    {
        lock (_sync)
        {
            if (_active is null || !_active.ToolChangedMode)
            {
                _active = null;
                _status = ProfileMonitorStatus.Idle;
                return OperationResult.Ok();
            }

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
                _status = ProfileMonitorStatus.Idle;
            }
            else
            {
                _status = new(ProfileMonitorState.Error, $"Originalmodus konnte beim Beenden nicht wiederhergestellt werden: {result.Error}", _active.ProfileProcess);
            }

            return result;
        }
    }

    private void TryActivate(IReadOnlyList<ProcessIdentity> running, DateTime utcNow)
    {
        var candidates = new List<(string Profile, DisplayMode Mode, ProcessIdentity Process)>();
        foreach (var profile in _profiles.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            var matches = ProcessMatcher.FindMatches(profile.Key, running);
            if (matches.Count > 1)
            {
                _status = new(ProfileMonitorState.Ambiguous,
                    $"Profil '{profile.Key}' passt zu mehreren laufenden Prozessen; es wird nicht automatisch geschaltet.", profile.Key);
                return;
            }
            if (matches.Count == 1)
                candidates.Add((profile.Key, profile.Value, matches[0]));
        }

        if (candidates.Count == 0)
        {
            _status = ProfileMonitorStatus.Idle;
            return;
        }

        if (candidates.Count > 1)
        {
            _status = new(ProfileMonitorState.Ambiguous, "Mehrere Profile sind gleichzeitig aktiv; es wird nicht automatisch geschaltet.");
            return;
        }

        var candidate = candidates[0];
        var original = _display.GetCurrentDisplayMode();
        if (original is null)
        {
            _nextActionUtc = utcNow + _retryInterval;
            _status = new(ProfileMonitorState.Error, "Der aktuelle Anzeigemodus konnte nicht gelesen werden.", candidate.Profile);
            return;
        }

        _active = new ActiveProfile
        {
            ProfileProcess = candidate.Profile,
            Process = candidate.Process,
            Target = candidate.Mode,
            Original = original
        };
        EnsureTargetMode(utcNow);
    }

    private void EnsureTargetMode(DateTime utcNow)
    {
        var active = _active ?? throw new InvalidOperationException("Ein aktives Profil wurde erwartet.");
        var current = _display.GetCurrentDisplayMode();
        if (current is not null && current.Equals(active.Target))
        {
            _nextActionUtc = utcNow + _verificationInterval;
            _status = new(ProfileMonitorState.Active, $"Profilmodus für '{active.ProfileProcess}' ist aktiv.", active.ProfileProcess);
            return;
        }

        var result = _display.SetDisplayMode(active.Target);
        if (result.Success)
        {
            active.ToolChangedMode = true;
            _nextActionUtc = utcNow + _verificationInterval;
            _status = new(ProfileMonitorState.Active, $"Profilmodus für '{active.ProfileProcess}' wurde angewendet.", active.ProfileProcess);
        }
        else
        {
            _nextActionUtc = utcNow + _retryInterval;
            _status = new(ProfileMonitorState.RetryPending,
                $"Profilmodus für '{active.ProfileProcess}' konnte nicht angewendet werden: {result.Error} Neuer Versuch folgt.", active.ProfileProcess);
        }
    }

    private void TryRestore(DateTime utcNow)
    {
        var active = _active ?? throw new InvalidOperationException("Ein aktives Profil wurde erwartet.");
        if (!active.ToolChangedMode)
        {
            _active = null;
            _status = ProfileMonitorStatus.Idle;
            return;
        }

        var result = RestoreOriginal();
        if (result.Success)
        {
            _active = null;
            _status = ProfileMonitorStatus.Idle;
        }
        else
        {
            _nextActionUtc = utcNow + _retryInterval;
            _status = new(ProfileMonitorState.Restoring, $"Originalmodus konnte nicht wiederhergestellt werden: {result.Error} Neuer Versuch folgt.", active.ProfileProcess);
        }
    }

    private OperationResult RestoreOriginal()
    {
        var active = _active ?? throw new InvalidOperationException("Ein aktives Profil wurde erwartet.");
        var current = _display.GetCurrentDisplayMode();
        return current is not null && current.Equals(active.Original)
            ? OperationResult.Ok()
            : _display.SetDisplayMode(active.Original);
    }
}

public sealed class ProfileManager : IDisposable
{
    private readonly ProfileMonitor _monitor;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Thread _thread;
    private readonly object _lifecycleSync = new();
    private bool _started;
    private bool _disposed;

    public ProfileManager(
        ConcurrentDictionary<string, DisplayMode> profiles,
        IDisplayService? display = null,
        IProcessProvider? processes = null)
    {
        _monitor = new ProfileMonitor(profiles, display ?? new WindowsDisplayService(), processes ?? new SystemProcessProvider());
        _thread = new Thread(Monitor) { IsBackground = true, Name = "DisplayModeSwitcher.ProfileMonitor" };
    }

    public ProfileMonitorStatus Status => _monitor.Status;

    public void Start()
    {
        lock (_lifecycleSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) return;
            _started = true;
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
