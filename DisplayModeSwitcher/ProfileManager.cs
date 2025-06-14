using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace DisplayModeSwitcher
{
    public class ProfileManager : IDisposable
    {
        private readonly ConcurrentDictionary<string, DisplayMode> _profiles;
        private string? _currentProcess;
        private DisplayMode? _originalMode;
        private readonly Thread _thread;
        private bool _running;

        public ProfileManager(ConcurrentDictionary<string, DisplayMode> profiles)
        {
            _profiles = profiles;
            _thread = new Thread(Monitor) { IsBackground = true };
        }

        public void Start()
        {
            _running = true;
            _thread.Start();
        }

        private void Monitor()
        {
            while (_running)
            {
                try
                {
                    if (_currentProcess == null)
                    {
                        foreach (var kvp in _profiles)
                        {
                            string name = Path.GetFileNameWithoutExtension(kvp.Key);
                            if (Process.GetProcessesByName(name).Length > 0)
                            {
                                var current = DisplayManager.GetCurrentDisplayMode();
                                if (current != null)
                                {
                                    _originalMode = current;
                                    _currentProcess = kvp.Key;
                                    DisplayManager.SetDisplayMode(kvp.Value.Width, kvp.Value.Height, kvp.Value.Frequency);
                                }
                                break;
                            }
                        }
                    }
                    else
                    {
                        string name = Path.GetFileNameWithoutExtension(_currentProcess);
                        if (Process.GetProcessesByName(name).Length == 0)
                        {
                            if (_originalMode != null)
                            {
                                DisplayManager.SetDisplayMode(_originalMode.Width, _originalMode.Height, _originalMode.Frequency);
                            }
                            _currentProcess = null;
                            _originalMode = null;
                        }
                    }
                }
                catch
                {
                    // ignore any polling errors
                }

                Thread.Sleep(1000);
            }
        }

        public void Dispose()
        {
            _running = false;
            _thread.Join();
        }
    }
}
