using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DisplayModeSwitcher
{
    public class ProfileManagerForm : Form
    {
        private readonly ProfileStore _store;
        private readonly ProfileManager _profileManager;
        private readonly ComboBox _processBox;
        private readonly ComboBox _modeBox;
        private readonly ComboBox _policyBox;
        private readonly Button _addButton;
        private readonly Button _removeButton;
        private readonly Button _browseButton;
        private readonly Button _newButton;
        private readonly Button _launchButton;
        private readonly ListBox _profileList;
        private readonly IProcessProvider _processProvider;
        private readonly CheckBox _showAllProcesses;
        private readonly Label _editStateLabel;
        private readonly Label _launchTypeLabel;
        private int _processLoadVersion;
        private int _launchTypeLoadVersion;
        private bool _isClosing;
        private bool _launchInProgress;
        private bool _editingUnsupportedProfile;
        private string? _editingProcessKey;
        private string? _preferredProcessPath;

        private sealed class ProfileListItem
        {
            public string ProcessKey { get; set; } = string.Empty;
            public ProcessPresentationItem Presentation { get; set; } = new(string.Empty, string.Empty, string.Empty);
            public DisplayProfile Profile { get; set; } = new(new DisplayMode());
            public bool IsMissingExecutable { get; set; }
            public override string ToString() => $"{Presentation} · {DisplayProfilePresentation.DescribeTargets(Profile)} · {ProfileRetentionPolicyText.ToShortDisplayName(Profile.Policy)}" + (IsMissingExecutable ? " — Datei fehlt" : string.Empty);
        }

        private sealed record PolicyListItem(ProfileRetentionPolicy Policy)
        {
            public override string ToString() => ProfileRetentionPolicyText.ToDisplayName(Policy);
        }

        public ProfileManagerForm(ProfileStore store, ProfileManager profileManager, IProcessProvider? processProvider = null)
        {
            _store = store;
            _profileManager = profileManager;
            _processProvider = processProvider ?? new SystemProcessProvider();

            Text = "Profile verwalten";
            Width = 660;
            Height = 435;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            _processBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 10, Top = 10, Width = 360 };
            _modeBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 380, Top = 10, Width = 250 };
            _showAllProcesses = new CheckBox { Text = "Alle Prozesse anzeigen", Left = 10, Top = 43, AutoSize = true };
            var policyLabel = new Label { Text = "Modus beibehalten:", Left = 250, Top = 44, AutoSize = true };
            _policyBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 380, Top = 40, Width = 250 };
            foreach (var policy in Enum.GetValues<ProfileRetentionPolicy>())
                _policyBox.Items.Add(new PolicyListItem(policy));
            SelectPolicy(ProfileRetentionPolicy.Startup);
            _browseButton = new Button { Text = "Durchsuchen…", Left = 10, Top = 70, Width = 105 };
            _addButton = new Button { Text = "Profil speichern", Left = 125, Top = 70, Width = 110 };
            _newButton = new Button { Text = "Neu", Left = 245, Top = 70, Width = 80 };
            _removeButton = new Button { Text = "Entfernen", Left = 335, Top = 70, Width = 100 };
            _launchButton = new Button { Text = "Im Profilmodus starten", Left = 445, Top = 70, Width = 185, Enabled = false };
            _editStateLabel = new Label { Text = "Neues Profil", Left = 10, Top = 103, AutoSize = true };
            _launchTypeLabel = new Label { Text = "Startart nur beim Klick: –", Left = 445, Top = 105, AutoSize = true };
            _profileList = new ListBox { Left = 10, Top = 128, Width = 620, Height = 255, HorizontalScrollbar = true };

            Controls.AddRange(new Control[] { _processBox, _modeBox, _showAllProcesses, policyLabel, _policyBox, _browseButton, _addButton, _newButton, _removeButton, _launchButton, _editStateLabel, _launchTypeLabel, _profileList });

            Load += ProfileManagerForm_Load;
            _addButton.Click += OnAdd;
            _removeButton.Click += OnRemove;
            _browseButton.Click += OnBrowse;
            _newButton.Click += (_, _) => StartNewProfile();
            _launchButton.Click += OnLaunch;
            _profileList.SelectedIndexChanged += (_, _) => BeginEditingSelectedProfile();
            _profileList.DoubleClick += (_, _) => BeginEditingSelectedProfile();
            _processBox.SelectedIndexChanged += (_, _) => _preferredProcessPath = (_processBox.SelectedItem as ProcessPresentationItem)?.Path;
            _showAllProcesses.CheckedChanged += (_, _) => RefreshProcesses();
            FormClosing += (_, _) => { _isClosing = true; _processLoadVersion++; _launchTypeLoadVersion++; };
        }

        private void ProfileManagerForm_Load(object? sender, EventArgs e)
        {
            RefreshProcesses();
            RefreshModes();
            RefreshProfileList();
        }

        private void RefreshProcesses()
        {
            var loadVersion = ++_processLoadVersion;
            var includeBackgroundProcesses = _showAllProcesses.Checked;
            var requestedPath = _preferredProcessPath ?? (_processBox.SelectedItem as ProcessPresentationItem)?.Path;
            Task.Run(() =>
            {
                var processes = _processProvider.GetCurrentSessionProcesses()
                    .Select(CreateDisplayInfo)
                    .ToList();
                var list = ProcessPresentation.CreateItems(processes, Application.ExecutablePath, includeBackgroundProcesses);

                if (_isClosing || IsDisposed || !IsHandleCreated)
                    return;
                try { BeginInvoke(new Action(() =>
                {
                    if (_isClosing || IsDisposed || loadVersion != _processLoadVersion)
                        return;
                    var items = list.ToList();
                    if (!string.IsNullOrWhiteSpace(requestedPath) && !items.Any(item => string.Equals(item.Path, requestedPath, StringComparison.OrdinalIgnoreCase)))
                        items.Add(ProcessPresentation.CreateProfileItem(requestedPath));

                    _processBox.Items.Clear();
                    foreach (var item in items)
                        _processBox.Items.Add(item);
                    var selected = items.FindIndex(item => string.Equals(item.Path, requestedPath, StringComparison.OrdinalIgnoreCase));
                    _processBox.SelectedIndex = selected >= 0 ? selected : _processBox.Items.Count > 0 ? 0 : -1;
                })); }
                catch (InvalidOperationException) { }
            });
        }

        private static ProcessDisplayInfo CreateDisplayInfo(ProcessIdentity identity)
        {
            try
            {
                using var process = Process.GetProcessById(identity.Id);
                return new ProcessDisplayInfo(identity, null, null, process.MainWindowTitle, process.MainWindowHandle);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                return new ProcessDisplayInfo(identity, null, null, null, 0);
            }
        }

        private void RefreshModes()
        {
            _modeBox.Items.Clear();
            var modes = DisplayManager.GetAvailableDisplayModes();
            foreach (var m in modes)
                _modeBox.Items.Add(m);
            if (_modeBox.Items.Count > 0)
                _modeBox.SelectedIndex = 0;
        }

        private void RefreshProfileList()
        {
            _launchTypeLoadVersion++;
            _launchTypeLabel.Text = "Startart nur beim Klick: –";
            _profileList.Items.Clear();
            foreach (var kvp in _store.Profiles)
            {
                _profileList.Items.Add(new ProfileListItem { ProcessKey = kvp.Key, Presentation = ProcessPresentation.CreateProfileItem(kvp.Key), Profile = kvp.Value, IsMissingExecutable = ProcessPresentation.IsMissingProfileExecutable(kvp.Key) });
            }
            UpdateLaunchButton();
        }

        private void OnAdd(object? sender, EventArgs e)
        {
            if (_editingUnsupportedProfile)
            {
                MessageBox.Show(DisplayProfileCompatibility.UnsupportedTargetsMessage, "Profile", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_processBox.SelectedItem is not ProcessPresentationItem processItem ||
                _modeBox.SelectedItem is not DisplayMode mode ||
                _policyBox.SelectedItem is not PolicyListItem policyItem)
                return;
            var process = processItem.Path;
            var result = ProfileEditWorkflow.Save(_store, _editingProcessKey, process, mode, policyItem.Policy);
            if (!result.Success)
            {
                MessageBox.Show(result.Error ?? "Das Profil konnte nicht gespeichert werden.", "Profile", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
                StartNewProfile();
            RefreshProfileList();
        }

        private void OnBrowse(object? sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "Anwendungen (*.exe)|*.exe",
                DefaultExt = "exe",
                CheckFileExists = true,
                CheckPathExists = true,
                Multiselect = false,
                Title = "Anwendung für Profil auswählen"
            };
            var selectedPath = (_processBox.SelectedItem as ProcessPresentationItem)?.Path;
            if (!string.IsNullOrWhiteSpace(selectedPath) && Path.IsPathFullyQualified(selectedPath))
            {
                var directory = Path.GetDirectoryName(selectedPath);
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)) dialog.InitialDirectory = directory;
            }
            if (string.IsNullOrWhiteSpace(dialog.InitialDirectory))
                dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            SelectProcessPath(ProcessMatcher.CanonicalizePath(dialog.FileName));
        }

        private void BeginEditingSelectedProfile()
        {
            if (_profileList.SelectedItem is not ProfileListItem item)
            {
                UpdateLaunchButton();
                return;
            }
            _editingProcessKey = item.ProcessKey;
            var supported = DisplayProfileCompatibility.TryGetCurrentPrimaryTarget(item.Profile, out _);
            _editingUnsupportedProfile = !supported;
            _editStateLabel.Text = supported ? "Profil bearbeiten" : "Monitorziele in diesem Zwischenstand nur lesbar";
            _addButton.Text = "Profil aktualisieren";
            _addButton.Enabled = supported;
            _modeBox.Enabled = supported;
            _policyBox.Enabled = supported;
            SelectProcessPath(item.ProcessKey);
            if (item.Profile.Targets.Count > 0)
                SelectMode(item.Profile.Targets[0].Mode);
            SelectPolicy(item.Profile.Policy);
            UpdateLaunchButton();
            RefreshLaunchType(item);
        }

        private void StartNewProfile()
        {
            _editingProcessKey = null;
            _editingUnsupportedProfile = false;
            _editStateLabel.Text = "Neues Profil";
            _addButton.Text = "Profil speichern";
            _addButton.Enabled = true;
            _modeBox.Enabled = true;
            _policyBox.Enabled = true;
            _profileList.ClearSelected();
            _launchTypeLoadVersion++;
            _launchTypeLabel.Text = "Startart nur beim Klick: –";
            SelectPolicy(ProfileRetentionPolicy.Startup);
            UpdateLaunchButton();
        }

        private async void OnLaunch(object? sender, EventArgs e)
        {
            if (_launchInProgress || _profileList.SelectedItem is not ProfileListItem item)
                return;

            _launchInProgress = true;
            UpdateLaunchButton();
            try
            {
                var result = await _profileManager.LaunchProfileApplicationAsync(item.ProcessKey);
                if (!result.Success && !_isClosing && !IsDisposed)
                    MessageBox.Show(this, result.Error ?? "Die Anwendung konnte nicht im Profilmodus gestartet werden.", "Profilstart", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (ObjectDisposedException)
            {
                if (!_isClosing && !IsDisposed)
                    MessageBox.Show(this, "Die Profilüberwachung wurde bereits beendet.", "Profilstart", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _launchInProgress = false;
                if (!_isClosing && !IsDisposed)
                    UpdateLaunchButton();
            }
        }

        private void UpdateLaunchButton()
        {
            var item = _profileList.SelectedItem as ProfileListItem;
            _launchButton.Enabled = !_launchInProgress && item is not null &&
                DisplayProfileCompatibility.TryGetCurrentPrimaryTarget(item.Profile, out _) &&
                Path.IsPathFullyQualified(item.ProcessKey) && !item.IsMissingExecutable;
        }

        private async void RefreshLaunchType(ProfileListItem item)
        {
            var loadVersion = ++_launchTypeLoadVersion;
            if (!DisplayProfileCompatibility.TryGetCurrentPrimaryTarget(item.Profile, out _))
            {
                _launchTypeLabel.Text = "Startart nur beim Klick: nicht unterstützt";
                return;
            }
            if (!Path.IsPathFullyQualified(item.ProcessKey) || item.IsMissingExecutable)
            {
                _launchTypeLabel.Text = "Startart nur beim Klick: –";
                return;
            }

            _launchTypeLabel.Text = "Startart beim Klick wird erkannt…";
            try
            {
                var plan = await _profileManager.ResolveLaunchPlanAsync(item.ProcessKey);
                if (_isClosing || IsDisposed || loadVersion != _launchTypeLoadVersion)
                    return;
                _launchTypeLabel.Text = $"Startart nur beim Klick: {plan.DisplayName}";
            }
            catch
            {
                if (!_isClosing && !IsDisposed && loadVersion == _launchTypeLoadVersion)
                    _launchTypeLabel.Text = "Startart nur beim Klick: Direkt";
            }
        }

        private void SelectProcessPath(string path)
        {
            _preferredProcessPath = path;
            var item = _processBox.Items.OfType<ProcessPresentationItem>().FirstOrDefault(candidate => string.Equals(candidate.Path, path, StringComparison.OrdinalIgnoreCase));
            if (item is null)
            {
                item = ProcessPresentation.CreateProfileItem(path);
                _processBox.Items.Add(item);
            }
            _processBox.SelectedItem = item;
        }

        private void SelectMode(DisplayMode mode)
        {
            var item = _modeBox.Items.OfType<DisplayMode>().FirstOrDefault(candidate => candidate.Width == mode.Width && candidate.Height == mode.Height && candidate.Frequency == mode.Frequency);
            if (item is null)
            {
                item = new DisplayMode
                {
                    Width = mode.Width,
                    Height = mode.Height,
                    Frequency = mode.Frequency,
                    Label = $"{mode.Width}x{mode.Height} @ {mode.Frequency}Hz (derzeit nicht verfügbar)"
                };
                _modeBox.Items.Add(item);
            }
            _modeBox.SelectedItem = item;
        }

        private void SelectPolicy(ProfileRetentionPolicy policy)
        {
            var item = _policyBox.Items.OfType<PolicyListItem>().FirstOrDefault(candidate => candidate.Policy == policy);
            if (item is not null) _policyBox.SelectedItem = item;
        }

        private void OnRemove(object? sender, EventArgs e)
        {
            if (_profileList.SelectedItem is not ProfileListItem item)
                return;
            var result = ProfileEditWorkflow.Remove(_store, item.ProcessKey);
            if (!result.Success)
            {
                MessageBox.Show(result.Error ?? "Das Profil konnte nicht entfernt werden.", "Profile", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
                StartNewProfile();
            RefreshProfileList();
        }
    }
}
