using System;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace DisplayModeSwitcher
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            using var windowsIdentity = WindowsIdentity.GetCurrent();
            var userIdentity = windowsIdentity.User?.Value ?? Environment.UserName;
            using var instanceMutex = new Mutex(true, $@"Local\DisplayModeSwitcher-{userIdentity}", out var isFirstInstance);
            if (!isFirstInstance)
                return;

            var profilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DisplayModeSwitcher",
                "profiles.json");
            var legacyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles.json");
            var store = new ProfileStore(profilePath, legacyPath);
            var autostart = new AutostartService(Application.ExecutablePath, new CurrentUserAutostartRegistry());

            using var manager = new ProfileManager(store.Profiles);
            manager.Start();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayForm(store, autostart, manager));
        }
    }
}
