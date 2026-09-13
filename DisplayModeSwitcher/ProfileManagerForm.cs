using System.Diagnostics;

namespace DisplayModeSwitcher;

public class ProfileManagerForm : Form
{
    private readonly ProfileStore _store;
    private readonly ProfileManager _profileManager;
    private readonly IProcessProvider _processProvider;
    private readonly ProfileTargetEditor _targets;
    private readonly ComboBox _processBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _monitorBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _modeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Enabled = false };
    private readonly ComboBox _policyBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ListBox _targetList = new() { HorizontalScrollbar = true };
    private readonly ListBox _profileList = new() { HorizontalScrollbar = true };
    private readonly CheckBox _showAllProcesses = new() { Text = "Alle Prozesse anzeigen", AutoSize = true };
    private readonly Button _browseButton = new() { Text = "Durchsuchen…" };
    private readonly Button _targetButton = new() { Text = "Monitor hinzufügen" };
    private readonly Button _newTargetButton = new() { Text = "Neues Ziel" };
    private readonly Button _removeTargetButton = new() { Text = "Ziel entfernen", Enabled = false };
    private readonly Button _rebindButton = new() { Text = "Ziel neu zuordnen", Enabled = false };
    private readonly Button _saveButton = new() { Text = "Profil speichern" };
    private readonly Button _removeButton = new() { Text = "Profil entfernen" };
    private readonly Button _newButton = new() { Text = "Neues Profil" };
    private readonly Button _launchButton = new() { Text = "Im Profilmodus starten", Enabled = false };
    private readonly Label _targetStatus = new() { AutoEllipsis = true };
    private readonly Label _editStateLabel = new() { Text = "Neues Profil", AutoSize = true };
    private readonly Label _launchTypeLabel = new() { Text = "Startart nur beim Klick: –", AutoSize = true };
    private int _processVersion, _topologyVersion, _modeVersion, _launchVersion;
    private bool _closing, _launching, _suppress;
    private string? _editingKey, _preferredProcessPath;
    private ProfileRetentionPolicy? _editingPolicy;

    private sealed class ProfileItem
    {
        public required string Key { get; init; }
        public required DisplayProfile Profile { get; init; }
        public required ProcessPresentationItem Presentation { get; init; }
        public bool Missing { get; init; }
        public override string ToString() => $"{Presentation} · {DisplayProfilePresentation.DescribeTargets(Profile)} · {ProfileRetentionPolicyText.ToShortDisplayName(Profile.Policy)}" + (Missing ? " — Datei fehlt" : "");
    }
    private sealed record PolicyItem(ProfileRetentionPolicy Policy) { public override string ToString() => ProfileRetentionPolicyText.ToDisplayName(Policy); }

    public ProfileManagerForm(ProfileStore store, ProfileManager profileManager, IDisplayTopologyService topologyService, IProcessProvider? processProvider = null)
    {
        _store = store;
        _profileManager = profileManager;
        _processProvider = processProvider ?? new SystemProcessProvider();
        _targets = new ProfileTargetEditor(topologyService);
        Text = "Profile verwalten";
        MinimumSize = new Size(780, 650);
        Size = new Size(900, 760);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        foreach (var policy in Enum.GetValues<ProfileRetentionPolicy>()) _policyBox.Items.Add(new PolicyItem(policy));
        BuildLayout();
        SelectPolicy(ProfileRetentionPolicy.Startup);

        Load += (_, _) => { RefreshProcesses(); RefreshTopology(); RefreshProfiles(); StartNewProfile(); };
        FormClosing += (_, _) => { _closing = true; _processVersion++; _topologyVersion++; _modeVersion++; _launchVersion++; };
        _saveButton.Click += SaveProfile;
        _removeButton.Click += RemoveProfile;
        _browseButton.Click += Browse;
        _newButton.Click += (_, _) => StartNewProfile();
        _launchButton.Click += Launch;
        _targetButton.Click += AddOrUpdateTarget;
        _newTargetButton.Click += (_, _) => StartNewTarget();
        _removeTargetButton.Click += RemoveTarget;
        _rebindButton.Click += RebindTarget;
        _targetList.SelectedIndexChanged += (_, _) => EditTarget();
        _monitorBox.SelectedIndexChanged += (_, _) => { if (!_suppress) { LoadModes(_monitorBox.SelectedItem as MonitorChoice, PreservedModeForChoice()); UpdateState(); } };
        _modeBox.SelectedIndexChanged += (_, _) => UpdateState();
        _profileList.SelectedIndexChanged += (_, _) => EditProfile();
        _profileList.DoubleClick += (_, _) => EditProfile();
        _processBox.SelectedIndexChanged += (_, _) => { _processVersion++; _preferredProcessPath = (_processBox.SelectedItem as ProcessPresentationItem)?.Path; UpdateState(); };
        _policyBox.SelectedIndexChanged += (_, _) => UpdateState();
        _showAllProcesses.CheckedChanged += (_, _) => RefreshProcesses();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 105));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 52));

        var app = Group("Anwendung");
        var a = Grid(3, 2); a.ColumnStyles.Add(new(SizeType.Percent, 100)); a.ColumnStyles.Add(new(SizeType.Absolute, 125)); a.ColumnStyles.Add(new(SizeType.Absolute, 190));
        Fill(_processBox); Fill(_browseButton); AnchorLeft(_showAllProcesses); AnchorLeft(_launchTypeLabel);
        a.Controls.Add(_processBox, 0, 0); a.SetColumnSpan(_processBox, 2); a.Controls.Add(_browseButton, 2, 0); a.Controls.Add(_showAllProcesses, 0, 1); a.Controls.Add(_launchTypeLabel, 1, 1); a.SetColumnSpan(_launchTypeLabel, 2); app.Controls.Add(a);

        var monitor = Group("Monitorziele");
        var m = Grid(4, 4); m.ColumnStyles.Add(new(SizeType.Percent, 48)); m.ColumnStyles.Add(new(SizeType.Percent, 32)); m.ColumnStyles.Add(new(SizeType.Absolute, 135)); m.ColumnStyles.Add(new(SizeType.Absolute, 145));
        m.RowStyles.Add(new(SizeType.Absolute, 34)); m.RowStyles.Add(new(SizeType.Percent, 100)); m.RowStyles.Add(new(SizeType.Absolute, 34)); m.RowStyles.Add(new(SizeType.Absolute, 28));
        foreach (var control in new Control[] { _monitorBox, _modeBox, _targetButton, _newTargetButton, _targetList, _removeTargetButton, _rebindButton, _targetStatus }) Fill(control);
        m.Controls.Add(_monitorBox, 0, 0); m.Controls.Add(_modeBox, 1, 0); m.Controls.Add(_targetButton, 2, 0); m.Controls.Add(_newTargetButton, 3, 0);
        m.Controls.Add(_targetList, 0, 1); m.SetColumnSpan(_targetList, 4); m.Controls.Add(_removeTargetButton, 2, 2); m.Controls.Add(_rebindButton, 3, 2); m.Controls.Add(_targetStatus, 0, 3); m.SetColumnSpan(_targetStatus, 4); monitor.Controls.Add(m);

        var behavior = Group("Verhalten");
        var b = Grid(3, 2); b.ColumnStyles.Add(new(SizeType.Absolute, 180)); b.ColumnStyles.Add(new(SizeType.Percent, 100)); b.ColumnStyles.Add(new(SizeType.Absolute, 210));
        var policyLabel = new Label { Text = "Modus beibehalten:", AutoSize = true }; AnchorLeft(policyLabel); Fill(_policyBox); Fill(_launchButton);
        b.Controls.Add(policyLabel, 0, 0); b.Controls.Add(_policyBox, 1, 0); b.Controls.Add(_launchButton, 2, 0); b.Controls.Add(_editStateLabel, 0, 1); b.SetColumnSpan(_editStateLabel, 3); behavior.Controls.Add(b);

        var profiles = Group("Profile");
        var p = Grid(3, 2); p.ColumnStyles.Add(new(SizeType.Percent, 100)); p.ColumnStyles.Add(new(SizeType.Absolute, 140)); p.ColumnStyles.Add(new(SizeType.Absolute, 140)); p.RowStyles.Add(new(SizeType.Percent, 100)); p.RowStyles.Add(new(SizeType.Absolute, 38));
        foreach (var control in new Control[] { _profileList, _saveButton, _newButton, _removeButton }) Fill(control);
        p.Controls.Add(_profileList, 0, 0); p.SetColumnSpan(_profileList, 3); p.Controls.Add(_saveButton, 0, 1); p.Controls.Add(_newButton, 1, 1); p.Controls.Add(_removeButton, 2, 1); profiles.Controls.Add(p);
        root.Controls.Add(app, 0, 0); root.Controls.Add(monitor, 0, 1); root.Controls.Add(behavior, 0, 2); root.Controls.Add(profiles, 0, 3); Controls.Add(root);
    }

    private static GroupBox Group(string text) => new() { Text = text, Dock = DockStyle.Fill };
    private static TableLayoutPanel Grid(int columns, int rows) => new() { Dock = DockStyle.Fill, Padding = new Padding(8), ColumnCount = columns, RowCount = rows };
    private static void Fill(Control control) => control.Dock = DockStyle.Fill;
    private static void AnchorLeft(Control control) => control.Anchor = AnchorStyles.Left;

    private void RefreshTopology()
    {
        var version = ++_topologyVersion; _targetStatus.Text = "Monitore werden gelesen…";
        Task.Run(_targets.RefreshTopology).ContinueWith(task => Ui(() =>
        {
            if (version != _topologyVersion) return;
            PopulateChoices();
            _targetStatus.Text = task.IsCompletedSuccessfully && task.Result.Success ? "Bitte einen Monitor wählen und dessen Modus hinzufügen." : task.Exception?.GetBaseException().Message ?? task.Result.Error;
            RefreshTargets();
        }));
    }

    private void PopulateChoices()
    {
        var selector = (_monitorBox.SelectedItem as MonitorChoice)?.Selector;
        _suppress = true; _monitorBox.Items.Clear(); foreach (var choice in _targets.Choices) _monitorBox.Items.Add(choice);
        _monitorBox.SelectedItem = selector is null ? null : _targets.FindChoice(selector); _suppress = false;
    }

    private DisplayMode? PreservedModeForChoice() => _targetList.SelectedItem is TargetDraftState state && _monitorBox.SelectedItem is MonitorChoice choice && MonitorSelectorIdentity.Equals(state.Target.MonitorSelector, choice.Selector) ? state.Target.Mode : null;

    private void LoadModes(MonitorChoice? choice, DisplayMode? preserved)
    {
        var version = ++_modeVersion; _modeBox.Enabled = false; _modeBox.Items.Clear();
        UpdateState();
        if (choice is null) { if (preserved is not null) SelectMode(preserved, true); return; }
        _targetStatus.Text = "Anzeigemodi werden gelesen…";
        Task.Run(() => _targets.GetModes(choice, preserved)).ContinueWith(task => Ui(() =>
        {
            if (version != _modeVersion) return;
            _modeBox.Items.Clear();
            if (!task.IsCompletedSuccessfully) { _targetStatus.Text = task.Exception?.GetBaseException().Message ?? "Die Anzeigemodi konnten nicht gelesen werden."; return; }
            foreach (var mode in task.Result.Modes) _modeBox.Items.Add(mode);
            _modeBox.Enabled = task.Result.Success;
            if (preserved is not null) SelectMode(preserved, true);
            _targetStatus.Text = task.Result.Success ? "Modus bewusst auswählen; es wird nichts sofort angewendet." : task.Result.Error;
            UpdateState();
        }));
    }

    private void AddOrUpdateTarget(object? sender, EventArgs e)
    {
        var result = _targetList.SelectedIndex >= 0
            ? _targets.Update(_targetList.SelectedIndex, _monitorBox.SelectedItem as MonitorChoice, _modeBox.SelectedItem as DisplayMode)
            : _targets.Add(_monitorBox.SelectedItem as MonitorChoice, _modeBox.SelectedItem as DisplayMode);
        if (!result.Success) TargetError(result.Error!); else { RefreshTargets(); StartNewTarget(); }
        UpdateState();
    }

    private void RemoveTarget(object? sender, EventArgs e)
    {
        var result = _targets.Remove(_targetList.SelectedIndex);
        if (!result.Success) TargetError(result.Error!); else { RefreshTargets(); StartNewTarget(); }
        UpdateState();
    }

    private void RebindTarget(object? sender, EventArgs e)
    {
        if (_targetList.SelectedItem is not TargetDraftState state || state.Target.MonitorSelector is not SpecificMonitor old || _monitorBox.SelectedItem is not MonitorChoice { Endpoint: not null } choice)
        { TargetError("Bitte das alte spezifische Ziel, danach einen aktuell verbundenen neuen Monitor auswählen."); return; }
        if (MonitorSelectorIdentity.Equals(old, choice.Selector)) { TargetError("Dieses Ziel ist bereits mit dem ausgewählten Monitor verbunden."); return; }
        var question = $"Altes Ziel:\n{old.FriendlyNameSnapshot}\n{ProfileTargetEditor.ShortPath(old.MonitorDevicePath)}\n\nNeues Ziel:\n{choice.Label}\n\nZiel wirklich neu zuordnen?";
        if (MessageBox.Show(this, question, "Monitorziel neu zuordnen", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) { EditTarget(); return; }
        var result = _targets.Rebind(state.Index, choice, true, _modeBox.SelectedItem as DisplayMode);
        if (!result.Success) TargetError(result.Error ?? "Das Ziel wurde nicht geändert."); else { RefreshTargets(); StartNewTarget(); }
        UpdateState();
    }

    private void StartNewTarget()
    {
        _modeVersion++; _targetList.ClearSelected(); _monitorBox.SelectedIndex = -1; _modeBox.Items.Clear(); _modeBox.Enabled = false;
        _targetButton.Text = "Monitor hinzufügen"; _removeTargetButton.Enabled = false; _rebindButton.Enabled = false;
        _targetStatus.Text = "Bitte einen Monitor wählen; es gibt keine automatische Vorauswahl.";
        UpdateState();
    }

    private void EditTarget()
    {
        if (_targetList.SelectedItem is not TargetDraftState state) { _targetButton.Text = "Monitor hinzufügen"; _removeTargetButton.Enabled = _rebindButton.Enabled = false; return; }
        _targetButton.Text = "Ziel aktualisieren"; _removeTargetButton.Enabled = true; _rebindButton.Enabled = state.Target.MonitorSelector is SpecificMonitor;
        var choice = _targets.FindChoice(state.Target.MonitorSelector); _suppress = true; _monitorBox.SelectedItem = choice; _suppress = false;
        if (choice is null) { _modeBox.Items.Clear(); SelectMode(state.Target.Mode, true); _modeBox.Enabled = false; _targetStatus.Text = state.Detail; }
        else LoadModes(choice, state.Target.Mode);
    }

    private void RefreshTargets()
    {
        _targetList.Items.Clear(); foreach (var state in _targets.GetTargetStates()) _targetList.Items.Add(state); UpdateState();
    }

    private void SaveProfile(object? sender, EventArgs e)
    {
        if (_processBox.SelectedItem is not ProcessPresentationItem process || _policyBox.SelectedItem is not PolicyItem policy) { MessageBox.Show(this, "Bitte eine Anwendung und ein Verhalten auswählen.", "Profile", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var preparation = ProfileTargetSavePreparation.Apply(
            _targets,
            _targetList.SelectedItem as TargetDraftState,
            _monitorBox.SelectedItem as MonitorChoice,
            _modeBox.SelectedItem as DisplayMode);
        if (!preparation.Success) { TargetError(preparation.Error!); return; }
        if (preparation.TargetUpdated) { RefreshTargets(); StartNewTarget(); }
        if (!_targets.CanSave) { TargetError(_targets.Targets.Count == 0 ? "Bitte mindestens ein Monitorziel hinzufügen." : "Ein Monitorziel ist mehrdeutig, nicht persistierbar oder Teil einer Klon-Gruppe und muss zuerst korrigiert werden."); return; }
        var result = ProfileEditWorkflow.Save(_store, _editingKey, process.Path, _targets.BuildProfile(policy.Policy));
        if (!result.Success) MessageBox.Show(this, result.Error ?? "Das Profil konnte nicht gespeichert werden.", "Profile", MessageBoxButtons.OK, MessageBoxIcon.Error);
        else { RefreshProfiles(); SelectProfile(process.Path); }
    }

    private void EditProfile()
    {
        if (_profileList.SelectedItem is not ProfileItem item) { UpdateState(); return; }
        _editingKey = item.Key; _editingPolicy = item.Profile.Policy; _targets.Load(item.Profile); _saveButton.Text = "Profil aktualisieren";
        SelectProcess(item.Key); SelectPolicy(item.Profile.Policy); RefreshTargets(); StartNewTarget(); RefreshLaunchType(item); UpdateState();
    }

    private void StartNewProfile()
    {
        _editingKey = null; _editingPolicy = null; _targets.BeginNew(); _saveButton.Text = "Profil speichern"; _profileList.ClearSelected();
        _launchVersion++; _launchTypeLabel.Text = "Startart nur beim Klick: –"; SelectPolicy(ProfileRetentionPolicy.Startup); RefreshTargets(); StartNewTarget(); UpdateState();
    }

    private bool Dirty()
    {
        if (_editingKey is null) return _targets.Targets.Count > 0 || PendingTargetEdit();
        return _targets.IsDirty || PendingTargetEdit() || !string.Equals((_processBox.SelectedItem as ProcessPresentationItem)?.Path, _editingKey, StringComparison.OrdinalIgnoreCase) || (_policyBox.SelectedItem as PolicyItem)?.Policy != _editingPolicy;
    }

    private bool PendingTargetEdit()
    {
        if (_monitorBox.SelectedItem is not MonitorChoice choice) return false;
        if (_targetList.SelectedItem is not TargetDraftState state) return true;
        if (!MonitorSelectorIdentity.Equals(state.Target.MonitorSelector, choice.Selector)) return true;
        return _modeBox.SelectedItem is not DisplayMode mode || !mode.Equals(state.Target.Mode);
    }

    private void UpdateState()
    {
        if (_suppress) return; var dirty = Dirty();
        _editStateLabel.Text = _editingKey is null ? "Neues Profil — Monitorwahl erforderlich" : dirty ? "Profil bearbeitet — erst speichern, dann starten" : "Gespeichertes Profil";
        _saveButton.Enabled = _targets.CanSave && ProfileTargetSavePreparation.CanPrepare(
            _targetList.SelectedItem as TargetDraftState,
            _monitorBox.SelectedItem as MonitorChoice,
            _modeBox.SelectedItem as DisplayMode);
        var item = _profileList.SelectedItem as ProfileItem;
        _launchButton.Enabled = !_launching && !dirty && item is not null && !_targets.HasBlockingResolution && item.Profile.Targets.Count > 0 && Path.IsPathFullyQualified(item.Key) && !item.Missing;
    }

    private void RefreshProcesses()
    {
        var version = ++_processVersion; var all = _showAllProcesses.Checked; var requested = _preferredProcessPath ?? (_processBox.SelectedItem as ProcessPresentationItem)?.Path;
        Task.Run(() => ProcessPresentation.CreateItems(_processProvider.GetCurrentSessionProcesses().Select(ProcessInfo).ToList(), Application.ExecutablePath, all).ToList()).ContinueWith(task => Ui(() =>
        {
            if (version != _processVersion || !task.IsCompletedSuccessfully) return; var items = task.Result;
            if (!string.IsNullOrWhiteSpace(requested) && !items.Any(item => string.Equals(item.Path, requested, StringComparison.OrdinalIgnoreCase))) items.Add(ProcessPresentation.CreateProfileItem(requested));
            _processBox.Items.Clear(); foreach (var item in items) _processBox.Items.Add(item); var index = items.FindIndex(item => string.Equals(item.Path, requested, StringComparison.OrdinalIgnoreCase)); _processBox.SelectedIndex = index >= 0 ? index : items.Count > 0 ? 0 : -1;
        }));
    }

    private static ProcessDisplayInfo ProcessInfo(ProcessIdentity identity)
    {
        try { using var process = Process.GetProcessById(identity.Id); return new(identity, null, null, process.MainWindowTitle, process.MainWindowHandle); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { return new(identity, null, null, null, 0); }
    }

    private void RefreshProfiles()
    {
        _launchVersion++; _profileList.Items.Clear();
        foreach (var item in _store.Profiles) _profileList.Items.Add(new ProfileItem { Key = item.Key, Profile = item.Value, Presentation = ProcessPresentation.CreateProfileItem(item.Key), Missing = ProcessPresentation.IsMissingProfileExecutable(item.Key) });
        UpdateState();
    }

    private void SelectProfile(string path) { var item = _profileList.Items.OfType<ProfileItem>().FirstOrDefault(candidate => string.Equals(candidate.Key, path, StringComparison.OrdinalIgnoreCase)); if (item is not null) _profileList.SelectedItem = item; }

    private void Browse(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog { Filter = "Anwendungen (*.exe)|*.exe", DefaultExt = "exe", CheckFileExists = true, CheckPathExists = true, Multiselect = false, Title = "Anwendung für Profil auswählen" };
        var path = (_processBox.SelectedItem as ProcessPresentationItem)?.Path; if (!string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)) { var directory = Path.GetDirectoryName(path); if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)) dialog.InitialDirectory = directory; }
        if (string.IsNullOrWhiteSpace(dialog.InitialDirectory)) dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (dialog.ShowDialog(this) == DialogResult.OK) SelectProcess(ProcessMatcher.CanonicalizePath(dialog.FileName));
    }

    private async void Launch(object? sender, EventArgs e)
    {
        if (_launching || Dirty() || _profileList.SelectedItem is not ProfileItem item) return; _launching = true; UpdateState();
        try { var result = await _profileManager.LaunchProfileApplicationAsync(item.Key); if (!result.Success && !_closing && !IsDisposed) MessageBox.Show(this, result.Error ?? "Die Anwendung konnte nicht im Profilmodus gestartet werden.", "Profilstart", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        catch (ObjectDisposedException) { if (!_closing && !IsDisposed) MessageBox.Show(this, "Die Profilüberwachung wurde bereits beendet.", "Profilstart", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { _launching = false; if (!_closing && !IsDisposed) UpdateState(); }
    }

    private async void RefreshLaunchType(ProfileItem item)
    {
        var version = ++_launchVersion; if (!Path.IsPathFullyQualified(item.Key) || item.Missing) { _launchTypeLabel.Text = "Startart nur beim Klick: –"; return; } _launchTypeLabel.Text = "Startart beim Klick wird erkannt…";
        try { var plan = await _profileManager.ResolveLaunchPlanAsync(item.Key); if (!_closing && !IsDisposed && version == _launchVersion) _launchTypeLabel.Text = $"Startart nur beim Klick: {plan.DisplayName}"; }
        catch { if (!_closing && !IsDisposed && version == _launchVersion) _launchTypeLabel.Text = "Startart nur beim Klick: Direkt"; }
    }

    private void SelectProcess(string path) { _processVersion++; _preferredProcessPath = path; var item = _processBox.Items.OfType<ProcessPresentationItem>().FirstOrDefault(candidate => string.Equals(candidate.Path, path, StringComparison.OrdinalIgnoreCase)); if (item is null) { item = ProcessPresentation.CreateProfileItem(path); _processBox.Items.Add(item); } _processBox.SelectedItem = item; }
    private void SelectMode(DisplayMode mode, bool unavailable) { var item = _modeBox.Items.OfType<DisplayMode>().FirstOrDefault(candidate => candidate.Equals(mode)); if (item is null) { item = new DisplayMode { Width = mode.Width, Height = mode.Height, Frequency = mode.Frequency, Label = $"{mode.Width}x{mode.Height} @ {mode.Frequency}Hz" + (unavailable ? " (derzeit nicht verfügbar)" : "") }; _modeBox.Items.Add(item); } _modeBox.SelectedItem = item; }
    private void SelectPolicy(ProfileRetentionPolicy policy) => _policyBox.SelectedItem = _policyBox.Items.OfType<PolicyItem>().First(item => item.Policy == policy);
    private void RemoveProfile(object? sender, EventArgs e) { if (_profileList.SelectedItem is not ProfileItem item) return; var result = ProfileEditWorkflow.Remove(_store, item.Key); if (!result.Success) MessageBox.Show(this, result.Error ?? "Das Profil konnte nicht entfernt werden.", "Profile", MessageBoxButtons.OK, MessageBoxIcon.Error); else { RefreshProfiles(); StartNewProfile(); } }
    private void TargetError(string error) => MessageBox.Show(this, error, "Monitorziele", MessageBoxButtons.OK, MessageBoxIcon.Information);
    private void Ui(Action action) { if (_closing || IsDisposed || !IsHandleCreated) return; try { BeginInvoke(new Action(() => { if (!_closing && !IsDisposed) action(); })); } catch (InvalidOperationException) { } }
}
