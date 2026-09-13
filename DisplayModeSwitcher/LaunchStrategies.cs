using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace DisplayModeSwitcher;

public enum LaunchStrategy
{
    Direct,
    Steam
}

public sealed record LaunchPlan(
    LaunchStrategy Strategy,
    string ExpectedExecutablePath,
    string? SteamAppId = null,
    string? SteamUri = null)
{
    public string DisplayName => Strategy == LaunchStrategy.Steam ? "Steam" : "Direkt";

    public static LaunchPlan Direct(string executablePath) =>
        new(LaunchStrategy.Direct, executablePath);

    public static LaunchPlan Steam(string executablePath, string appId) =>
        new(LaunchStrategy.Steam, executablePath, appId, SteamLaunchInfo.CreateUri(appId));
}

public interface ILaunchPlanResolver
{
    LaunchPlan Resolve(string executablePath);
}

public sealed class SteamAwareLaunchPlanResolver : ILaunchPlanResolver
{
    private readonly IProcessPresentationFileSystem _fileSystem;

    public SteamAwareLaunchPlanResolver(IProcessPresentationFileSystem? fileSystem = null)
    {
        _fileSystem = fileSystem ?? new ProcessPresentationFileSystem();
    }

    public LaunchPlan Resolve(string executablePath)
    {
        string canonicalPath;
        try { canonicalPath = ProcessMatcher.CanonicalizePath(executablePath); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return LaunchPlan.Direct(executablePath);
        }

        var installation = SteamManifestCatalog.FindForExecutable(canonicalPath, _fileSystem);
        return installation is null
            ? LaunchPlan.Direct(canonicalPath)
            : LaunchPlan.Steam(canonicalPath, installation.AppId);
    }
}

public sealed record SteamInstallation(string AppId, string InstallDirectory, string? Name);

public static class SteamManifestCatalog
{
    private static readonly Regex ManifestName = new(
        "^appmanifest_(?<id>[0-9]+)\\.acf$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static SteamInstallation? FindForExecutable(
        string executablePath,
        IProcessPresentationFileSystem? fileSystem = null)
    {
        fileSystem ??= new ProcessPresentationFileSystem();
        try
        {
            var location = FindSteamLocation(executablePath);
            if (location is null)
                return null;

            SteamInstallation? match = null;
            foreach (var manifestPath in fileSystem.EnumerateFiles(location.Value.SteamAppsDirectory, "appmanifest_*.acf"))
            {
                if (!IsFileInDirectory(manifestPath, location.Value.SteamAppsDirectory))
                    continue;

                var nameMatch = ManifestName.Match(Path.GetFileName(manifestPath));
                if (!nameMatch.Success)
                    continue;

                if (!ValveKeyValuesParser.TryParseFlatFields(fileSystem.ReadAllText(manifestPath), out var fields))
                    continue;

                if (!fields.TryGetValue("installdir", out var installDirectory) ||
                    !string.Equals(installDirectory, location.Value.InstallDirectory, StringComparison.OrdinalIgnoreCase))
                    continue;

                fields.TryGetValue("name", out var gameName);
                if (match is not null)
                    return null;
                match = new SteamInstallation(nameMatch.Groups["id"].Value, installDirectory, gameName);
            }
            return match;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            // Manifestzugriff ist rein optional. Jeder unklare Befund fällt sicher auf Direktstart zurück.
        }

        return null;
    }

    private static (string SteamAppsDirectory, string InstallDirectory)? FindSteamLocation(string executablePath)
    {
        var parent = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(parent))
            return null;

        var current = new DirectoryInfo(parent);
        while (current.Parent is not null)
        {
            if (string.Equals(current.Parent.Name, "common", StringComparison.OrdinalIgnoreCase) &&
                current.Parent.Parent is { } steamApps &&
                string.Equals(steamApps.Name, "steamapps", StringComparison.OrdinalIgnoreCase))
            {
                return (steamApps.FullName, current.Name);
            }

            current = current.Parent;
        }

        return null;
    }

    private static bool IsFileInDirectory(string filePath, string directory)
    {
        var fileDirectory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        return fileDirectory is not null && string.Equals(
            Path.TrimEndingDirectorySeparator(fileDirectory),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)),
            StringComparison.OrdinalIgnoreCase);
    }
}

internal static class ValveKeyValuesParser
{
    public static bool TryParseFlatFields(string text, out IReadOnlyDictionary<string, string> fields)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var position = 0;
        var parsedAny = false;
        SkipWhitespace(text, ref position);
        var valid = TryReadQuoted(text, ref position, out var rootKey) &&
            string.Equals(rootKey, "AppState", StringComparison.OrdinalIgnoreCase);
        SkipWhitespace(text, ref position);
        valid = valid && position < text.Length && text[position++] == '{' &&
            ParseEntries(text, ref position, result, collectFields: true, ref parsedAny);
        SkipWhitespace(text, ref position);
        fields = result;
        return valid && parsedAny && position == text.Length;
    }

    private static bool ParseEntries(
        string text,
        ref int position,
        Dictionary<string, string> fields,
        bool collectFields,
        ref bool parsedAny)
    {
        while (true)
        {
            SkipWhitespace(text, ref position);
            if (position >= text.Length)
                return false;
            if (text[position] == '}')
            {
                position++;
                return true;
            }

            if (!TryReadQuoted(text, ref position, out var key))
                return false;
            SkipWhitespace(text, ref position);
            if (position >= text.Length)
                return false;

            if (text[position] == '{')
            {
                position++;
                if (!ParseEntries(text, ref position, fields, collectFields: false, ref parsedAny))
                    return false;
                continue;
            }

            if (!TryReadQuoted(text, ref position, out var value))
                return false;
            if (collectFields)
            {
                if (!fields.TryAdd(key, value))
                    return false;
                parsedAny = true;
            }
        }
    }

    private static bool TryReadQuoted(string text, ref int position, out string value)
    {
        value = string.Empty;
        if (position >= text.Length || text[position] != '"')
            return false;

        position++;
        var builder = new StringBuilder();
        while (position < text.Length)
        {
            var character = text[position++];
            if (character == '"')
            {
                value = builder.ToString();
                return true;
            }
            if (character == '\\')
            {
                if (position >= text.Length)
                    return false;
                character = text[position++];
            }
            builder.Append(character);
        }

        return false;
    }

    private static void SkipWhitespace(string text, ref int position)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position]))
            position++;
    }
}

public static class SteamLaunchInfo
{
    private static readonly Regex ValidUri = new(
        "^steam://run/[0-9]+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string CreateUri(string appId)
    {
        if (string.IsNullOrEmpty(appId) || appId.Any(character => !char.IsAsciiDigit(character)))
            throw new ArgumentException("Die Steam-App-ID ist ungültig.", nameof(appId));
        return $"steam://run/{appId}";
    }

    public static ProcessStartInfo Create(string steamUri)
    {
        if (!ValidUri.IsMatch(steamUri))
            throw new ArgumentException("Die Steam-Startadresse ist ungültig.", nameof(steamUri));

        return new ProcessStartInfo
        {
            FileName = steamUri,
            UseShellExecute = true
        };
    }
}
