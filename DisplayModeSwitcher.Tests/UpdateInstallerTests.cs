using System.IO.Compression;

namespace DisplayModeSwitcher;

internal static class UpdateInstallerTests
{
    private static readonly string[] RequiredFiles =
    [
        "DisplayModeSwitcher.exe",
        "DisplayModeSwitcher.dll",
        "DisplayModeSwitcher.deps.json",
        "DisplayModeSwitcher.runtimeconfig.json"
    ];

    internal static void ValidArchiveIsPreparedWithoutTouchingInstallation()
    {
        WithTemporaryDirectory(root =>
        {
            var install = Path.Combine(root, "install");
            var staging = Path.Combine(root, "staging");
            Directory.CreateDirectory(install);
            Directory.CreateDirectory(staging);
            foreach (var file in RequiredFiles) File.WriteAllText(Path.Combine(install, file), "old-" + file);
            var archive = CreateValidApplicationArchive(staging);
            var expectedVersion = typeof(UpdateArchivePreparer).Assembly.GetName().Version!;

            var result = UpdateArchivePreparer.Prepare(archive, staging, Path.Combine(install, RequiredFiles[0]), 42, expectedVersion);

            True(result.Success, result.Error);
            True(result.Update is not null);
            True(File.Exists(result.Update!.RunnerExecutable));
            True(File.Exists(result.Update.RequestPath));
            Equal("new-DisplayModeSwitcher.exe", File.ReadAllText(result.Update.RunnerExecutable));
            Equal("new-DisplayModeSwitcher.exe", File.ReadAllText(Path.Combine(staging, "payload", RequiredFiles[0])));
            Equal("old-DisplayModeSwitcher.exe", File.ReadAllText(Path.Combine(install, RequiredFiles[0])));
        });
    }

    internal static void UnsafeOrProfileArchivesAreRejectedAndCleaned()
    {
        WithTemporaryDirectory(root =>
        {
            var install = CreateInstallation(root);
            foreach (var unsafeName in new[] { "../outside.txt", "profiles.json" })
            {
                var staging = Path.Combine(root, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(staging);
                var entries = RequiredFiles.Select(file => (file, "new")).Append((unsafeName, "forbidden"));
                var archive = CreateArchive(staging, entries);

                var result = UpdateArchivePreparer.Prepare(archive, staging, Path.Combine(install, RequiredFiles[0]), 42, new Version(1, 0, 0, 0));

                True(!result.Success);
                True(!Directory.Exists(Path.Combine(staging, "payload")));
                True(!File.Exists(Path.Combine(root, "outside.txt")));
            }
        });
    }

    internal static void MissingApplicationFilesAreRejected()
    {
        WithTemporaryDirectory(root =>
        {
            var install = CreateInstallation(root);
            var staging = Path.Combine(root, "staging");
            Directory.CreateDirectory(staging);
            var archive = CreateArchive(staging, new[] { ("DisplayModeSwitcher.exe", "new") });

            var result = UpdateArchivePreparer.Prepare(archive, staging, Path.Combine(install, RequiredFiles[0]), 42, new Version(1, 0, 0, 0));

            True(!result.Success);
            True(!Directory.Exists(Path.Combine(staging, "payload")));
        });
    }

    internal static void StagingInsideInstallationIsRejected()
    {
        WithTemporaryDirectory(root =>
        {
            var install = CreateInstallation(root);
            var staging = Path.Combine(install, "updates");
            Directory.CreateDirectory(staging);
            var archive = CreateArchive(staging, RequiredFiles.Select(file => (file, "new")));

            var result = UpdateArchivePreparer.Prepare(archive, staging, Path.Combine(install, RequiredFiles[0]), 42, new Version(1, 0, 0, 0));

            True(!result.Success);
            Equal("old-DisplayModeSwitcher.exe", File.ReadAllText(Path.Combine(install, RequiredFiles[0])));
        });
    }

    internal static void PackageVersionMustMatchReleaseVersion()
    {
        WithTemporaryDirectory(root =>
        {
            var install = CreateInstallation(root);
            var staging = Path.Combine(root, "staging");
            Directory.CreateDirectory(staging);
            var archive = CreateValidApplicationArchive(staging);
            var actual = typeof(UpdateArchivePreparer).Assembly.GetName().Version!;
            var wrong = new Version(actual.Major, actual.Minor, actual.Build + 1, 0);

            var result = UpdateArchivePreparer.Prepare(archive, staging, Path.Combine(install, RequiredFiles[0]), 42, wrong);

            True(!result.Success);
            True(result.Error?.Contains("passt nicht zum Release", StringComparison.Ordinal) == true);
            True(!Directory.Exists(Path.Combine(staging, "payload")));
        });
    }

    internal static void TransactionReplacesPayloadAndKeepsBackup()
    {
        WithTemporaryDirectory(root =>
        {
            var install = CreateInstallation(root);
            var payload = Path.Combine(root, "payload");
            var backup = Path.Combine(root, "backup");
            Directory.CreateDirectory(payload);
            File.WriteAllText(Path.Combine(payload, RequiredFiles[0]), "new-exe");
            File.WriteAllText(Path.Combine(payload, "new-file.txt"), "new-file");
            string? started = null;
            var request = Request(install, payload, backup);

            var result = TransactionalUpdateInstaller.Apply(request, executable =>
            {
                started = executable;
                return true;
            });

            True(result.Success, result.Error);
            Equal(Path.Combine(install, RequiredFiles[0]), started);
            Equal("new-exe", File.ReadAllText(Path.Combine(install, RequiredFiles[0])));
            Equal("new-file", File.ReadAllText(Path.Combine(install, "new-file.txt")));
            Equal("old-DisplayModeSwitcher.exe", File.ReadAllText(Path.Combine(backup, RequiredFiles[0])));
        });
    }

    internal static void TransientExecutableLockIsRetried()
    {
        WithTemporaryDirectory(root =>
        {
            var install = CreateInstallation(root);
            var payload = Path.Combine(root, "payload");
            var backup = Path.Combine(root, "backup");
            Directory.CreateDirectory(payload);
            File.WriteAllText(Path.Combine(payload, RequiredFiles[0]), "new-exe");
            var executable = Path.Combine(install, RequiredFiles[0]);
            var temporaryLock = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.None);
            var releaseLock = Task.Run(() =>
            {
                Thread.Sleep(600);
                temporaryLock.Dispose();
            });

            var result = TransactionalUpdateInstaller.Apply(Request(install, payload, backup), _ => true);
            releaseLock.GetAwaiter().GetResult();

            True(result.Success, result.Error);
            Equal("new-exe", File.ReadAllText(executable));
            Equal("old-DisplayModeSwitcher.exe", File.ReadAllText(Path.Combine(backup, RequiredFiles[0])));
        });
    }

    internal static void FailedRestartRollsBackEveryChangedFile()
    {
        WithTemporaryDirectory(root =>
        {
            var install = CreateInstallation(root);
            var payload = Path.Combine(root, "payload");
            var backup = Path.Combine(root, "backup");
            Directory.CreateDirectory(payload);
            File.WriteAllText(Path.Combine(payload, RequiredFiles[0]), "new-exe");
            File.WriteAllText(Path.Combine(payload, "new-file.txt"), "new-file");

            var result = TransactionalUpdateInstaller.Apply(Request(install, payload, backup), _ => false);

            True(!result.Success);
            True(result.RollbackSucceeded, result.Error);
            Equal("old-DisplayModeSwitcher.exe", File.ReadAllText(Path.Combine(install, RequiredFiles[0])));
            True(!File.Exists(Path.Combine(install, "new-file.txt")));
        });
    }

    internal static void OverlappingUpdateDirectoriesAreRejected()
    {
        WithTemporaryDirectory(root =>
        {
            var install = CreateInstallation(root);
            var payload = Path.Combine(root, "payload");
            Directory.CreateDirectory(payload);
            File.WriteAllText(Path.Combine(payload, RequiredFiles[0]), "new");
            var backupInsideInstall = Path.Combine(install, "backup");

            var result = TransactionalUpdateInstaller.Apply(Request(install, payload, backupInsideInstall), _ => true);

            True(!result.Success);
            True(!Directory.Exists(backupInsideInstall));
            Equal("old-DisplayModeSwitcher.exe", File.ReadAllText(Path.Combine(install, RequiredFiles[0])));
        });
    }

    private static UpdateInstallRequest Request(string install, string payload, string backup) =>
        new(42, install, payload, backup, RequiredFiles[0], Path.Combine(Path.GetDirectoryName(backup)!, "result.json"));

    private static string CreateInstallation(string root)
    {
        var install = Path.Combine(root, "install");
        Directory.CreateDirectory(install);
        foreach (var file in RequiredFiles) File.WriteAllText(Path.Combine(install, file), "old-" + file);
        return install;
    }

    private static string CreateArchive(string root, IEnumerable<(string Name, string Content)> entries)
    {
        var archivePath = Path.Combine(root, $"{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        foreach (var item in entries)
        {
            var entry = archive.CreateEntry(item.Name);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(item.Content);
        }
        return archivePath;
    }

    private static string CreateValidApplicationArchive(string root)
    {
        var archivePath = Path.Combine(root, $"{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        foreach (var file in RequiredFiles)
        {
            var entry = archive.CreateEntry(file);
            using var target = entry.Open();
            if (file.EndsWith(".dll", StringComparison.Ordinal))
            {
                using var source = File.OpenRead(typeof(UpdateArchivePreparer).Assembly.Location);
                source.CopyTo(target);
            }
            else
            {
                using var writer = new StreamWriter(target);
                writer.Write("new-" + file);
            }
        }
        return archivePath;
    }

    private static void WithTemporaryDirectory(Action<string> action)
    {
        var path = Path.Combine(Path.GetTempPath(), "DisplayModeSwitcher.UpdateInstallerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try { action(path); }
        finally { Directory.Delete(path, recursive: true); }
    }

    private static void True(bool condition, string? message = null)
    {
        if (!condition) throw new InvalidOperationException(message ?? "Bedingung war nicht erfüllt.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Erwartet: {expected}; tatsächlich: {actual}");
    }
}
