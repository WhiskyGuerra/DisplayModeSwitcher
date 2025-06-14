using Microsoft.Win32;
using System.Windows.Forms;

namespace DisplayModeSwitcher
{
    public static class AutostartManager
    {
        private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "DisplayModeSwitcher";

        public static bool IsEnabled()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunPath, writable: false);
            return key?.GetValue(ValueName) != null;
        }

        public static void Enable()
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunPath);
            if (key == null)
                return;
            key.SetValue(ValueName, Application.ExecutablePath);
        }

        public static void Disable()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunPath, writable: true);
            key?.DeleteValue(ValueName, false);
        }
    }
}
