using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Management;

namespace DisplayModeSwitcher
{
    public class ProfileManager : IDisposable
    {
        private readonly ConcurrentDictionary<string, DisplayMode> _profiles;
        private readonly ConcurrentDictionary<int, DisplayMode> _active = new();
        private readonly ManagementEventWatcher _startWatcher;
        private readonly ManagementEventWatcher _stopWatcher;

        public ProfileManager(ConcurrentDictionary<string, DisplayMode> profiles)
        {
            _profiles = profiles;

            _startWatcher = new ManagementEventWatcher(
                new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
            _startWatcher.EventArrived += OnProcessStarted;

            _stopWatcher = new ManagementEventWatcher(
                new WqlEventQuery("SELECT * FROM Win32_ProcessStopTrace"));
            _stopWatcher.EventArrived += OnProcessStopped;
        }

        public void Start()
        {
            _startWatcher.Start();
            _stopWatcher.Start();
        }

        private void OnProcessStarted(object sender, EventArrivedEventArgs e)
        {
            string processName = (string)e.NewEvent.Properties["ProcessName"].Value;
            int pid = Convert.ToInt32(e.NewEvent.Properties["ProcessID"].Value);

            if (_profiles.TryGetValue(processName, out var mode))
            {
                // Explicitly specify the method to resolve ambiguity
                DisplayMode? current = DisplayManager.GetCurrentDisplayMode();
                if (current != null)
                {
                    _active[pid] = current;
                }
                DisplayManager.SetDisplayMode(mode.Width, mode.Height, mode.Frequency);
            }
        }

        private void OnProcessStopped(object sender, EventArrivedEventArgs e)
        {
            int pid = Convert.ToInt32(e.NewEvent.Properties["ProcessID"].Value);
            if (_active.TryGetValue(pid, out var mode))
            {
                DisplayManager.SetDisplayMode(mode.Width, mode.Height, mode.Frequency);
                _active.TryRemove(pid, out _);
            }
        }

        public void Dispose()
        {
            _startWatcher.Stop();
            _stopWatcher.Stop();
            _startWatcher.Dispose();
            _stopWatcher.Dispose();
        }
    }
}
