using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace DisplayModeSwitcher
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles.json");
            var store = new ProfileStore(path);

            using var manager = new ProfileManager(store.Profiles);
            manager.Start();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayForm(store));
        }
    }
}
