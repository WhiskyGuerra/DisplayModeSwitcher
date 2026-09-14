using System.Drawing;
using System.Windows.Forms;

namespace DisplayModeSwitcher;

public sealed class ManualDisplayForm : Form
{
    private sealed record MonitorChoice(ManualMonitorMenu Monitor)
    {
        public string Text => Monitor.Enabled ? Monitor.Label : $"{Monitor.Label} — nicht verfügbar";
    }
    private sealed record FrequencyChoice(DisplayMode Mode)
    {
        public string Text => $"{Mode.Frequency} Hz";
    }

    private readonly ManualDisplaySwitcher _switcher;
    private readonly ManualDisplaySelection _selection = new();
    private readonly ComboBox _monitorBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _resolutionBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _frequencyBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Label _currentModeLabel = new() { AutoSize = true, Text = "—", Anchor = AnchorStyles.Left };
    private readonly Label _messageLabel = new() { AutoSize = true, Dock = DockStyle.Fill, MaximumSize = new Size(520, 0) };
    private readonly Button _applyButton = new() { Text = "Anwenden", AutoSize = true };
    private readonly Button _refreshButton = new() { Text = "Aktualisieren", AutoSize = true };
    private readonly ToolTip _toolTip = new();
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 500 };
    private bool _updatingControls;

    public ManualDisplayForm(ManualDisplaySwitcher switcher)
    {
        _switcher = switcher ?? throw new ArgumentNullException(nameof(switcher));
        Text = "Anzeigemodus manuell";
        Icon = ApplicationIconProvider.Create();
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(560, 275);
        MinimumSize = new Size(500, 300);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 7
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        AddRow(layout, 0, "Monitor:", _monitorBox);
        AddRow(layout, 1, "Aktueller Modus:", _currentModeLabel);
        AddRow(layout, 2, "Auflösung:", _resolutionBox);
        AddRow(layout, 3, "Bildwiederholrate:", _frequencyBox);
        layout.Controls.Add(_messageLabel, 0, 4);
        layout.SetColumnSpan(_messageLabel, 2);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var closeButton = new Button { Text = "Schließen", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(closeButton);
        buttons.Controls.Add(_applyButton);
        buttons.Controls.Add(_refreshButton);
        layout.Controls.Add(buttons, 0, 6);
        layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout);
        CancelButton = closeButton;
        AcceptButton = _applyButton;

        _monitorBox.SelectedIndexChanged += (_, _) => MonitorChanged();
        _resolutionBox.SelectedIndexChanged += (_, _) => ResolutionChanged();
        _frequencyBox.SelectedIndexChanged += (_, _) => FrequencyChanged();
        _refreshButton.Click += (_, _) => RefreshData(preserveSelection: true);
        _applyButton.Click += (_, _) => ApplySelection();
        _statusTimer.Tick += (_, _) => RefreshProfileStatus();
        Shown += (_, _) =>
        {
            RefreshData(preserveSelection: false);
            _statusTimer.Start();
        };
    }

    private static void AddRow(TableLayoutPanel layout, int row, string caption, Control control)
    {
        layout.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 12, 3) }, 0, row);
        control.Margin = new Padding(3, 3, 3, 5);
        layout.Controls.Add(control, 1, row);
    }

    private void RefreshData(bool preserveSelection)
    {
        var previousPath = preserveSelection ? _selection.SelectedMonitor?.MonitorDevicePath : null;
        var previousMode = preserveSelection ? _selection.SelectedMode : null;
        var result = _switcher.RefreshMenu();
        _selection.Load(result, previousPath, previousMode);
        PopulateControls();
    }

    private void PopulateControls()
    {
        _updatingControls = true;
        try
        {
            var monitorChoices = _selection.Monitors.Select(item => new MonitorChoice(item)).ToArray();
            _monitorBox.DataSource = monitorChoices;
            _monitorBox.DisplayMember = nameof(MonitorChoice.Text);
            _monitorBox.SelectedIndex = _selection.SelectedMonitor is null
                ? -1
                : Array.FindIndex(monitorChoices, item => ReferenceEquals(item.Monitor, _selection.SelectedMonitor));
            SetDropDownWidth(_monitorBox, monitorChoices.Select(item => item.Text));
            PopulateResolutionControls();
        }
        finally { _updatingControls = false; }
        UpdateUiState();
    }

    private void PopulateResolutionControls()
    {
        _resolutionBox.DataSource = _selection.Resolutions.ToArray();
        _resolutionBox.DisplayMember = nameof(ManualResolution.Label);
        _resolutionBox.SelectedIndex = _selection.SelectedResolution is null || _selection.SelectedMonitor is null
            ? -1
            : _selection.Resolutions.ToList().FindIndex(item => item == _selection.SelectedResolution);
        PopulateFrequencyControls();
    }

    private void PopulateFrequencyControls()
    {
        _frequencyBox.DataSource = _selection.Frequencies.Select(item => new FrequencyChoice(item)).ToArray();
        _frequencyBox.DisplayMember = nameof(FrequencyChoice.Text);
        _frequencyBox.SelectedIndex = _selection.SelectedMode is null
            ? -1
            : _selection.Frequencies.ToList().FindIndex(item => item.Frequency == _selection.SelectedMode.Frequency);
    }

    private void MonitorChanged()
    {
        if (_updatingControls) return;
        _selection.SelectMonitor(_monitorBox.SelectedIndex);
        _updatingControls = true;
        try { PopulateResolutionControls(); }
        finally { _updatingControls = false; }
        UpdateUiState();
    }

    private void ResolutionChanged()
    {
        if (_updatingControls) return;
        _selection.SelectResolution(_resolutionBox.SelectedIndex);
        _updatingControls = true;
        try { PopulateFrequencyControls(); }
        finally { _updatingControls = false; }
        UpdateUiState();
    }

    private void FrequencyChanged()
    {
        if (_updatingControls) return;
        _selection.SelectFrequency(_frequencyBox.SelectedIndex);
        UpdateUiState();
    }

    private void RefreshProfileStatus()
    {
        _selection.UpdateProfileBlockReason(_switcher.CurrentProfileBlockReason());
        UpdateUiState();
    }

    private void UpdateUiState()
    {
        var monitor = _selection.SelectedMonitor;
        _currentModeLabel.Text = monitor?.CurrentMode?.Label ?? "—";
        _resolutionBox.Enabled = monitor is { Enabled: true };
        _frequencyBox.Enabled = _selection.Resolutions.Count > 0 && _resolutionBox.SelectedIndex >= 0;
        _applyButton.Enabled = _selection.CanApply;
        _messageLabel.Text = _selection.ApplyBlockReason ?? "Bereit. Es wird ausschließlich der gewählte Monitor umgeschaltet.";
        _messageLabel.ForeColor = _selection.CanApply ? SystemColors.ControlText : Color.DarkOrange;
        _toolTip.SetToolTip(_monitorBox, monitor?.DisabledReason ?? monitor?.Label ?? "Monitor bewusst auswählen.");
    }

    private void ApplySelection()
    {
        var monitor = _selection.SelectedMonitor;
        var mode = _selection.SelectedMode;
        if (!_selection.CanApply || monitor?.MonitorDevicePath is null || mode is null)
        {
            UpdateUiState();
            return;
        }

        var result = _switcher.Switch(monitor.MonitorDevicePath, mode, monitor.Label);
        if (!result.Success)
        {
            MessageBox.Show(this, result.Error ?? "Der Anzeigemodus konnte nicht geändert werden.", "Anzeigemodus", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            RefreshData(preserveSelection: true);
            return;
        }

        RefreshData(preserveSelection: true);
    }

    private static void SetDropDownWidth(ComboBox box, IEnumerable<string> labels)
    {
        var measured = labels.Select(label => TextRenderer.MeasureText(label, box.Font).Width + SystemInformation.VerticalScrollBarWidth + 24).DefaultIfEmpty(box.Width).Max();
        box.DropDownWidth = Math.Min(Math.Max(box.Width, measured), 900);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _statusTimer.Stop();
            _statusTimer.Dispose();
            _toolTip.Dispose();
        }
        base.Dispose(disposing);
    }
}
