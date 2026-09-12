using System.Diagnostics;

namespace DisplayModeSwitcher;

public sealed record ProcessDisplayInfo(
    ProcessIdentity Identity,
    string? ProductName,
    string? FileDescription,
    string? MainWindowTitle,
    nint MainWindowHandle);

public sealed record ProcessPresentationItem(string Path, string DisplayName, string ExecutableName)
{
    public override string ToString() => string.Equals(DisplayName, ExecutableName, StringComparison.OrdinalIgnoreCase)
        ? ExecutableName
        : $"{DisplayName} [{ExecutableName}]";
}

public static class ProcessPresentation
{
    public static IReadOnlyList<ProcessPresentationItem> CreateItems(
        IEnumerable<ProcessDisplayInfo> processes,
        string toolExecutablePath,
        bool includeBackgroundProcesses)
    {
        var toolPath = TryCanonicalize(toolExecutablePath);
        return processes
            .Where(process => !string.IsNullOrWhiteSpace(process.Identity.ExecutablePath))
            .Where(process => !string.Equals(process.Identity.ExecutablePath, toolPath, StringComparison.OrdinalIgnoreCase))
            .Where(process => includeBackgroundProcesses || IsUserApplication(process))
            .GroupBy(process => process.Identity.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(process => process.Identity.Id)
                .ThenBy(process => process.Identity.StartTimeUtc)
                .First())
            .Select(CreateItem)
            .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.ExecutableName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static ProcessPresentationItem CreateProfileItem(string profileKey)
    {
        var executableName = Path.GetFileName(profileKey.Trim());
        if (string.IsNullOrWhiteSpace(executableName))
            executableName = profileKey.Trim();

        var displayName = GetFriendlyNameFromFile(executableName, null, TryGetVersionInfo(profileKey));
        return new ProcessPresentationItem(profileKey, displayName, executableName);
    }

    public static string GetFriendlyNameFromFile(string executableName, string? mainWindowTitle, FileVersionInfo? versionInfo)
        => GetFriendlyName(executableName, versionInfo?.ProductName, versionInfo?.FileDescription, mainWindowTitle);

    public static string GetFriendlyName(string executableName, string? productName, string? fileDescription, string? mainWindowTitle)
    {
        var candidates = new[]
        {
            productName,
            fileDescription,
            mainWindowTitle,
            executableName
        };

        foreach (var candidate in candidates)
        {
            var normalized = Normalize(candidate);
            if (string.IsNullOrWhiteSpace(normalized))
                continue;
            if (!IsRedundant(normalized, executableName))
                return normalized;
        }

        return executableName;
    }

    private static ProcessPresentationItem CreateItem(ProcessDisplayInfo process)
    {
        var executableName = Path.GetFileName(process.Identity.ExecutablePath);
        var versionInfo = TryGetVersionInfo(process.Identity.ExecutablePath);
        var displayName = GetFriendlyName(
            executableName,
            versionInfo?.ProductName ?? process.ProductName,
            versionInfo?.FileDescription ?? process.FileDescription,
            process.MainWindowTitle);
        return new ProcessPresentationItem(process.Identity.ExecutablePath, displayName, executableName);
    }

    private static bool IsUserApplication(ProcessDisplayInfo process) =>
        process.MainWindowHandle != 0 && !string.IsNullOrWhiteSpace(Normalize(process.MainWindowTitle));

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool IsRedundant(string value, string executableName)
    {
        var executableStem = Path.GetFileNameWithoutExtension(executableName);
        return string.Equals(value, executableName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, executableStem, StringComparison.OrdinalIgnoreCase);
    }

    private static FileVersionInfo? TryGetVersionInfo(string executablePath)
    {
        try { return FileVersionInfo.GetVersionInfo(executablePath); }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static string TryCanonicalize(string path)
    {
        try { return ProcessMatcher.CanonicalizePath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}
