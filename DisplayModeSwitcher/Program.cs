using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace DisplayModeSwitcher
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            var profiles = new Dictionary<string, DisplayMode>
            {
                { "notepad.exe", new DisplayMode { Width = 3840, Height = 1080, Frequency = 100 } }
            };

            using var manager = new ProfileManager(profiles);
            manager.Start();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayForm());
        }
    }
}
