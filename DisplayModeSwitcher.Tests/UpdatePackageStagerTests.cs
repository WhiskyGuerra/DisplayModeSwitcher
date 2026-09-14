using System.Net;
using System.Security.Cryptography;
using System.Text;
using DisplayModeSwitcher;

public static class UpdatePackageStagerTests
{
    public static void ValidPackageIsStagedAfterHashVerification()
    {
        WithTemporaryDirectory(root =>
        {
            var archive = Encoding.UTF8.GetBytes("sicheres Test-ZIP");
            var hash = Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant();
            var checksum = Encoding.UTF8.GetBytes($"{hash}  DisplayModeSwitcher-win-x64.zip\n");
            using var http = new HttpClient(new AssetHandler(archive, checksum));
            var result = new UpdatePackageStager(http, root).StageAsync(Package(archive.Length, checksum.Length), new Version(1, 2, 3)).GetAwaiter().GetResult();

            Expect(result.Success && result.ArchivePath is not null && File.Exists(result.ArchivePath));
            Expect(result.Sha256 == hash);
            Expect(File.ReadAllBytes(result.ArchivePath!).SequenceEqual(archive));
            Expect(Directory.GetDirectories(root).Length == 1);
        });
    }

    public static void HashMismatchRemovesIncompleteStaging()
    {
        WithTemporaryDirectory(root =>
        {
            var archive = Encoding.UTF8.GetBytes("manipuliert");
            var wrongHash = new string('a', 64);
            using var http = new HttpClient(new AssetHandler(archive, Encoding.ASCII.GetBytes(wrongHash)));
            var result = new UpdatePackageStager(http, root).StageAsync(Package(archive.Length, 64), new Version(1, 2, 3)).GetAwaiter().GetResult();

            Expect(!result.Success && result.Error!.Contains("stimmt nicht", StringComparison.Ordinal));
            Expect(!Directory.EnumerateFileSystemEntries(root).Any());
        });
    }

    public static void RejectsUnsafeMetadataBeforeDownload()
    {
        WithTemporaryDirectory(root =>
        {
            var handler = new AssetHandler([], []);
            using var http = new HttpClient(handler);
            var oversized = new UpdatePackage(
                new Uri("https://github.com/example/archive.zip"),
                UpdatePackageStager.MaximumArchiveBytes + 1,
                new Uri("https://github.com/example/archive.zip.sha256"), 64);
            var result = new UpdatePackageStager(http, root).StageAsync(oversized, new Version(2, 0)).GetAwaiter().GetResult();
            Expect(!result.Success && handler.RequestCount == 0);

            var unsafeHost = oversized with { ArchiveSize = 10, ArchiveUri = new Uri("https://example.com/archive.zip") };
            result = new UpdatePackageStager(http, root).StageAsync(unsafeHost, new Version(2, 0)).GetAwaiter().GetResult();
            Expect(!result.Success && handler.RequestCount == 0);
        });
    }

    public static void RejectsDownloadedSizeThatDiffersFromMetadata()
    {
        WithTemporaryDirectory(root =>
        {
            var archive = Encoding.UTF8.GetBytes("zu kurz");
            var hash = Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant();
            var checksum = Encoding.ASCII.GetBytes(hash);
            using var http = new HttpClient(new AssetHandler(archive, checksum));
            var result = new UpdatePackageStager(http, root).StageAsync(Package(archive.Length + 1, checksum.Length), new Version(1, 2, 3)).GetAwaiter().GetResult();

            Expect(!result.Success && result.Error!.Contains("GitHub-Metadaten", StringComparison.Ordinal));
            Expect(!Directory.EnumerateFileSystemEntries(root).Any());
        });
    }

    public static void ParsesOnlyValidSha256Values()
    {
        var expected = new string('F', 64);
        Expect(UpdatePackageStager.TryParseSha256($"{expected}  package.zip", out var parsed) && parsed == expected.ToLowerInvariant());
        Expect(!UpdatePackageStager.TryParseSha256("1234", out _));
        Expect(!UpdatePackageStager.TryParseSha256(new string('g', 64), out _));
    }

    private static UpdatePackage Package(long archiveSize, long checksumSize) => new(
        new Uri("https://github.com/WhiskyGuerra/DisplayModeSwitcher/releases/download/v1.2.3/DisplayModeSwitcher-win-x64.zip"), archiveSize,
        new Uri("https://github.com/WhiskyGuerra/DisplayModeSwitcher/releases/download/v1.2.3/DisplayModeSwitcher-win-x64.zip.sha256"), checksumSize);

    private static void WithTemporaryDirectory(Action<string> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "DisplayModeSwitcher.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { test(root); }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static void Expect(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Erwartung nicht erfüllt.");
    }

    private sealed class AssetHandler(byte[] archive, byte[] checksum) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var content = request.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.Ordinal)
                ? checksum
                : archive;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(content)
            });
        }
    }
}
