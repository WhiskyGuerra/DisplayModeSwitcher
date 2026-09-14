using System;
using System.Drawing;
using System.Diagnostics;
using System.Reflection;
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
        private readonly ManualDisplaySwitcher _manualDisplay;
        private readonly HttpClient _updateHttpClient;
        private readonly HttpClient _updateDownloadHttpClient;
        private readonly IUpdateChecker _updateChecker;
        private readonly IUpdatePackageStager _updateStager;
        private readonly Icon _applicationIcon;
        private readonly NotifyIcon trayIcon;
        private readonly ContextMenuStrip contextMenu;
        private readonly ToolStripMenuItem _statusItem;
        private readonly System.Windows.Forms.Timer _statusTimer;
        private bool _clipboardErrorShown;
        public TrayForm(ProfileStore store, AutostartService autostart, ProfileManager profileManager, IDisplayTopologyService topology, ITargetedDisplayService targetedDisplay)
        {
            _store = store;
            _autostart = autostart;
            _profileManager = profileManager;
            _topology = topology;
            _manualDisplay = new ManualDisplaySwitcher(
                topology,
                targetedDisplay,
                () => _profileManager.Status,
                _profileManager.RecordDiagnosticEvent);
            _updateHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            _updateChecker = new GitHubReleaseUpdateChecker(_updateHttpClient);
            _updateDownloadHttpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            _updateStager = new UpdatePackageStager(
                _updateDownloadHttpClient,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisplayModeSwitcher", "Updates"));
            _applicationIcon = ApplicationIconProvider.Create();
            contextMenu = new ContextMenuStrip();

            var manualModeItem = new ToolStripMenuItem("Anzeigemodus manuell...");
            manualModeItem.Click += (s, e) =>
            {
                using var form = new ManualDisplayForm(_manualDisplay);
                form.ShowDialog();
            };
            contextMenu.Items.Add(manualModeItem);

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

            var updateItem = new ToolStripMenuItem("Auf Updates prüfen...");
            updateItem.Click += async (_, _) => await CheckForUpdatesAsync(updateItem);
            contextMenu.Items.Add(updateItem);

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
                Icon = _applicationIcon,
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

        private async Task CheckForUpdatesAsync(ToolStripMenuItem item)
        {
            item.Enabled = false;
            item.Text = "Prüfe auf Updates...";
            try
            {
                var currentVersion = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0, 0);
                var result = await _updateChecker.CheckAsync(currentVersion);
                if (result.State == UpdateCheckState.NoPublishedRelease)
                {
                    MessageBox.Show(this, "Für das Projekt wurde noch kein stabiles GitHub-Release veröffentlicht.", "Updateprüfung", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (result.State == UpdateCheckState.UpToDate)
                {
                    MessageBox.Show(this, $"Version {currentVersion} ist aktuell.", "Updateprüfung", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (result.State == UpdateCheckState.Failed || result.Release is null)
                {
                    MessageBox.Show(this, result.Error ?? "Die Updateprüfung ist fehlgeschlagen.", "Updateprüfung", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (result.Release.Package is null)
                {
                    var open = MessageBox.Show(this,
                        $"Version {result.Release.Version} ist verfügbar, enthält aber kein verifizierbares Update-Paket.\n\n{result.Release.PackageIssue}\n\nSoll die sichere GitHub-Release-Seite geöffnet werden?",
                        "Update ohne Paket", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (open == DialogResult.Yes)
                        Process.Start(new ProcessStartInfo(result.Release.ReleasePage.AbsoluteUri) { UseShellExecute = true });
                    return;
                }

                var sizeMb = result.Release.Package.ArchiveSize / 1024d / 1024d;
                var download = MessageBox.Show(this,
                    $"Version {result.Release.Version} ist verfügbar ({sizeMb:0.0} MB).\n\nDas Paket und seine SHA-256-Prüfsumme jetzt herunterladen und sicher prüfen? Die laufende Installation wird dabei noch nicht verändert.",
                    "Update verfügbar", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (download != DialogResult.Yes) return;

                item.Text = "Update wird geprüft...";
                var staged = await _updateStager.StageAsync(result.Release.Package, result.Release.Version);
                if (!staged.Success)
                {
                    MessageBox.Show(this, staged.Error ?? "Das Update konnte nicht sicher bereitgestellt werden.", "Updateprüfung", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var prepared = UpdateArchivePreparer.Prepare(
                    staged.ArchivePath!, staged.StagingDirectory!, Application.ExecutablePath,
                    Environment.ProcessId, result.Release.Version);
                if (!prepared.Success || prepared.Update is null)
                {
                    MessageBox.Show(this, prepared.Error ?? "Das geprüfte Update-Paket konnte nicht sicher vorbereitet werden.",
                        "Update nicht installierbar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (_profileManager.Status.State != ProfileMonitorState.Idle)
                {
                    MessageBox.Show(this,
                        "Das Update ist geprüft und vorbereitet, kann aber nicht installiert werden, solange ein Profil aktiv ist oder wiederhergestellt wird.\n\nBitte die Profilanwendung beenden und die Updateprüfung erneut starten.",
                        "Update wartet", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var install = MessageBox.Show(this,
                    $"Version {result.Release.Version} wurde vollständig heruntergeladen, per SHA-256 geprüft und sicher entpackt.\n\nJetzt installieren? Display Mode Switcher wird beendet, mit Backup aktualisiert und anschließend neu gestartet. Profile und Autostart-Pfad bleiben erhalten.",
                    "Update installieren", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);
                if (install != DialogResult.Yes) return;

                if (_profileManager.Status.State != ProfileMonitorState.Idle)
                {
                    MessageBox.Show(this,
                        "Inzwischen wurde ein Profil aktiv. Die Installation wurde zur Sicherheit nicht gestartet.",
                        "Update wartet", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (!UpdateInstallerProcess.Launch(prepared.Update))
                {
                    MessageBox.Show(this, "Der separate Updater konnte nicht gestartet werden. Die laufende Installation blieb unverändert.",
                        "Update fehlgeschlagen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                Application.Exit();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Die Updateprüfung ist fehlgeschlagen: {ex.Message}", "Updateprüfung", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                if (!IsDisposed && !Disposing)
                {
                    item.Text = "Auf Updates prüfen...";
                    item.Enabled = true;
                }
            }
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
            _applicationIcon.Dispose();
            _updateHttpClient.Dispose();
            _updateDownloadHttpClient.Dispose();
            contextMenu.Dispose();
            base.OnFormClosed(e);
        }
    }
}
