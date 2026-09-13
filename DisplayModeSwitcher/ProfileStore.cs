using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DisplayModeSwitcher;

public sealed class ProfileStore
{
    private readonly string _filePath;
    private readonly string? _legacyFilePath;
    private readonly object _lock = new();
    private bool _saveBlockedByLoadError;

    private sealed class ProfileRecord
    {
        private string? _policy;

        public string Process { get; set; } = string.Empty;
        public uint Width { get; set; }
        public uint Height { get; set; }
        public uint Frequency { get; set; }
        public string? Policy
        {
            get => _policy;
            set
            {
                _policy = value;
                PolicySpecified = true;
            }
        }

        [JsonIgnore]
        public bool PolicySpecified { get; private set; }
    }

    public ConcurrentDictionary<string, DisplayProfile> Profiles { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? LastError { get; private set; }
    public string FilePath => _filePath;

    public ProfileStore(string filePath, string? legacyFilePath = null)
    {
        _filePath = filePath;
        _legacyFilePath = legacyFilePath;
        Load();
    }

    public OperationResult Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    if (!string.IsNullOrWhiteSpace(_legacyFilePath) && File.Exists(_legacyFilePath))
                        return MigrateLegacy(_legacyFilePath);

                    Profiles.Clear();
                    LastError = null;
                    _saveBlockedByLoadError = false;
                    return OperationResult.Ok();
                }

                var loaded = ReadAndValidate(_filePath);
                ReplaceProfiles(loaded);
                LastError = null;
                _saveBlockedByLoadError = false;
                return OperationResult.Ok();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                LastError = $"Profile konnten nicht geladen werden: {ex.Message}";
                _saveBlockedByLoadError = true;
                return OperationResult.Fail(LastError);
            }
        }
    }

    public OperationResult Save()
    {
        lock (_lock)
        {
            if (_saveBlockedByLoadError)
                return OperationResult.Fail("Die Profildatei wurde wegen eines Ladefehlers nicht überschrieben. Bitte die beschädigte Datei zuerst sichern oder korrigieren.");

            try
            {
                WriteProfiles(Profiles);
                LastError = null;
                return OperationResult.Ok();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or InvalidOperationException)
            {
                LastError = $"Profile konnten nicht gespeichert werden: {ex.Message}";
                return OperationResult.Fail(LastError);
            }
        }
    }

    private OperationResult MigrateLegacy(string legacyFilePath)
    {
        try
        {
            var loaded = ReadAndValidate(legacyFilePath);
            WriteProfiles(loaded);
            ReplaceProfiles(loaded);
            _saveBlockedByLoadError = false;
            LastError = null;
            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or InvalidOperationException)
        {
            LastError = $"Alte Profile konnten nicht übernommen werden: {ex.Message}";
            _saveBlockedByLoadError = true;
            return OperationResult.Fail(LastError);
        }
    }

    private static Dictionary<string, DisplayProfile> ReadAndValidate(string path)
    {
        var json = File.ReadAllText(path);
        var records = JsonSerializer.Deserialize<List<ProfileRecord>>(json)
            ?? throw new InvalidDataException("Die Profildatei enthält keine Profilliste.");
        Validate(records);

        var result = new Dictionary<string, DisplayProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            var policy = ParsePolicy(record);
            if (!result.TryAdd(record.Process, new DisplayProfile(new DisplayMode
                {
                    Width = record.Width,
                    Height = record.Height,
                    Frequency = record.Frequency,
                    Label = $"{record.Width}x{record.Height} @ {record.Frequency}Hz"
                }, policy)))
                throw new InvalidDataException($"Das Profil '{record.Process}' ist doppelt vorhanden.");
        }
        return result;
    }

    private static void Validate(IEnumerable<ProfileRecord> records)
    {
        foreach (var record in records)
        {
            if (string.IsNullOrWhiteSpace(record.Process))
                throw new InvalidDataException("Ein Profil besitzt keinen Prozess.");
            if (record.Width == 0 || record.Height == 0 || record.Frequency == 0)
                throw new InvalidDataException($"Das Profil '{record.Process}' besitzt einen ungültigen Anzeigemodus.");
            _ = ParsePolicy(record);
        }
    }

    private static ProfileRetentionPolicy ParsePolicy(ProfileRecord record)
    {
        if (!record.PolicySpecified)
            return ProfileRetentionPolicy.Startup;
        if (record.Policy is null)
            throw new InvalidDataException($"Das Profil '{record.Process}' besitzt keine gültige Richtlinie.");
        return record.Policy switch
        {
            nameof(ProfileRetentionPolicy.Once) => ProfileRetentionPolicy.Once,
            nameof(ProfileRetentionPolicy.Startup) => ProfileRetentionPolicy.Startup,
            nameof(ProfileRetentionPolicy.Continuous) => ProfileRetentionPolicy.Continuous,
            _ => throw new InvalidDataException($"Das Profil '{record.Process}' besitzt eine unbekannte Richtlinie '{record.Policy}'.")
        };
    }

    private static ProfileRecord ToRecord(KeyValuePair<string, DisplayProfile> item) => new()
    {
        Process = item.Key,
        Width = item.Value.Mode.Width,
        Height = item.Value.Mode.Height,
        Frequency = item.Value.Mode.Frequency,
        Policy = item.Value.Policy.ToString()
    };

    private void WriteProfiles(IEnumerable<KeyValuePair<string, DisplayProfile>> profiles)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Der Profilpfad besitzt keinen gültigen Zielordner.");

        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var records = profiles.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase).Select(ToRecord).ToList();
            Validate(records);
            var json = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _filePath, true);
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch { }
        }
    }

    private void ReplaceProfiles(IReadOnlyDictionary<string, DisplayProfile> loaded)
    {
        Profiles.Clear();
        foreach (var item in loaded)
            Profiles[item.Key] = item.Value;
    }
}
