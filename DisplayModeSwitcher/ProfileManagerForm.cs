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
        private readonly ComboBox _processBox;
        private readonly ComboBox _modeBox;
        private readonly Button _addButton;
        private readonly Button _removeButton;
        private readonly ListBox _profileList;
        private readonly IProcessProvider _processProvider;
        private readonly CheckBox _showAllProcesses;
        private int _processLoadVersion;
        private bool _isClosing;

        private sealed class ProfileListItem
        {
            public string ProcessKey { get; set; } = string.Empty;
            public ProcessPresentationItem Presentation { get; set; } = new(string.Empty, string.Empty, string.Empty);
            public DisplayMode Mode { get; set; } = new DisplayMode();
            public override string ToString() => $"{Presentation} → {Mode.Width}x{Mode.Height}@{Mode.Frequency}Hz";
        }

        public ProfileManagerForm(ProfileStore store, IProcessProvider? processProvider = null)
        {
            _store = store;
            _processProvider = processProvider ?? new SystemProcessProvider();

            Text = "Profile verwalten";
            Width = 660;
            Height = 340;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            _processBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 10, Top = 10, Width = 360 };
            _modeBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 380, Top = 10, Width = 250 };
            _showAllProcesses = new CheckBox { Text = "Alle Prozesse anzeigen", Left = 10, Top = 43, AutoSize = true };
            _addButton = new Button { Text = "Speichern", Left = 10, Top = 70, Width = 100 };
            _removeButton = new Button { Text = "Entfernen", Left = 120, Top = 70, Width = 100 };
            _profileList = new ListBox { Left = 10, Top = 110, Width = 620, Height = 180, HorizontalScrollbar = true };

            Controls.AddRange(new Control[] { _processBox, _modeBox, _showAllProcesses, _addButton, _removeButton, _profileList });

            Load += ProfileManagerForm_Load;
            _addButton.Click += OnAdd;
            _removeButton.Click += OnRemove;
            _showAllProcesses.CheckedChanged += (_, _) => RefreshProcesses();
            FormClosing += (_, _) => { _isClosing = true; _processLoadVersion++; };
        }

        private void ProfileManagerForm_Load(object? sender, EventArgs e)
        {
            RefreshProcesses();
            RefreshModes();
            RefreshProfileList();
        }

        private void RefreshProcesses()
        {
            _processBox.Items.Clear();
            var loadVersion = ++_processLoadVersion;
            var includeBackgroundProcesses = _showAllProcesses.Checked;
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
                    foreach (var item in list)
                        _processBox.Items.Add(item);
                    if (_processBox.Items.Count > 0)
                        _processBox.SelectedIndex = 0;
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
            _profileList.Items.Clear();
            foreach (var kvp in _store.Profiles)
            {
                _profileList.Items.Add(new ProfileListItem { ProcessKey = kvp.Key, Presentation = ProcessPresentation.CreateProfileItem(kvp.Key), Mode = kvp.Value });
            }
        }

        private void OnAdd(object? sender, EventArgs e)
        {
            if (_processBox.SelectedItem is not ProcessPresentationItem processItem ||
                _modeBox.SelectedItem is not DisplayMode mode)
                return;
            var process = processItem.Path;
            var hadPrevious = _store.Profiles.TryGetValue(process, out var previous);
            _store.Profiles[process] = mode;
            var result = _store.Save();
            if (!result.Success)
            {
                if (hadPrevious && previous is not null)
                    _store.Profiles[process] = previous;
                else
                    _store.Profiles.TryRemove(process, out _);
                MessageBox.Show(result.Error ?? "Das Profil konnte nicht gespeichert werden.", "Profile", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            RefreshProfileList();
        }

        private void OnRemove(object? sender, EventArgs e)
        {
            if (_profileList.SelectedItem is not ProfileListItem item)
                return;
            if (_store.Profiles.TryRemove(item.ProcessKey, out _))
            {
                var result = _store.Save();
                if (!result.Success)
                {
                    _store.Profiles[item.ProcessKey] = item.Mode;
                    MessageBox.Show(result.Error ?? "Das Profil konnte nicht entfernt werden.", "Profile", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                RefreshProfileList();
            }
        }
    }
}
