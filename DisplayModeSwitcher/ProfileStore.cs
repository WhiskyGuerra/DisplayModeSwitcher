using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DisplayModeSwitcher;

public sealed class ProfileStore
{
    private const int CurrentVersion = 2;
    private readonly string _filePath;
    private readonly string? _legacyFilePath;
    private readonly object _lock = new();
    private bool _saveBlockedByLoadError;

    private sealed class ProfileFileV2
    {
        public int Version { get; set; }
        public List<ProfileRecordV2?>? Profiles { get; set; }
    }

    private sealed class ProfileRecordV2
    {
        public string? Process { get; set; }
        public string? Policy { get; set; }
        public List<TargetRecordV2?>? Targets { get; set; }
    }

    private sealed class TargetRecordV2
    {
        public SelectorRecordV2? MonitorSelector { get; set; }
        public ModeRecordV2? Mode { get; set; }
    }

    private sealed class SelectorRecordV2
    {
        public string? Kind { get; set; }
        public string? MonitorDevicePath { get; set; }
        public string? FriendlyNameSnapshot { get; set; }
        public string? EdidManufacturerId { get; set; }
        public string? EdidProductCodeId { get; set; }
    }

    private sealed class ModeRecordV2
    {
        public uint Width { get; set; }
        public uint Height { get; set; }
        public uint Frequency { get; set; }
    }

    private sealed class LegacyProfileRecord
    {
        private string? _policy;

        public string? Process { get; set; }
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

                var loaded = ReadAndValidate(_filePath, out var requiresV2Migration);
                if (requiresV2Migration)
                    WriteProfiles(loaded);
                ReplaceProfiles(loaded);
                LastError = null;
                _saveBlockedByLoadError = false;
                return OperationResult.Ok();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or InvalidOperationException)
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or InvalidOperationException or ArgumentException)
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
            var loaded = ReadAndValidate(legacyFilePath, out _);
            WriteProfiles(loaded);
            ReplaceProfiles(loaded);
            _saveBlockedByLoadError = false;
            LastError = null;
            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            LastError = $"Alte Profile konnten nicht übernommen werden: {ex.Message}";
            _saveBlockedByLoadError = true;
            return OperationResult.Fail(LastError);
        }
    }

    private static Dictionary<string, DisplayProfile> ReadAndValidate(string path, out bool requiresV2Migration)
    {
        var json = File.ReadAllText(path);
        using var document = JsonDocument.Parse(json);
        switch (document.RootElement.ValueKind)
        {
            case JsonValueKind.Array:
                requiresV2Migration = true;
                return ReadLegacy(json);
            case JsonValueKind.Object:
                requiresV2Migration = false;
                return ReadV2(json);
            default:
                throw new InvalidDataException("Die Profildatei besitzt kein unterstütztes Format.");
        }
    }

    private static Dictionary<string, DisplayProfile> ReadLegacy(string json)
    {
        var records = JsonSerializer.Deserialize<List<LegacyProfileRecord?>>(json)
            ?? throw new InvalidDataException("Die Profildatei enthält keine Profilliste.");
        var result = new Dictionary<string, DisplayProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            if (record is null)
                throw new InvalidDataException("Die Profildatei enthält einen leeren Profileintrag.");
            var process = ValidateProcess(record.Process);
            var policy = ParseLegacyPolicy(record);
            var mode = CreateMode(record.Width, record.Height, record.Frequency, process);
            var profile = new DisplayProfile(policy,
                [new DisplayProfileTarget(new PrimaryMonitor(), mode)]);
            ValidateProfile(process, profile);
            if (!result.TryAdd(process, profile))
                throw new InvalidDataException($"Das Profil '{process}' ist doppelt vorhanden.");
        }
        return result;
    }

    private static Dictionary<string, DisplayProfile> ReadV2(string json)
    {
        var file = JsonSerializer.Deserialize<ProfileFileV2>(json)
            ?? throw new InvalidDataException("Die Profildatei enthält keine Profildaten.");
        if (file.Version != CurrentVersion)
            throw new InvalidDataException($"Die Profilversion '{file.Version}' wird nicht unterstützt.");
        if (file.Profiles is null)
            throw new InvalidDataException("Die Profildatei enthält keine Profilliste.");

        var result = new Dictionary<string, DisplayProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in file.Profiles)
        {
            if (record is null)
                throw new InvalidDataException("Die Profildatei enthält einen leeren Profileintrag.");
            var process = ValidateProcess(record.Process);
            var policy = ParsePolicy(record.Policy, process);
            if (record.Targets is null || record.Targets.Count == 0)
                throw new InvalidDataException($"Das Profil '{process}' besitzt kein Monitorziel.");

            var targets = record.Targets.Select(target => ParseTarget(
                target ?? throw new InvalidDataException($"Das Profil '{process}' besitzt ein leeres Monitorziel."), process)).ToArray();
            var profile = new DisplayProfile(policy, targets);
            ValidateProfile(process, profile);
            if (!result.TryAdd(process, profile))
                throw new InvalidDataException($"Das Profil '{process}' ist doppelt vorhanden.");
        }
        return result;
    }

    private static DisplayProfileTarget ParseTarget(TargetRecordV2 record, string process)
    {
        if (record.MonitorSelector is null)
            throw new InvalidDataException($"Das Profil '{process}' besitzt ein Ziel ohne Monitorwahl.");
        if (record.Mode is null)
            throw new InvalidDataException($"Das Profil '{process}' besitzt ein Ziel ohne Anzeigemodus.");

        MonitorSelector selector = record.MonitorSelector.Kind switch
        {
            nameof(PrimaryMonitor) => new PrimaryMonitor(),
            nameof(SpecificMonitor) => new SpecificMonitor(
                record.MonitorSelector.MonitorDevicePath ?? string.Empty,
                record.MonitorSelector.FriendlyNameSnapshot ?? string.Empty,
                record.MonitorSelector.EdidManufacturerId,
                record.MonitorSelector.EdidProductCodeId),
            null or "" => throw new InvalidDataException($"Das Profil '{process}' besitzt keine gültige Monitorwahl."),
            _ => throw new InvalidDataException($"Das Profil '{process}' besitzt eine unbekannte Monitorwahl '{record.MonitorSelector.Kind}'.")
        };
        return new DisplayProfileTarget(selector,
            CreateMode(record.Mode.Width, record.Mode.Height, record.Mode.Frequency, process));
    }

    private static string ValidateProcess(string? process)
    {
        if (string.IsNullOrWhiteSpace(process))
            throw new InvalidDataException("Ein Profil besitzt keinen Prozess.");
        return process;
    }

    private static DisplayMode CreateMode(uint width, uint height, uint frequency, string process)
    {
        if (width == 0 || height == 0 || frequency == 0)
            throw new InvalidDataException($"Das Profil '{process}' besitzt einen ungültigen Anzeigemodus.");
        return new DisplayMode
        {
            Width = width,
            Height = height,
            Frequency = frequency,
            Label = $"{width}x{height} @ {frequency}Hz"
        };
    }

    private static ProfileRetentionPolicy ParseLegacyPolicy(LegacyProfileRecord record)
    {
        if (!record.PolicySpecified)
            return ProfileRetentionPolicy.Startup;
        return ParsePolicy(record.Policy, record.Process ?? string.Empty);
    }

    private static ProfileRetentionPolicy ParsePolicy(string? value, string process) => value switch
    {
        nameof(ProfileRetentionPolicy.Once) => ProfileRetentionPolicy.Once,
        nameof(ProfileRetentionPolicy.Startup) => ProfileRetentionPolicy.Startup,
        nameof(ProfileRetentionPolicy.Continuous) => ProfileRetentionPolicy.Continuous,
        null or "" => throw new InvalidDataException($"Das Profil '{process}' besitzt keine gültige Richtlinie."),
        _ => throw new InvalidDataException($"Das Profil '{process}' besitzt eine unbekannte Richtlinie '{value}'.")
    };

    private static void ValidateProfile(string process, DisplayProfile profile)
    {
        if (!Enum.IsDefined(profile.Policy))
            throw new InvalidDataException($"Das Profil '{process}' besitzt eine unbekannte Richtlinie '{profile.Policy}'.");
        if (profile.Targets.Count == 0)
            throw new InvalidDataException($"Das Profil '{process}' besitzt kein Monitorziel.");

        var selectors = new List<MonitorSelector>();
        foreach (var target in profile.Targets)
        {
            if (target is null || target.MonitorSelector is null || target.Mode is null)
                throw new InvalidDataException($"Das Profil '{process}' besitzt ein unvollständiges Monitorziel.");
            _ = CreateMode(target.Mode.Width, target.Mode.Height, target.Mode.Frequency, process);
            ValidateSelector(target.MonitorSelector, process);
            if (selectors.Any(existing => MonitorSelectorIdentity.Equals(existing, target.MonitorSelector)))
                throw new InvalidDataException($"Das Profil '{process}' besitzt eine Monitorwahl doppelt.");
            selectors.Add(target.MonitorSelector);
        }
    }

    private static void ValidateSelector(MonitorSelector selector, string process)
    {
        switch (selector)
        {
            case PrimaryMonitor:
                return;
            case SpecificMonitor specific:
                if (string.IsNullOrWhiteSpace(specific.MonitorDevicePath) ||
                    !specific.MonitorDevicePath.StartsWith(@"\\?\DISPLAY#", StringComparison.OrdinalIgnoreCase) ||
                    !IsCleanText(specific.MonitorDevicePath))
                    throw new InvalidDataException($"Das Profil '{process}' besitzt keinen gültigen Monitor-Gerätepfad.");
                if (string.IsNullOrWhiteSpace(specific.FriendlyNameSnapshot) || !IsCleanText(specific.FriendlyNameSnapshot))
                    throw new InvalidDataException($"Das Profil '{process}' besitzt keinen sinnvollen Monitor-Anzeigenamen.");
                if (specific.EdidManufacturerId is not null &&
                        (string.IsNullOrWhiteSpace(specific.EdidManufacturerId) || !IsCleanText(specific.EdidManufacturerId)) ||
                    specific.EdidProductCodeId is not null &&
                        (string.IsNullOrWhiteSpace(specific.EdidProductCodeId) || !IsCleanText(specific.EdidProductCodeId)))
                    throw new InvalidDataException($"Das Profil '{process}' besitzt ungültige EDID-Hinweise.");
                return;
            default:
                throw new InvalidDataException($"Das Profil '{process}' besitzt eine unbekannte Monitorwahl.");
        }
    }

    private static bool IsCleanText(string value) =>
        string.Equals(value, value.Trim(), StringComparison.Ordinal) &&
        !value.Any(char.IsControl);

    private static ProfileFileV2 ToFile(IEnumerable<KeyValuePair<string, DisplayProfile>> profiles)
    {
        var records = new List<ProfileRecordV2?>();
        foreach (var item in profiles.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            ValidateProcess(item.Key);
            if (item.Value is null)
                throw new InvalidDataException($"Das Profil '{item.Key}' enthält keine Profildaten.");
            ValidateProfile(item.Key, item.Value);
            records.Add(new ProfileRecordV2
            {
                Process = item.Key,
                Policy = item.Value.Policy.ToString(),
                Targets = item.Value.Targets.Select(target => (TargetRecordV2?)ToRecord(target)).ToList()
            });
        }
        return new ProfileFileV2 { Version = CurrentVersion, Profiles = records };
    }

    private static TargetRecordV2 ToRecord(DisplayProfileTarget target) => new()
    {
        MonitorSelector = target.MonitorSelector switch
        {
            PrimaryMonitor => new SelectorRecordV2 { Kind = nameof(PrimaryMonitor) },
            SpecificMonitor specific => new SelectorRecordV2
            {
                Kind = nameof(SpecificMonitor),
                MonitorDevicePath = specific.MonitorDevicePath,
                FriendlyNameSnapshot = specific.FriendlyNameSnapshot,
                EdidManufacturerId = specific.EdidManufacturerId,
                EdidProductCodeId = specific.EdidProductCodeId
            },
            _ => throw new InvalidDataException("Ein Profil besitzt eine unbekannte Monitorwahl.")
        },
        Mode = new ModeRecordV2
        {
            Width = target.Mode.Width,
            Height = target.Mode.Height,
            Frequency = target.Mode.Frequency
        }
    };

    private void WriteProfiles(IEnumerable<KeyValuePair<string, DisplayProfile>> profiles)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Der Profilpfad besitzt keinen gültigen Zielordner.");

        var file = ToFile(profiles);
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var json = JsonSerializer.Serialize(file, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
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
            Profiles[item.Key] = item.Value.DeepCopy();
    }
}
