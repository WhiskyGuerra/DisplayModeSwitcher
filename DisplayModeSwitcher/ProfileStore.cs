using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DisplayModeSwitcher
{
    public class ProfileStore
    {
        private readonly string _filePath;
        private readonly object _lock = new();

        private class ProfileRecord
        {
            public string Process { get; set; } = string.Empty;
            public uint Width { get; set; }
            public uint Height { get; set; }
            public uint Frequency { get; set; }
        }

        public ConcurrentDictionary<string, DisplayMode> Profiles { get; }

        public ProfileStore(string filePath)
        {
            _filePath = filePath;
            Profiles = new ConcurrentDictionary<string, DisplayMode>(StringComparer.OrdinalIgnoreCase);
            Load();
        }

        public void Load()
        {
            lock (_lock)
            {
                Profiles.Clear();
                if (!File.Exists(_filePath))
                    return;

                string json = File.ReadAllText(_filePath);
                var records = JsonSerializer.Deserialize<List<ProfileRecord>>(json) ?? new List<ProfileRecord>();
                foreach (var r in records)
                {
                    var mode = new DisplayMode
                    {
                        Width = r.Width,
                        Height = r.Height,
                        Frequency = r.Frequency,
                        Label = $"{r.Width}x{r.Height} @ {r.Frequency}Hz"
                    };
                    Profiles[r.Process] = mode;
                }
            }
        }

        public void Save()
        {
            lock (_lock)
            {
                var records = Profiles.Select(kvp => new ProfileRecord
                {
                    Process = kvp.Key,
                    Width = kvp.Value.Width,
                    Height = kvp.Value.Height,
                    Frequency = kvp.Value.Frequency
                }).ToList();

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(records, options);
                File.WriteAllText(_filePath, json);
            }
        }
    }
}
