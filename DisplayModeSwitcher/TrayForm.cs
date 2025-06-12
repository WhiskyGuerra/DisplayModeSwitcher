using System;
using System.Drawing;
using System.Windows.Forms;

namespace DisplayModeSwitcher
{
    public class TrayForm : Form
    {
        private NotifyIcon trayIcon;
        private ContextMenuStrip contextMenu;

        public TrayForm()
        {
            contextMenu = new ContextMenuStrip();

            // Füge deine Auflösungsoptionen hier ein – kann auch später aus JSON geladen werden
            AddResolutionOption("3840x1080 @ 100Hz", 3840, 1080, 100);
            AddResolutionOption("1920x1080 @ 60Hz", 1920, 1080, 60);

            contextMenu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("Beenden");
            exitItem.Click += (s, e) => Application.Exit();
            contextMenu.Items.Add(exitItem);

            trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                ContextMenuStrip = contextMenu,
                Text = "Display Mode Switcher",
                Visible = true
            };

            // Verstecke die Form – das ist kein normales UI-Fenster
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            Opacity = 0;
        }

        private void AddResolutionOption(string label, uint width, uint height, uint hz)
        {
            var item = new ToolStripMenuItem(label);
            item.Click += (s, e) =>
            {
                bool success = DisplayManager.SetDisplayMode(width, height, hz);
                if (!success)
                    MessageBox.Show($"Fehler beim Umschalten auf {label}", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            contextMenu.Items.Add(item);
        }


        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            trayIcon.Visible = false;
            base.OnFormClosing(e);
        }
    }
}
