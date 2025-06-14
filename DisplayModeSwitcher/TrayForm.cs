using System;
using System.Drawing;
using System.Windows.Forms;

namespace DisplayModeSwitcher
{
    public class TrayForm : Form
    {
        private readonly ProfileStore _store;
        private NotifyIcon trayIcon;
        private ContextMenuStrip contextMenu;
        public TrayForm(ProfileStore store)
        {
            _store = store;
            contextMenu = new ContextMenuStrip();

            AddGroupedResolutions();

            var manageItem = new ToolStripMenuItem("Profile verwalten...");
            manageItem.Click += (s, e) =>
            {
                using var form = new ProfileManagerForm(_store);
                form.ShowDialog();
            };
            contextMenu.Items.Add(manageItem);

            contextMenu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("Beenden");
            exitItem.Click += (s, e) => Application.Exit();
            contextMenu.Items.Add(exitItem);

            trayIcon = new NotifyIcon
            {
                Icon = new Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Icon.ico")),
                ContextMenuStrip = contextMenu,
                Text = "Display Mode Switcher",
                Visible = true
            };

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

        private void AddGroupedResolutions()
        {
            var modes = DisplayManager.GetAvailableDisplayModes();

            var grouped = modes
                .GroupBy(m => new { m.Width, m.Height })
                .OrderByDescending(g => g.Key.Width)
                .ThenByDescending(g => g.Key.Height);

            foreach (var group in grouped)
            {
                var submenu = new ToolStripMenuItem($"📐 {group.Key.Width}x{group.Key.Height}");

                foreach (var mode in group.OrderByDescending(m => m.Frequency))
                {
                    var label = $"@ {mode.Frequency}Hz";
                    var item = new ToolStripMenuItem(label);
                    uint width = mode.Width, height = mode.Height, freq = mode.Frequency;

                    item.Click += (s, e) =>
                    {
                        bool success = DisplayManager.SetDisplayMode(width, height, freq);
                        if (!success)
                            MessageBox.Show($"Fehler beim Umschalten auf {width}x{height} @ {freq}Hz", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    };

                    submenu.DropDownItems.Add(item);
                }

                contextMenu.Items.Add(submenu);
            }
        }

    }
}
