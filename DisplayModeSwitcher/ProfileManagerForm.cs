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
    private readonly Button _targetButton = new() { Text = "Ziel hinzufügen" };
    private readonly Button _newTargetButton = new() { Text = "Auswahl leeren" };
    private readonly Button _removeTargetButton = new() { Text = "Ziel entfernen", Enabled = false };
    private readonly Button _rebindButton = new() { Text = "Ziel neu zuordnen", Enabled = false };
    private readonly Button _saveButton = new() { Text = "Neues Profil speichern" };
    private readonly Button _removeButton = new() { Text = "Profil entfernen" };
    private readonly Button _newButton = new() { Text = "Neues Profil anlegen" };
    private readonly Button _launchButton = new() { Text = "Optional: Profil jetzt starten", Enabled = false };
    private readonly Label _targetStatus = new() { AutoEllipsis = true, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _editStateLabel = new() { Text = "Neues Profil", AutoSize = true };
    private readonly Label _launchTypeLabel = new() { Text = "Optionaler Start per Klick: –", AutoSize = true };
    private readonly ToolTip _toolTip = new() { AutoPopDelay = 15000, InitialDelay = 400, ReshowDelay = 100, ShowAlways = true };
    private int _processVersion, _topologyVersion, _modeVersion, _launchVersion;
    private bool _closing, _launching, _suppress, _topologyLoading;
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
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(920, 720);
        Size = new Size(1040, 840);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        foreach (var policy in Enum.GetValues<ProfileRetentionPolicy>()) _policyBox.Items.Add(new PolicyItem(policy));
        BuildLayout();
        ConfigureLongTextAccess();
        ConfigureTabOrder();
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
        _targetList.SelectedIndexChanged += (_, _) => { if (!_suppress) EditTarget(); };
        _monitorBox.SelectedIndexChanged += (_, _) => MonitorSelectionChanged();
        _modeBox.SelectedIndexChanged += (_, _) => UpdateState();
        _profileList.SelectedIndexChanged += (_, _) => EditProfile();
        _profileList.DoubleClick += (_, _) => EditProfile();
        _processBox.SelectedIndexChanged += (_, _) => { _processVersion++; _preferredProcessPath = (_processBox.SelectedItem as ProcessPresentationItem)?.Path; UpdateState(); };
        _policyBox.SelectedIndexChanged += (_, _) => UpdateState();
        _showAllProcesses.CheckedChanged += (_, _) => RefreshProcesses();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 48));

        var app = Group("1 · Anwendung auswählen");
        var a = Grid(3, 3); a.ColumnStyles.Add(new(SizeType.Percent, 100)); a.ColumnStyles.Add(new(SizeType.Absolute, 140)); a.ColumnStyles.Add(new(SizeType.Absolute, 200));
        a.RowStyles.Add(new(SizeType.Absolute, 23)); a.RowStyles.Add(new(SizeType.Absolute, 34)); a.RowStyles.Add(new(SizeType.Absolute, 30));
        var processLabel = FieldLabel("Anwendung / vollständiger EXE-Pfad:");
        Fill(_processBox); Fill(_browseButton); AnchorLeft(_showAllProcesses); AnchorLeft(_launchTypeLabel);
        a.Controls.Add(processLabel, 0, 0); a.SetColumnSpan(processLabel, 3);
        a.Controls.Add(_processBox, 0, 1); a.SetColumnSpan(_processBox, 2); a.Controls.Add(_browseButton, 2, 1);
        a.Controls.Add(_showAllProcesses, 0, 2); a.Controls.Add(_launchTypeLabel, 1, 2); a.SetColumnSpan(_launchTypeLabel, 2); app.Controls.Add(a);

        var monitor = Group("2 · Monitorziele zusammenstellen");
        var m = Grid(4, 5); m.ColumnStyles.Add(new(SizeType.Percent, 48)); m.ColumnStyles.Add(new(SizeType.Percent, 32)); m.ColumnStyles.Add(new(SizeType.Absolute, 190)); m.ColumnStyles.Add(new(SizeType.Absolute, 170));
        m.RowStyles.Add(new(SizeType.Absolute, 23)); m.RowStyles.Add(new(SizeType.Absolute, 34)); m.RowStyles.Add(new(SizeType.Percent, 100)); m.RowStyles.Add(new(SizeType.Absolute, 36)); m.RowStyles.Add(new(SizeType.Absolute, 30));
        foreach (var control in new Control[] { _monitorBox, _modeBox, _targetButton, _newTargetButton, _targetList, _removeTargetButton, _rebindButton, _targetStatus }) Fill(control);
        m.Controls.Add(FieldLabel("Monitor:"), 0, 0); m.Controls.Add(FieldLabel("Zielmodus:"), 1, 0);
        m.Controls.Add(_monitorBox, 0, 1); m.Controls.Add(_modeBox, 1, 1); m.Controls.Add(_targetButton, 2, 1); m.Controls.Add(_newTargetButton, 3, 1);
        m.Controls.Add(_targetList, 0, 2); m.SetColumnSpan(_targetList, 4); m.Controls.Add(_removeTargetButton, 2, 3); m.Controls.Add(_rebindButton, 3, 3); m.Controls.Add(_targetStatus, 0, 4); m.SetColumnSpan(_targetStatus, 4); monitor.Controls.Add(m);

        var behavior = Group("3 · Verhalten festlegen und Profil speichern");
        var b = Grid(3, 3); b.ColumnStyles.Add(new(SizeType.Absolute, 190)); b.ColumnStyles.Add(new(SizeType.Percent, 100)); b.ColumnStyles.Add(new(SizeType.Absolute, 240));
        b.RowStyles.Add(new(SizeType.Absolute, 34)); b.RowStyles.Add(new(SizeType.Absolute, 28)); b.RowStyles.Add(new(SizeType.Absolute, 34));
        var policyLabel = new Label { Text = "Modus beibehalten:", AutoSize = true }; AnchorLeft(policyLabel); Fill(_policyBox); Fill(_launchButton); Fill(_saveButton);
        _saveButton.AutoEllipsis = true;
        b.Controls.Add(policyLabel, 0, 0); b.Controls.Add(_policyBox, 1, 0); b.Controls.Add(_launchButton, 2, 0);
        b.Controls.Add(_editStateLabel, 0, 1); b.SetColumnSpan(_editStateLabel, 3);
        b.Controls.Add(_saveButton, 0, 2); b.SetColumnSpan(_saveButton, 3); behavior.Controls.Add(b);

        var profiles = Group("Gespeicherte Profile öffnen oder verwalten");
        var p = Grid(3, 2); p.ColumnStyles.Add(new(SizeType.Percent, 100)); p.ColumnStyles.Add(new(SizeType.Absolute, 170)); p.ColumnStyles.Add(new(SizeType.Absolute, 170)); p.RowStyles.Add(new(SizeType.Percent, 100)); p.RowStyles.Add(new(SizeType.Absolute, 38));
        foreach (var control in new Control[] { _profileList, _newButton, _removeButton }) Fill(control);
        p.Controls.Add(_profileList, 0, 0); p.SetColumnSpan(_profileList, 3); p.Controls.Add(_newButton, 1, 1); p.Controls.Add(_removeButton, 2, 1); profiles.Controls.Add(p);
        root.Controls.Add(app, 0, 0); root.Controls.Add(monitor, 0, 1); root.Controls.Add(behavior, 0, 2); root.Controls.Add(profiles, 0, 3); Controls.Add(root);
    }

    private static GroupBox Group(string text) => new() { Text = text, Dock = DockStyle.Fill };
    private static TableLayoutPanel Grid(int columns, int rows) => new() { Dock = DockStyle.Fill, Padding = new Padding(10, 8, 10, 8), ColumnCount = columns, RowCount = rows };
    private static Label FieldLabel(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
    private static void Fill(Control control) => control.Dock = DockStyle.Fill;
    private static void AnchorLeft(Control control) => control.Anchor = AnchorStyles.Left;

    private void ConfigureTabOrder()
    {
        _processBox.TabIndex = 0;
        _browseButton.TabIndex = 1;
        _showAllProcesses.TabIndex = 2;
        _monitorBox.TabIndex = 3;
        _modeBox.TabIndex = 4;
        _targetButton.TabIndex = 5;
        _newTargetButton.TabIndex = 6;
        _targetList.TabIndex = 7;
        _removeTargetButton.TabIndex = 8;
        _rebindButton.TabIndex = 9;
        _policyBox.TabIndex = 10;
        _saveButton.TabIndex = 11;
        _launchButton.TabIndex = 12;
        _profileList.TabIndex = 13;
        _newButton.TabIndex = 14;
        _removeButton.TabIndex = 15;
    }

    private void ConfigureLongTextAccess()
    {
        _toolTip.SetToolTip(_processBox, "Vollständige Anwendung und Pfad; die Liste klappt bei langen Einträgen breiter auf.");
        _toolTip.SetToolTip(_monitorBox, "Vollständiger Monitorname und Gerätepfad-Hinweis; ein bereits vorhandenes identisches Ziel wird zur Bearbeitung geöffnet.");
        _toolTip.SetToolTip(_modeBox, "Zielauflösung und Bildwiederholrate für den ausdrücklich gewählten Monitor.");
        _toolTip.SetToolTip(_targetList, "Gespeicherte Ziele dieses Profilentwurfs. Lange Einträge sind horizontal scrollbar.");
        _toolTip.SetToolTip(_profileList, "Gespeicherte Profile. Lange Einträge sind horizontal scrollbar.");
        _toolTip.SetToolTip(_launchButton, "Optionaler, bewusster Start. Auswahl oder Speichern eines Profils startet keine Anwendung.");

        foreach (var box in new[] { _processBox, _monitorBox, _modeBox, _policyBox })
        {
            box.DropDown += (_, _) => FitDropDown(box);
            box.SelectedIndexChanged += (_, _) => _toolTip.SetToolTip(box, box.SelectedItem?.ToString() ?? box.AccessibleDescription);
        }

        ConfigureListTextAccess(_targetList, item => item is TargetDraftState state ? $"{state}\n{state.Detail}" : item?.ToString());
        ConfigureListTextAccess(_profileList, item => item is ProfileItem profile ? $"{profile}\n{profile.Key}" : item?.ToString());
    }

    private void ConfigureListTextAccess(ListBox list, Func<object?, string?> description)
    {
        var lastIndex = -2;
        list.MouseMove += (_, e) =>
        {
            var index = list.IndexFromPoint(e.Location);
            if (index == lastIndex) return;
            lastIndex = index;
            _toolTip.SetToolTip(list, index >= 0 ? description(list.Items[index]) : null);
        };
        list.MouseLeave += (_, _) => lastIndex = -2;
    }

    private static void FitDropDown(ComboBox box)
    {
        var widest = box.Items.Cast<object>().Select(item => TextRenderer.MeasureText(item?.ToString() ?? string.Empty, box.Font).Width).DefaultIfEmpty(box.Width).Max();
        var screenWidth = Screen.FromControl(box).WorkingArea.Width;
        box.DropDownWidth = Math.Max(box.Width, Math.Min(widest + SystemInformation.VerticalScrollBarWidth + 16, Math.Max(box.Width, screenWidth - 80)));
    }

    private static void FitHorizontalExtent(ListBox list)
    {
        list.HorizontalExtent = list.Items.Cast<object>().Select(item => TextRenderer.MeasureText(item?.ToString() ?? string.Empty, list.Font).Width).DefaultIfEmpty(0).Max() + 16;
    }

    private void RefreshTopology()
    {
        var version = ++_topologyVersion; _topologyLoading = true; SetTargetStatus(PresentationStatusKind.Loading, "Monitore werden gelesen…");
        Task.Run(_targets.RefreshTopology).ContinueWith(task => Ui(() =>
        {
            if (version != _topologyVersion) return;
            _topologyLoading = false;
            PopulateChoices();
            if (task.IsCompletedSuccessfully && task.Result.Success)
                SetTargetStatus(PresentationStatusKind.Ready, "Monitor wählen und anschließend als Ziel hinzufügen.");
            else
                SetTargetStatus(PresentationStatusKind.Error, task.Exception?.GetBaseException().Message ?? task.Result.Error);
            RefreshTargets();
        }));
    }

    private void PopulateChoices()
    {
        var selector = (_monitorBox.SelectedItem as MonitorChoice)?.Selector;
        _suppress = true; _monitorBox.Items.Clear(); foreach (var choice in _targets.Choices) _monitorBox.Items.Add(choice);
        _monitorBox.SelectedItem = selector is null ? null : _targets.FindChoice(selector); _suppress = false;
        FitDropDown(_monitorBox);
    }

    private DisplayMode? PreservedModeForChoice() => _targetList.SelectedItem is TargetDraftState state && _monitorBox.SelectedItem is MonitorChoice choice && MonitorSelectorIdentity.Equals(state.Target.MonitorSelector, choice.Selector) ? state.Target.Mode : null;

    private void MonitorSelectionChanged()
    {
        if (_suppress) return;
        var choice = _monitorBox.SelectedItem as MonitorChoice;
        if (_targetList.SelectedIndex < 0 && choice is not null)
        {
            var match = ExistingTargetSelection.Resolve(_targetList.Items.OfType<TargetDraftState>(), choice.Selector);
            if (match is not null)
            {
                _suppress = true;
                try { _targetList.SelectedItem = match; }
                finally { _suppress = false; }
                EditTarget("Bestehendes Monitorziel ausgewählt — Änderungen können direkt am Ziel oder mit dem Profil gespeichert werden.");
                UpdateState();
                return;
            }
        }

        LoadModes(choice, PreservedModeForChoice());
        UpdateState();
    }

    private void LoadModes(MonitorChoice? choice, DisplayMode? preserved, string? readyMessage = null)
    {
        var version = ++_modeVersion; _modeBox.Enabled = false; _modeBox.Items.Clear();
        UpdateState();
        if (choice is null) { if (preserved is not null) SelectMode(preserved, true); return; }
        SetTargetStatus(PresentationStatusKind.Loading, "Anzeigemodi werden gelesen…");
        Task.Run(() => _targets.GetModes(choice, preserved)).ContinueWith(task => Ui(() =>
        {
            if (version != _modeVersion) return;
            _modeBox.Items.Clear();
            if (!task.IsCompletedSuccessfully) { SetTargetStatus(PresentationStatusKind.Error, task.Exception?.GetBaseException().Message ?? "Die Anzeigemodi konnten nicht gelesen werden."); return; }
            foreach (var mode in task.Result.Modes) _modeBox.Items.Add(mode);
            _modeBox.Enabled = task.Result.Success;
            if (preserved is not null) SelectMode(preserved, true);
            SetTargetStatus(task.Result.Success ? PresentationStatusKind.Ready : PresentationStatusKind.Error, task.Result.Success ? readyMessage ?? "Zielmodus bewusst auswählen; es wird nichts sofort angewendet." : task.Result.Error);
            FitDropDown(_modeBox);
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
        _targetButton.Text = ProfileManagerPresentation.TargetAction(false); _removeTargetButton.Enabled = false; _rebindButton.Enabled = false;
        if (!_topologyLoading) SetTargetStatus(PresentationStatusKind.Neutral, "Monitor wählen; ein bereits vorhandenes Ziel wird automatisch zur Bearbeitung geöffnet.");
        UpdateState();
    }

    private void EditTarget(string? readyMessage = null)
    {
        if (_targetList.SelectedItem is not TargetDraftState state) { _targetButton.Text = ProfileManagerPresentation.TargetAction(false); _removeTargetButton.Enabled = _rebindButton.Enabled = false; return; }
        _targetButton.Text = ProfileManagerPresentation.TargetAction(true); _removeTargetButton.Enabled = true; _rebindButton.Enabled = state.Target.MonitorSelector is SpecificMonitor;
        var choice = _targets.FindChoice(state.Target.MonitorSelector); _suppress = true; _monitorBox.SelectedItem = choice; _suppress = false;
        if (choice is null) { _modeBox.Items.Clear(); SelectMode(state.Target.Mode, true); _modeBox.Enabled = false; SetTargetStatus(PresentationStatusKind.Warning, state.Detail); }
        else LoadModes(choice, state.Target.Mode, readyMessage);
    }

    private void RefreshTargets()
    {
        _targetList.Items.Clear(); foreach (var state in _targets.GetTargetStates()) _targetList.Items.Add(state); FitHorizontalExtent(_targetList); UpdateState();
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
        _editingKey = item.Key; _editingPolicy = item.Profile.Policy; _targets.Load(item.Profile); _saveButton.Text = ProfileManagerPresentation.ProfileSaveAction(true);
        SelectProcess(item.Key); SelectPolicy(item.Profile.Policy); RefreshTargets(); StartNewTarget(); RefreshLaunchType(item); UpdateState();
    }

    private void StartNewProfile()
    {
        _editingKey = null; _editingPolicy = null; _targets.BeginNew(); _saveButton.Text = ProfileManagerPresentation.ProfileSaveAction(false); _profileList.ClearSelected();
        _launchVersion++; _launchTypeLabel.Text = "Optionaler Start per Klick: –"; SelectPolicy(ProfileRetentionPolicy.Startup); RefreshTargets(); StartNewTarget(); UpdateState();
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
        var item = _profileList.SelectedItem as ProfileItem;
        var editorState = ProfileManagerPresentation.EditorState(
            _editingKey is not null,
            dirty,
            _launching,
            item?.Missing == true,
            _targets.HasBlockingResolution);
        _editStateLabel.Text = editorState.Text;
        _editStateLabel.ForeColor = editorState.Kind switch
        {
            ProfileEditorStateKind.Saved => Color.DarkGreen,
            ProfileEditorStateKind.Dirty => Color.DarkOrange,
            ProfileEditorStateKind.Blocked => Color.Firebrick,
            ProfileEditorStateKind.Launching => SystemColors.HotTrack,
            _ => SystemColors.ControlText
        };
        _toolTip.SetToolTip(_editStateLabel, editorState.Text);
        _saveButton.Enabled = _targets.CanSave && ProfileTargetSavePreparation.CanPrepare(
            _targetList.SelectedItem as TargetDraftState,
            _monitorBox.SelectedItem as MonitorChoice,
            _modeBox.SelectedItem as DisplayMode);
        _launchButton.Enabled = !_launching && !dirty && item is not null && !_targets.HasBlockingResolution && item.Profile.Targets.Count > 0 && Path.IsPathFullyQualified(item.Key) && !item.Missing;
    }

    private void RefreshProcesses()
    {
        var version = ++_processVersion; var all = _showAllProcesses.Checked; var requested = _preferredProcessPath ?? (_processBox.SelectedItem as ProcessPresentationItem)?.Path;
        Task.Run(() => ProcessPresentation.CreateItems(_processProvider.GetCurrentSessionProcesses().Select(ProcessInfo).ToList(), Application.ExecutablePath, all).ToList()).ContinueWith(task => Ui(() =>
        {
            if (version != _processVersion || !task.IsCompletedSuccessfully) return; var items = task.Result;
            if (!string.IsNullOrWhiteSpace(requested) && !items.Any(item => string.Equals(item.Path, requested, StringComparison.OrdinalIgnoreCase))) items.Add(ProcessPresentation.CreateProfileItem(requested));
            _processBox.Items.Clear(); foreach (var item in items) _processBox.Items.Add(item); FitDropDown(_processBox); var index = items.FindIndex(item => string.Equals(item.Path, requested, StringComparison.OrdinalIgnoreCase)); _processBox.SelectedIndex = index >= 0 ? index : items.Count > 0 ? 0 : -1;
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
        FitHorizontalExtent(_profileList);
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
        var version = ++_launchVersion; if (!Path.IsPathFullyQualified(item.Key) || item.Missing) { _launchTypeLabel.Text = "Optionaler Start per Klick: –"; return; } _launchTypeLabel.Text = "Optionaler Start per Klick wird vorbereitet…";
        try { var plan = await _profileManager.ResolveLaunchPlanAsync(item.Key); if (!_closing && !IsDisposed && version == _launchVersion) _launchTypeLabel.Text = $"Optionaler Start per Klick: {plan.DisplayName}"; }
        catch { if (!_closing && !IsDisposed && version == _launchVersion) _launchTypeLabel.Text = "Optionaler Start per Klick: Direkt"; }
    }

    private void SelectProcess(string path) { _processVersion++; _preferredProcessPath = path; var item = _processBox.Items.OfType<ProcessPresentationItem>().FirstOrDefault(candidate => string.Equals(candidate.Path, path, StringComparison.OrdinalIgnoreCase)); if (item is null) { item = ProcessPresentation.CreateProfileItem(path); _processBox.Items.Add(item); } _processBox.SelectedItem = item; }
    private void SelectMode(DisplayMode mode, bool unavailable) { var item = _modeBox.Items.OfType<DisplayMode>().FirstOrDefault(candidate => candidate.Equals(mode)); if (item is null) { item = new DisplayMode { Width = mode.Width, Height = mode.Height, Frequency = mode.Frequency, Label = $"{mode.Width}x{mode.Height} @ {mode.Frequency}Hz" + (unavailable ? " (derzeit nicht verfügbar)" : "") }; _modeBox.Items.Add(item); } _modeBox.SelectedItem = item; }
    private void SelectPolicy(ProfileRetentionPolicy policy) => _policyBox.SelectedItem = _policyBox.Items.OfType<PolicyItem>().First(item => item.Policy == policy);
    private void RemoveProfile(object? sender, EventArgs e) { if (_profileList.SelectedItem is not ProfileItem item) return; var result = ProfileEditWorkflow.Remove(_store, item.Key); if (!result.Success) MessageBox.Show(this, result.Error ?? "Das Profil konnte nicht entfernt werden.", "Profile", MessageBoxButtons.OK, MessageBoxIcon.Error); else { RefreshProfiles(); StartNewProfile(); } }
    private void SetTargetStatus(PresentationStatusKind kind, string? message)
    {
        _targetStatus.Text = ProfileManagerPresentation.TargetStatus(kind, message);
        _targetStatus.ForeColor = kind switch
        {
            PresentationStatusKind.Ready => Color.DarkGreen,
            PresentationStatusKind.Warning => Color.DarkOrange,
            PresentationStatusKind.Error => Color.Firebrick,
            PresentationStatusKind.Loading => SystemColors.HotTrack,
            _ => SystemColors.ControlText
        };
        _toolTip.SetToolTip(_targetStatus, _targetStatus.Text);
    }

    private void TargetError(string error)
    {
        SetTargetStatus(PresentationStatusKind.Error, error);
        MessageBox.Show(this, error, "Monitorziele", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _toolTip.Dispose();
        base.Dispose(disposing);
    }

    private void Ui(Action action) { if (_closing || IsDisposed || !IsHandleCreated) return; try { BeginInvoke(new Action(() => { if (!_closing && !IsDisposed) action(); })); } catch (InvalidOperationException) { } }
}
