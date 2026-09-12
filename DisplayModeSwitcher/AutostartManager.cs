using Microsoft.Win32;
using System.Security;

namespace DisplayModeSwitcher;

public interface IAutostartRegistry
{
    OperationResult Read(out string? value);
    OperationResult Write(string value);
    OperationResult Delete();
}

public sealed class CurrentUserAutostartRegistry : IAutostartRegistry
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DisplayModeSwitcher";

    public OperationResult Read(out string? value)
    {
        value = null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunPath, writable: false);
            value = key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            return OperationResult.Fail($"Autostart-Status konnte nicht gelesen werden: {ex.Message}");
        }
    }

    public OperationResult Write(string value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunPath, writable: true);
            if (key is null)
                return OperationResult.Fail("Der Autostart-Schlüssel konnte nicht geöffnet werden.");
            key.SetValue(ValueName, value, RegistryValueKind.String);
            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            return OperationResult.Fail($"Autostart konnte nicht aktiviert werden: {ex.Message}");
        }
    }

    public OperationResult Delete()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            return OperationResult.Fail($"Autostart konnte nicht deaktiviert werden: {ex.Message}");
        }
    }
}

public sealed record AutostartStatus(bool IsEnabled, string? Error = null);

public sealed class AutostartService
{
    private readonly string _executablePath;
    private readonly IAutostartRegistry _registry;

    public AutostartService(string executablePath, IAutostartRegistry registry)
    {
        _executablePath = ProcessMatcher.CanonicalizePath(executablePath);
        _registry = registry;
    }

    public string ExpectedCommand => $"\"{_executablePath}\"";

    public AutostartStatus GetStatus()
    {
        var result = _registry.Read(out var value);
        if (!result.Success)
            return new(false, result.Error);
        return new(IsExpectedCommand(value));
    }

    public OperationResult Enable()
    {
        var write = _registry.Write(ExpectedCommand);
        if (!write.Success) return write;
        var status = GetStatus();
        return status.Error is not null
            ? OperationResult.Fail(status.Error)
            : status.IsEnabled ? OperationResult.Ok() : OperationResult.Fail("Der Autostart-Eintrag wurde geschrieben, entspricht aber nicht dem erwarteten Programmaufruf.");
    }

    public OperationResult Disable()
    {
        var delete = _registry.Delete();
        if (!delete.Success) return delete;
        var status = GetStatus();
        return status.Error is not null
            ? OperationResult.Fail(status.Error)
            : !status.IsEnabled ? OperationResult.Ok() : OperationResult.Fail("Der Autostart-Eintrag ist weiterhin aktiv.");
    }

    public bool IsExpectedCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        var trimmed = command.Trim();
        string commandPath;
        if (!trimmed.StartsWith('"')) return false;
        var closingQuote = trimmed.IndexOf('"', 1);
        if (closingQuote < 0 || !string.IsNullOrWhiteSpace(trimmed[(closingQuote + 1)..])) return false;
        commandPath = trimmed[1..closingQuote];

        try
        {
            return string.Equals(ProcessMatcher.CanonicalizePath(Environment.ExpandEnvironmentVariables(commandPath)), _executablePath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
