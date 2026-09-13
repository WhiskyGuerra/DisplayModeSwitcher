using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DisplayModeSwitcher;

public sealed record ProcessDisplayInfo(ProcessIdentity Identity, string? ProductName, string? FileDescription, string? MainWindowTitle, nint MainWindowHandle);

public sealed record ProcessPresentationItem(string Path, string DisplayName, string ExecutableName)
{
    public override string ToString() => string.Equals(DisplayName, ExecutableName, StringComparison.OrdinalIgnoreCase) ? ExecutableName : $"{DisplayName} [{ExecutableName}]";
}

public interface IProcessPresentationFileSystem
{
    IEnumerable<string> EnumerateFiles(string directory, string pattern);
    string ReadAllText(string path);
}

public sealed class ProcessPresentationFileSystem : IProcessPresentationFileSystem
{
    public IEnumerable<string> EnumerateFiles(string directory, string pattern) => Directory.EnumerateFiles(directory, pattern);
    public string ReadAllText(string path) => File.ReadAllText(path);
}

public static class ProcessPresentation
{
    private static readonly HashSet<string> GenericFolderNames = new(StringComparer.OrdinalIgnoreCase) { "bin", "x64", "x86", "win64", "win32", "binaries", "release", "debug", "common", "steamapps", "program files", "program files (x86)" };

    public static IReadOnlyList<ProcessPresentationItem> CreateItems(IEnumerable<ProcessDisplayInfo> processes, string toolExecutablePath, bool includeBackgroundProcesses, string? windowsDirectory = null, IProcessPresentationFileSystem? fileSystem = null)
    {
        fileSystem ??= new ProcessPresentationFileSystem();
        var toolName = Path.GetFileName(toolExecutablePath);
        windowsDirectory ??= Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return processes.Where(p => !string.IsNullOrWhiteSpace(p.Identity.ExecutablePath))
            .Where(p => !string.Equals(Path.GetFileName(p.Identity.ExecutablePath), toolName, StringComparison.OrdinalIgnoreCase))
            .Where(p => includeBackgroundProcesses || (IsUserApplication(p) && !IsWindowsPath(p.Identity.ExecutablePath, windowsDirectory)))
            .GroupBy(p => p.Identity.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(p => p.Identity.Id).ThenBy(p => p.Identity.StartTimeUtc).First())
            .Select(p => CreateItem(p, fileSystem))
            .OrderBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase).ThenBy(i => i.ExecutableName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static ProcessPresentationItem CreateProfileItem(string profileKey, IProcessPresentationFileSystem? fileSystem = null)
    {
        fileSystem ??= new ProcessPresentationFileSystem();
        var executableName = Path.GetFileName(profileKey.Trim());
        if (string.IsNullOrWhiteSpace(executableName)) executableName = profileKey.Trim();
        var info = TryGetVersionInfo(profileKey);
        var name = GetFriendlyName(executableName, info?.ProductName, info?.FileDescription, null, FindSteamGameName(profileKey, fileSystem), GetInstallationFolderName(profileKey));
        return new(profileKey, name, executableName);
    }

    public static bool IsMissingProfileExecutable(string profileKey, Func<string, bool>? fileExists = null)
    {
        if (!Path.IsPathFullyQualified(profileKey))
            return false;

        try { return !(fileExists ?? File.Exists)(profileKey); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    public static string GetFriendlyNameFromFile(string executableName, string? mainWindowTitle, FileVersionInfo? versionInfo) => GetFriendlyName(executableName, versionInfo?.ProductName, versionInfo?.FileDescription, mainWindowTitle);

    public static string GetFriendlyName(string executableName, string? productName, string? fileDescription, string? mainWindowTitle, string? steamGameName = null, string? installationFolderName = null)
    {
        // System stubs commonly expose only technical metadata; their real window title is
        // more useful than falling all the way back to the executable.
        if (IsTechnicalMetadata(productName) && (IsTechnicalMetadata(fileDescription) || (!string.IsNullOrWhiteSpace(fileDescription) && IsRedundant(fileDescription, executableName))))
        {
            var title = Normalize(mainWindowTitle);
            if (!string.IsNullOrWhiteSpace(title) && !title.Contains('\\') && !title.Contains('/') && !IsRedundant(title, executableName)) return title;
        }
        foreach (var candidate in new[] { productName, fileDescription, steamGameName, mainWindowTitle, installationFolderName, executableName })
        {
            var value = Normalize(candidate);
            if (!string.IsNullOrWhiteSpace(value) && !IsUnhelpfulName(value, executableName)) return value;
        }
        return executableName;
    }

    public static string? FindSteamGameName(string executablePath, IProcessPresentationFileSystem? fileSystem = null)
    {
        fileSystem ??= new ProcessPresentationFileSystem();
        try
        {
            var parent = Path.GetDirectoryName(executablePath);
            if (string.IsNullOrWhiteSpace(parent)) return null;
            var current = new DirectoryInfo(parent);
            DirectoryInfo? install = null;
            DirectoryInfo? steamApps = null;
            while (current.Parent is not null)
            {
                if (string.Equals(current.Parent.Name, "common", StringComparison.OrdinalIgnoreCase) && current.Parent.Parent is { } candidate && string.Equals(candidate.Name, "steamapps", StringComparison.OrdinalIgnoreCase)) { install = current; steamApps = candidate; break; }
                current = current.Parent;
            }
            if (install is null || steamApps is null) return null;
            foreach (var manifest in fileSystem.EnumerateFiles(steamApps.FullName, "appmanifest_*.acf"))
            {
                var fields = ParseVdfFields(fileSystem.ReadAllText(manifest));
                if (fields.TryGetValue("installdir", out var dir) && string.Equals(dir, install.Name, StringComparison.OrdinalIgnoreCase) && fields.TryGetValue("name", out var name) && !IsUnhelpfulName(name, Path.GetFileName(executablePath))) return Normalize(name);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        return null;
    }

    private static ProcessPresentationItem CreateItem(ProcessDisplayInfo process, IProcessPresentationFileSystem fileSystem)
    {
        var exe = Path.GetFileName(process.Identity.ExecutablePath);
        var info = TryGetVersionInfo(process.Identity.ExecutablePath);
        var name = GetFriendlyName(exe, info?.ProductName ?? process.ProductName, info?.FileDescription ?? process.FileDescription, process.MainWindowTitle, FindSteamGameName(process.Identity.ExecutablePath, fileSystem), GetInstallationFolderName(process.Identity.ExecutablePath));
        return new(process.Identity.ExecutablePath, name, exe);
    }

    private static bool IsUserApplication(ProcessDisplayInfo process) => process.MainWindowHandle != 0 && !string.IsNullOrWhiteSpace(Normalize(process.MainWindowTitle));

    private static bool IsWindowsPath(string path, string windowsDirectory)
    {
        try { var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(windowsDirectory)); var full = Path.GetFullPath(path); return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || string.Equals(full, root, StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    private static string? GetInstallationFolderName(string path)
    {
        try
        {
            var directory = new DirectoryInfo(Path.GetDirectoryName(path) ?? string.Empty);
            while (directory.Parent is not null)
            {
                var name = Normalize(directory.Name);
                if (!string.IsNullOrWhiteSpace(name) && !GenericFolderNames.Contains(name)) return name;
                directory = directory.Parent;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
        return null;
    }

    private static Dictionary<string, string> ParseVdfFields(string text)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(text, "\\\"(?<key>[^\\\"]+)\\\"\\s*\\\"(?<value>(?:\\\\.|[^\\\"])*)\\\"")) fields.TryAdd(match.Groups["key"].Value, Regex.Unescape(match.Groups["value"].Value));
        return fields;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static bool IsUnhelpfulName(string value, string executableName)
    {
        var compact = Compact(value);
        var stem = Compact(Path.GetFileNameWithoutExtension(executableName));
        return string.IsNullOrWhiteSpace(compact) || value.Contains('\\') || value.Contains('/') || GenericFolderNames.Contains(value) || string.Equals(compact, stem, StringComparison.OrdinalIgnoreCase) || compact.Equals("bootstrappackagedgame", StringComparison.OrdinalIgnoreCase) || compact.Equals("net", StringComparison.OrdinalIgnoreCase) || compact.Equals("microsoftwindowsoperatingsystem", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTechnicalMetadata(string? value)
    {
        var normalized = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized)) return true;
        var compact = Compact(normalized);
        return compact.Equals("bootstrappackagedgame", StringComparison.OrdinalIgnoreCase) || compact.Equals("net", StringComparison.OrdinalIgnoreCase) || compact.Equals("microsoftwindowsoperatingsystem", StringComparison.OrdinalIgnoreCase) || IsRedundant(normalized, "placeholder.exe");
    }

    private static bool IsRedundant(string value, string executableName)
    {
        var compact = Compact(value);
        var stem = Compact(Path.GetFileNameWithoutExtension(executableName));
        return string.Equals(compact, stem, StringComparison.OrdinalIgnoreCase);
    }

    private static string Compact(string value) => new(value.Where(character => character <= 127 && char.IsLetterOrDigit(character)).ToArray());

    private static FileVersionInfo? TryGetVersionInfo(string path) { try { return FileVersionInfo.GetVersionInfo(path); } catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or System.ComponentModel.Win32Exception) { return null; } }
}
