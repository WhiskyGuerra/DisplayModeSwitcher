using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DisplayModeSwitcher
{
    public class TrayForm : Form
    {
        private readonly ProfileStore _store;
        private readonly AutostartService _autostart;
        private readonly ProfileManager _profileManager;
        private readonly IDisplayTopologyService _topology;
        private readonly NotifyIcon trayIcon;
        private readonly ContextMenuStrip contextMenu;
        private readonly ToolStripMenuItem _statusItem;
        private readonly System.Windows.Forms.Timer _statusTimer;
        private bool _clipboardErrorShown;
        public TrayForm(ProfileStore store, AutostartService autostart, ProfileManager profileManager, IDisplayTopologyService topology)
        {
            _store = store;
            _autostart = autostart;
            _profileManager = profileManager;
            _topology = topology;
            contextMenu = new ContextMenuStrip();

            AddGroupedResolutions();

            var autostartItem = new ToolStripMenuItem("Autostart mit Windows");
            RefreshAutostartItem(autostartItem);
            autostartItem.Click += (s, e) =>
            {
                var current = _autostart.GetStatus();
                var result = current.IsEnabled ? _autostart.Disable() : _autostart.Enable();
                RefreshAutostartItem(autostartItem);
                if (!result.Success)
                    MessageBox.Show(result.Error ?? "Der Autostart-Status konnte nicht geändert werden.", "Autostart", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            contextMenu.Items.Add(autostartItem);

            _statusItem = new ToolStripMenuItem { Enabled = false };
            contextMenu.Items.Add(_statusItem);
            contextMenu.Opening += (s, e) =>
            {
                RefreshAutostartItem(autostartItem);
                RefreshStatus();
            };

            var diagnosticsItem = new ToolStripMenuItem("Diagnose kopieren");
            diagnosticsItem.Click += (s, e) => CopyDiagnostics();
            contextMenu.Items.Add(diagnosticsItem);

            var manageItem = new ToolStripMenuItem("Profile verwalten...");
            manageItem.Click += (s, e) =>
            {
                using var form = new ProfileManagerForm(_store, _profileManager, _topology);
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

            _statusTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _statusTimer.Tick += (s, e) => RefreshStatus();
            _statusTimer.Start();
            RefreshStatus();

            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            Opacity = 0;

            Shown += (s, e) =>
            {
                Hide();
                if (_store.LastError is not null)
                    MessageBox.Show(_store.LastError, "Profile", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
        }

        private void RefreshStatus()
        {
            var status = _profileManager.Status;
            var state = DiagnosticReportFormatter.DisplayState(status.State);
            _statusItem.Text = $"Status: {state}";
            _statusItem.ToolTipText = status.Message;
            var tooltip = $"Display Mode Switcher – {state}";
            trayIcon.Text = tooltip.Length <= 63 ? tooltip : "Display Mode Switcher";
        }

        private void CopyDiagnostics()
        {
            DisplayMode? currentMode;
            try { currentMode = DisplayManager.GetCurrentDisplayMode(); }
            catch { currentMode = null; }

            var report = DiagnosticReportFormatter.Format(DiagnosticReportFormatter.Create(
                _profileManager.Status, _profileManager.DiagnosticEvents, currentMode));
            try
            {
                Clipboard.SetText(report);
            }
            catch (Exception ex) when (ex is ExternalException or ThreadStateException)
            {
                if (_clipboardErrorShown) return;
                _clipboardErrorShown = true;
                MessageBox.Show("Die Diagnose konnte nicht in die Zwischenablage kopiert werden. Bitte erneut versuchen.", "Diagnose", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void RefreshAutostartItem(ToolStripMenuItem item)
        {
            var status = _autostart.GetStatus();
            item.Checked = status.IsEnabled;
            item.Enabled = status.Error is null;
            item.ToolTipText = status.Error ?? string.Empty;
        }


        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _statusTimer.Stop();
            trayIcon.Visible = false;
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _statusTimer.Dispose();
            trayIcon.Dispose();
            contextMenu.Dispose();
            base.OnFormClosed(e);
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
                        var result = DisplayManager.SetDisplayMode(width, height, freq);
                        if (!result.Success)
                            MessageBox.Show(result.Error ?? $"Fehler beim Umschalten auf {width}x{height} @ {freq}Hz", "Anzeigemodus", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    };

                    submenu.DropDownItems.Add(item);
                }

                contextMenu.Items.Add(submenu);
            }
        }

    }
}
