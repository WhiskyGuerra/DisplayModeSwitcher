using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

namespace DisplayModeSwitcher;

public sealed record PreparedUpdate(string RunnerExecutable, string RequestPath);
public sealed record UpdatePreparationResult(bool Success, PreparedUpdate? Update = null, string? Error = null);
internal sealed record UpdateInstallRequest(int ProcessId, string InstallDirectory, string PayloadDirectory, string BackupDirectory, string ExecutableName, string ResultPath);
internal sealed record UpdateInstallResult(bool Success, string? Error = null, bool RollbackSucceeded = true);

/// <summary>Prüft und entpackt ein bereits hashverifiziertes ZIP, ohne die Installation zu berühren.</summary>
public static class UpdateArchivePreparer
{
    internal const int MaximumEntries = 500;
    internal const long MaximumExtractedBytes = 250L * 1024 * 1024;
    private static readonly string[] RequiredRootFiles =
    [
        "DisplayModeSwitcher.exe",
        "DisplayModeSwitcher.dll",
        "DisplayModeSwitcher.deps.json",
        "DisplayModeSwitcher.runtimeconfig.json"
    ];

    public static UpdatePreparationResult Prepare(string archivePath, string stagingDirectory, string currentExecutablePath, int processId, Version expectedVersion)
    {
        string? payloadDirectory = null;
        string? runnerDirectory = null;
        string? backupDirectory = null;
        try
        {
            var archive = Path.GetFullPath(archivePath);
            var staging = Path.GetFullPath(stagingDirectory);
            var executable = Path.GetFullPath(currentExecutablePath);
            ArgumentNullException.ThrowIfNull(expectedVersion);
            var installDirectory = Path.GetDirectoryName(executable) ?? throw new InvalidOperationException("Der Installationsordner konnte nicht bestimmt werden.");
            if (!File.Exists(archive)) return Failed("Das geprüfte Update-Paket ist nicht mehr vorhanden.");
            if (processId <= 0) return Failed("Die laufende Prozess-ID ist ungültig.");
            if (staging == Path.GetPathRoot(staging) || Overlaps(staging, installDirectory))
                return Failed("Update-Staging und Installation müssen in getrennten Ordnern liegen.");
            var stagingRoot = staging.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!archive.StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase))
                return Failed("Das Update-Paket liegt nicht im erwarteten Staging-Ordner.");

            payloadDirectory = Path.Combine(staging, "payload");
            runnerDirectory = Path.Combine(staging, "runner");
            backupDirectory = Path.Combine(staging, "backup");
            if (Directory.Exists(payloadDirectory) || Directory.Exists(runnerDirectory) || Directory.Exists(backupDirectory))
                return Failed("Der Update-Stagingordner wurde bereits für eine Installation verwendet.");

            ExtractSafely(archive, payloadDirectory);
            foreach (var required in RequiredRootFiles)
                if (!File.Exists(Path.Combine(payloadDirectory, required)))
                    throw new InvalidDataException($"Im Update-Paket fehlt '{required}'.");
            ValidateApplicationIdentity(Path.Combine(payloadDirectory, "DisplayModeSwitcher.dll"), expectedVersion);

            // Der Runner stammt aus dem bereits hash- und versionsgeprüften Payload.
            // Dadurch können neue Releases auch Fehler im Updater selbst beheben.
            Directory.CreateDirectory(runnerDirectory);
            foreach (var runnerFile in RequiredRootFiles)
            {
                var source = Path.Combine(payloadDirectory, runnerFile);
                File.Copy(source, Path.Combine(runnerDirectory, runnerFile), overwrite: false);
            }

            var requestPath = Path.Combine(staging, "install-request.json");
            var resultPath = Path.Combine(staging, "install-result.json");
            var request = new UpdateInstallRequest(processId, installDirectory, payloadDirectory, backupDirectory, Path.GetFileName(executable), resultPath);
            File.WriteAllText(requestPath, JsonSerializer.Serialize(request));
            return new(true, new PreparedUpdate(Path.Combine(runnerDirectory, Path.GetFileName(executable)), requestPath));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or OverflowException)
        {
            CleanupDirectory(payloadDirectory);
            CleanupDirectory(runnerDirectory);
            CleanupDirectory(backupDirectory);
            return Failed($"Das Update-Paket konnte nicht sicher vorbereitet werden: {ex.Message}");
        }
    }

    internal static void ExtractSafely(string archivePath, string payloadDirectory)
    {
        Directory.CreateDirectory(payloadDirectory);
        var root = Path.GetFullPath(payloadDirectory) + Path.DirectorySeparatorChar;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count == 0 || archive.Entries.Count > MaximumEntries)
            throw new InvalidDataException("Das Update-Paket enthält keine oder zu viele Einträge.");

        foreach (var entry in archive.Entries)
        {
            var relative = entry.FullName.Replace('\\', '/');
            var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0) continue;
            if (Path.IsPathRooted(relative) || relative.Contains(':') || segments.Any(segment => segment is "." or ".."))
                throw new InvalidDataException($"Unsicherer ZIP-Pfad: {entry.FullName}");
            if (IsSymbolicLink(entry)) throw new InvalidDataException($"Symbolische Links sind im Update-Paket nicht erlaubt: {entry.FullName}");
            if (segments.Any(segment => string.Equals(segment, "profiles.json", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Profildateien dürfen nicht Bestandteil eines Update-Pakets sein.");

            var destination = Path.GetFullPath(Path.Combine(payloadDirectory, Path.Combine(segments)));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !paths.Add(destination))
                throw new InvalidDataException($"Mehrdeutiger oder unsicherer ZIP-Pfad: {entry.FullName}");
            totalBytes = checked(totalBytes + entry.Length);
            if (totalBytes > MaximumExtractedBytes) throw new InvalidDataException("Das entpackte Update überschreitet das Größenlimit.");

            if (relative.EndsWith('/'))
            {
                Directory.CreateDirectory(destination);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var source = entry.Open();
            using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            source.CopyTo(target);
            if (target.Length != entry.Length) throw new InvalidDataException($"ZIP-Eintrag wurde nicht vollständig entpackt: {entry.FullName}");
        }
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry) => ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;
    private static void ValidateApplicationIdentity(string assemblyPath, Version expectedVersion)
    {
        AssemblyName identity;
        try { identity = AssemblyName.GetAssemblyName(assemblyPath); }
        catch (Exception ex) when (ex is BadImageFormatException or FileLoadException)
        {
            throw new InvalidDataException("Das Update enthält keine gültige Display-Mode-Switcher-Assembly.", ex);
        }

        var expected = Normalize(expectedVersion);
        var actual = Normalize(identity.Version ?? new Version());
        if (!string.Equals(identity.Name, "DisplayModeSwitcher", StringComparison.Ordinal) || actual != expected)
            throw new InvalidDataException($"Die Paketversion {actual} passt nicht zum Release {expected}.");
    }

    private static Version Normalize(Version version) => new(
        Math.Max(0, version.Major), Math.Max(0, version.Minor),
        Math.Max(0, version.Build), Math.Max(0, version.Revision));

    private static bool Overlaps(string first, string second)
    {
        var firstRoot = Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var secondRoot = Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return string.Equals(firstRoot, secondRoot, StringComparison.OrdinalIgnoreCase) ||
               firstRoot.StartsWith(secondRoot, StringComparison.OrdinalIgnoreCase) ||
               secondRoot.StartsWith(firstRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static void CleanupDirectory(string? path)
    {
        if (path is null) return;
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { }
    }

    private static UpdatePreparationResult Failed(string error) => new(false, Error: error);
}

/// <summary>Überschreibt ausschließlich Dateien aus dem geprüften Payload und rollt Teiländerungen zurück.</summary>
internal static class TransactionalUpdateInstaller
{
    internal const int FileOperationAttempts = 40;
    internal const int FileOperationDelayMilliseconds = 250;

    internal static UpdateInstallResult Apply(UpdateInstallRequest request, Func<string, bool> startApplication)
    {
        try
        {
            ValidateRequest(request);
            Directory.CreateDirectory(request.BackupDirectory);
            var payloadRoot = Path.GetFullPath(request.PayloadDirectory) + Path.DirectorySeparatorChar;
            var installRoot = Path.GetFullPath(request.InstallDirectory) + Path.DirectorySeparatorChar;
            var backupRoot = Path.GetFullPath(request.BackupDirectory) + Path.DirectorySeparatorChar;
            var applied = new List<(string Target, string? Backup)>();

            try
            {
                foreach (var source in Directory.EnumerateFiles(request.PayloadDirectory, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("Das vorbereitete Update enthält einen nicht erlaubten Verweis.");
                    var relative = Path.GetRelativePath(payloadRoot, source);
                    if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
                        throw new InvalidDataException("Eine Update-Datei liegt außerhalb des vorbereiteten Pakets.");
                    var target = SafeCombine(installRoot, relative);
                    var backup = File.Exists(target) ? SafeCombine(backupRoot, relative) : null;
                    if (backup is not null)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                        RetryFileOperation(
                            () => File.Copy(target, backup, overwrite: false),
                            "Backup", target);
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    RetryFileOperation(() => ReplaceFile(source, target), "Austausch", target);
                    applied.Add((target, backup));
                }

                var executable = SafeCombine(installRoot, request.ExecutableName);
                if (!File.Exists(executable) || !startApplication(executable))
                    throw new IOException("Die aktualisierte Anwendung konnte nicht gestartet werden.");
                return new(true);
            }
            catch (Exception ex)
            {
                var rollbackErrors = Rollback(applied);
                var suffix = rollbackErrors.Count == 0 ? string.Empty : $" Rollbackfehler: {string.Join(" | ", rollbackErrors)}";
                return new(false, $"Das Update wurde nicht installiert: {ex.Message}{suffix}", rollbackErrors.Count == 0);
            }
        }
        catch (Exception ex)
        {
            return new(false, $"Der Updateauftrag ist ungültig: {ex.Message}", false);
        }
    }

    private static void ValidateRequest(UpdateInstallRequest request)
    {
        var install = Path.GetFullPath(request.InstallDirectory);
        var payload = Path.GetFullPath(request.PayloadDirectory);
        var backup = Path.GetFullPath(request.BackupDirectory);
        if (install == Path.GetPathRoot(install) || payload == Path.GetPathRoot(payload) || backup == Path.GetPathRoot(backup))
            throw new InvalidDataException("Ein Updatepfad darf kein Laufwerksstamm sein.");
        if (!Directory.Exists(install) || !Directory.Exists(payload)) throw new DirectoryNotFoundException("Installations- oder Payloadordner fehlt.");
        if (Overlaps(install, payload) || Overlaps(install, backup) || Overlaps(payload, backup))
            throw new InvalidDataException("Installations-, Payload- und Backupordner müssen sicher getrennt sein.");
        if (string.IsNullOrWhiteSpace(request.ExecutableName) || Path.GetFileName(request.ExecutableName) != request.ExecutableName)
            throw new InvalidDataException("Der Anwendungsname ist ungültig.");
    }

    private static bool IsWithin(string candidate, string parent)
    {
        var parentRoot = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(candidate).StartsWith(parentRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Overlaps(string first, string second) =>
        string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase) ||
        IsWithin(first, second) || IsWithin(second, first);

    private static string SafeCombine(string rootWithSeparator, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(rootWithSeparator, relative));
        if (!path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Updatepfad verlässt den erlaubten Ordner.");
        return path;
    }

    private static void ReplaceFile(string source, string target)
    {
        var temporary = target + $".dms-new-{Guid.NewGuid():N}";
        try
        {
            File.Copy(source, temporary, overwrite: false);
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch { }
        }
    }

    private static void RetryFileOperation(Action operation, string actionName, string target)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= FileOperationAttempts; attempt++)
        {
            try
            {
                operation();
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastError = ex;
                if (attempt < FileOperationAttempts) Thread.Sleep(FileOperationDelayMilliseconds);
            }
        }

        throw new IOException(
            $"{actionName} für '{Path.GetFileName(target)}' war nach {FileOperationAttempts * FileOperationDelayMilliseconds / 1000} Sekunden nicht möglich: {lastError?.Message}",
            lastError);
    }

    private static List<string> Rollback(IEnumerable<(string Target, string? Backup)> applied)
    {
        var errors = new List<string>();
        foreach (var item in applied.Reverse())
        {
            try
            {
                if (item.Backup is null) File.Delete(item.Target);
                else RetryFileOperation(() => ReplaceFile(item.Backup, item.Target), "Rollback", item.Target);
            }
            catch (Exception ex) { errors.Add($"{Path.GetFileName(item.Target)}: {ex.Message}"); }
        }
        return errors;
    }
}

internal static class UpdateInstallerProcess
{
    internal const string ApplyArgument = "--apply-update";

    internal static bool TryRun(string[] args)
    {
        if (args.Length != 2 || !string.Equals(args[0], ApplyArgument, StringComparison.Ordinal)) return false;
        Run(args[1]);
        return true;
    }

    internal static bool Launch(PreparedUpdate update)
    {
        var start = new ProcessStartInfo(update.RunnerExecutable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(update.RunnerExecutable)! };
        start.ArgumentList.Add(ApplyArgument);
        start.ArgumentList.Add(update.RequestPath);
        return Process.Start(start) is not null;
    }

    private static void Run(string requestPath)
    {
        UpdateInstallRequest? request = null;
        UpdateInstallResult result;
        try
        {
            request = JsonSerializer.Deserialize<UpdateInstallRequest>(File.ReadAllText(Path.GetFullPath(requestPath)))
                ?? throw new InvalidDataException("Der Updateauftrag ist leer.");
            WaitForProcess(request.ProcessId);
            result = TransactionalUpdateInstaller.Apply(request, executable =>
                Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(executable)! }) is not null);
        }
        catch (Exception ex)
        {
            result = new(false, $"Der separate Updater ist fehlgeschlagen: {ex.Message}", false);
        }

        if (request is not null)
        {
            try { File.WriteAllText(request.ResultPath, JsonSerializer.Serialize(result)); }
            catch { }
        }

        if (!result.Success)
        {
            var rollback = result.RollbackSucceeded
                ? "Die bisherige Installation wurde wiederhergestellt. Bitte Display Mode Switcher manuell erneut starten."
                : "Die Wiederherstellung war nicht vollständig. Bitte die Dateien im Update-Backup prüfen, bevor das Tool erneut gestartet wird.";
            MessageBox.Show($"{result.Error}\n\n{rollback}", "Update fehlgeschlagen", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void WaitForProcess(int processId)
    {
        if (processId <= 0 || processId == Environment.ProcessId) throw new InvalidDataException("Die zu wartende Prozess-ID ist ungültig.");
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.WaitForExit(30000)) throw new TimeoutException("Die laufende Anwendung wurde nicht rechtzeitig beendet.");
        }
        catch (ArgumentException) { }
    }
}
