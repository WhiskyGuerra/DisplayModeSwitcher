using System;
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

        private class ProfileListItem
        {
            public string Process { get; set; } = string.Empty;
            public DisplayMode Mode { get; set; } = new DisplayMode();
            public override string ToString() => $"{Process} → {Mode.Width}x{Mode.Height}@{Mode.Frequency}Hz";
        }

        private class ProcessItem
        {
            public string Name { get; set; } = string.Empty;
            public int Id { get; set; }
            public string Path { get; set; } = string.Empty;
            public override string ToString() => $"{Name} ({Id}) - {Path}";
        }

        public ProfileManagerForm(ProfileStore store, IProcessProvider? processProvider = null)
        {
            _store = store;
            _processProvider = processProvider ?? new SystemProcessProvider();

            Text = "Profile verwalten";
            Width = 400;
            Height = 300;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            _processBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 10, Top = 10, Width = 150 };
            _modeBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 170, Top = 10, Width = 200 };
            _addButton = new Button { Text = "Speichern", Left = 10, Top = 40, Width = 100 };
            _removeButton = new Button { Text = "Entfernen", Left = 120, Top = 40, Width = 100 };
            _profileList = new ListBox { Left = 10, Top = 80, Width = 360, Height = 170 };

            Controls.AddRange(new Control[] { _processBox, _modeBox, _addButton, _removeButton, _profileList });

            Load += ProfileManagerForm_Load;
            _addButton.Click += OnAdd;
            _removeButton.Click += OnRemove;
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
            Task.Run(() =>
            {
                var list = _processProvider.GetCurrentSessionProcesses()
                    .Where(process => !process.ExecutablePath.Contains("System32", StringComparison.OrdinalIgnoreCase))
                    .Select(process => new ProcessItem
                    {
                        Name = System.IO.Path.GetFileName(process.ExecutablePath),
                        Id = process.Id,
                        Path = process.ExecutablePath
                    })
                    .OrderBy(p => p.Name)
                    .ToList();

                if (IsDisposed || !IsHandleCreated)
                    return;
                BeginInvoke(new Action(() =>
                {
                    foreach (var item in list)
                        _processBox.Items.Add(item);
                    if (_processBox.Items.Count > 0)
                        _processBox.SelectedIndex = 0;
                }));
            });
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
                _profileList.Items.Add(new ProfileListItem { Process = kvp.Key, Mode = kvp.Value });
            }
        }

        private void OnAdd(object? sender, EventArgs e)
        {
            if (_processBox.SelectedItem is not ProcessItem processItem ||
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
            if (_store.Profiles.TryRemove(item.Process, out _))
            {
                var result = _store.Save();
                if (!result.Success)
                {
                    _store.Profiles[item.Process] = item.Mode;
                    MessageBox.Show(result.Error ?? "Das Profil konnte nicht entfernt werden.", "Profile", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                RefreshProfileList();
            }
        }
    }
}
