using System.Collections.Concurrent;
using System.Text.Json;

namespace DisplayModeSwitcher;

public sealed class ProfileStore
{
    private readonly string _filePath;
    private readonly string? _legacyFilePath;
    private readonly object _lock = new();
    private bool _saveBlockedByLoadError;

    private sealed class ProfileRecord
    {
        public string Process { get; set; } = string.Empty;
        public uint Width { get; set; }
        public uint Height { get; set; }
        public uint Frequency { get; set; }
    }

    public ConcurrentDictionary<string, DisplayMode> Profiles { get; } = new(StringComparer.OrdinalIgnoreCase);
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

            string? temporaryPath = null;
            try
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (string.IsNullOrWhiteSpace(directory))
                    throw new InvalidOperationException("Der Profilpfad besitzt keinen gültigen Zielordner.");

                Directory.CreateDirectory(directory);
                temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");
                var records = Profiles.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase).Select(ToRecord).ToList();
                Validate(records);
                var json = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(temporaryPath, json);
                File.Move(temporaryPath, _filePath, true);
                LastError = null;
                return OperationResult.Ok();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or InvalidOperationException)
            {
                if (temporaryPath is not null)
                {
                    try { File.Delete(temporaryPath); }
                    catch { }
                }
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
            ReplaceProfiles(loaded);
            _saveBlockedByLoadError = false;
            var saveResult = Save();
            if (!saveResult.Success)
                return saveResult;

            LastError = null;
            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            LastError = $"Alte Profile konnten nicht übernommen werden: {ex.Message}";
            _saveBlockedByLoadError = true;
            return OperationResult.Fail(LastError);
        }
    }

    private static Dictionary<string, DisplayMode> ReadAndValidate(string path)
    {
        var json = File.ReadAllText(path);
        var records = JsonSerializer.Deserialize<List<ProfileRecord>>(json)
            ?? throw new InvalidDataException("Die Profildatei enthält keine Profilliste.");
        Validate(records);

        var result = new Dictionary<string, DisplayMode>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            if (!result.TryAdd(record.Process, new DisplayMode
                {
                    Width = record.Width,
                    Height = record.Height,
                    Frequency = record.Frequency,
                    Label = $"{record.Width}x{record.Height} @ {record.Frequency}Hz"
                }))
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
        }
    }

    private static ProfileRecord ToRecord(KeyValuePair<string, DisplayMode> item) => new()
    {
        Process = item.Key,
        Width = item.Value.Width,
        Height = item.Value.Height,
        Frequency = item.Value.Frequency
    };

    private void ReplaceProfiles(IReadOnlyDictionary<string, DisplayMode> loaded)
    {
        Profiles.Clear();
        foreach (var item in loaded)
            Profiles[item.Key] = item.Value;
    }
}
