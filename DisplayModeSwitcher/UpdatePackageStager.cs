using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace DisplayModeSwitcher;

public sealed record UpdateStageResult(
    bool Success,
    string? StagingDirectory = null,
    string? ArchivePath = null,
    string? Sha256 = null,
    string? Error = null);

public interface IUpdatePackageStager
{
    Task<UpdateStageResult> StageAsync(UpdatePackage package, Version version, CancellationToken cancellationToken = default);
}

/// <summary>
/// Lädt ein Release-Paket in einen neuen, anwendungseigenen Staging-Ordner.
/// Es wird weder entpackt noch in die laufende Installation geschrieben.
/// </summary>
public sealed class UpdatePackageStager : IUpdatePackageStager
{
    internal const long MaximumArchiveBytes = 100L * 1024 * 1024;
    internal const long MaximumChecksumBytes = 4096;
    private static readonly HashSet<string> AllowedDownloadHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "github.com",
        "release-assets.githubusercontent.com",
        "objects.githubusercontent.com"
    };

    private readonly HttpClient _httpClient;
    private readonly string _updatesRoot;

    public UpdatePackageStager(HttpClient httpClient, string updatesRoot)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (string.IsNullOrWhiteSpace(updatesRoot)) throw new ArgumentException("Der Update-Stagingpfad fehlt.", nameof(updatesRoot));
        _updatesRoot = Path.GetFullPath(updatesRoot);
    }

    public async Task<UpdateStageResult> StageAsync(UpdatePackage package, Version version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(version);
        if (package.ArchiveSize <= 0 || package.ArchiveSize > MaximumArchiveBytes)
            return Failed($"Das Update-Paket überschreitet das erlaubte Größenlimit von {MaximumArchiveBytes / 1024 / 1024} MB.");
        if (package.ChecksumSize <= 0 || package.ChecksumSize > MaximumChecksumBytes)
            return Failed("Die veröffentlichte Prüfsummendatei besitzt eine unzulässige Größe.");
        if (!IsAllowedInitialUri(package.ArchiveUri) || !IsAllowedInitialUri(package.ChecksumUri))
            return Failed("Mindestens eine Update-Adresse stammt nicht vom erwarteten GitHub-Host.");

        string? stagingDirectory = null;
        try
        {
            Directory.CreateDirectory(_updatesRoot);
            stagingDirectory = Path.Combine(_updatesRoot, $"{FormatVersion(version)}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(stagingDirectory);

            var checksumDownload = await DownloadTextAsync(package.ChecksumUri, MaximumChecksumBytes, cancellationToken).ConfigureAwait(false);
            if (checksumDownload.Bytes != package.ChecksumSize)
                return CleanupAndFail(stagingDirectory, "Die Größe der geladenen Prüfsummendatei stimmt nicht mit den GitHub-Metadaten überein.");
            if (!TryParseSha256(checksumDownload.Text, out var expectedHash))
                return CleanupAndFail(stagingDirectory, "Die veröffentlichte SHA-256-Prüfsumme ist ungültig.");

            var archivePath = Path.Combine(stagingDirectory, GitHubReleaseUpdateChecker.ArchiveAssetName);
            var archiveBytes = await DownloadFileAsync(package.ArchiveUri, archivePath, MaximumArchiveBytes, cancellationToken).ConfigureAwait(false);
            if (archiveBytes != package.ArchiveSize)
                return CleanupAndFail(stagingDirectory, "Die Größe des geladenen Update-Pakets stimmt nicht mit den GitHub-Metadaten überein.");
            string actualHash;
            await using (var archive = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
                actualHash = Convert.ToHexString(await SHA256.HashDataAsync(archive, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expectedHash), Encoding.ASCII.GetBytes(actualHash)))
                return CleanupAndFail(stagingDirectory, "Die SHA-256-Prüfsumme des Downloads stimmt nicht mit dem Release überein.");

            await File.WriteAllTextAsync(Path.Combine(stagingDirectory, GitHubReleaseUpdateChecker.ChecksumAssetName), expectedHash + Environment.NewLine, cancellationToken).ConfigureAwait(false);
            return new(true, stagingDirectory, archivePath, actualHash);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CleanupAndFail(stagingDirectory, "Der Update-Download hat zu lange gedauert.");
        }
        catch (OperationCanceledException)
        {
            Cleanup(stagingDirectory);
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            return CleanupAndFail(stagingDirectory, $"Das Update konnte nicht sicher bereitgestellt werden: {ex.Message}");
        }
    }

    internal static bool TryParseSha256(string? text, out string hash)
    {
        hash = string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var token = text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (token is null || token.Length != 64 || token.Any(character => !Uri.IsHexDigit(character))) return false;
        hash = token.ToLowerInvariant();
        return true;
    }

    private async Task<(string Text, long Bytes)> DownloadTextAsync(Uri uri, long maximumBytes, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(uri, maximumBytes, cancellationToken).ConfigureAwait(false);
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var bytes = await CopyLimitedAsync(input, output, maximumBytes, cancellationToken).ConfigureAwait(false);
        return (Encoding.UTF8.GetString(output.ToArray()), bytes);
    }

    private async Task<long> DownloadFileAsync(Uri uri, string destination, long maximumBytes, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(uri, maximumBytes, cancellationToken).ConfigureAwait(false);
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await CopyLimitedAsync(input, output, maximumBytes, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, long maximumBytes, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        request.Headers.UserAgent.ParseAdd("DisplayModeSwitcher-Updater/1.0");
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            throw new HttpRequestException($"GitHub antwortete mit HTTP-Status {(int)response.StatusCode}.");
        }
        if (response.RequestMessage?.RequestUri is not { } finalUri || !IsAllowedFinalUri(finalUri))
        {
            response.Dispose();
            throw new HttpRequestException("Der GitHub-Download wurde auf einen nicht erlaubten Host umgeleitet.");
        }
        if (response.Content.Headers.ContentLength is { } length && length > maximumBytes)
        {
            response.Dispose();
            throw new IOException("Der Download überschreitet das erlaubte Größenlimit.");
        }
        return response;
    }

    private static async Task<long> CopyLimitedAsync(Stream input, Stream output, long maximumBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) return total;
            total += read;
            if (total > maximumBytes) throw new IOException("Der Download überschreitet das erlaubte Größenlimit.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsAllowedInitialUri(Uri uri) =>
        uri.IsAbsoluteUri
        && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase);

    private static bool IsAllowedFinalUri(Uri uri) =>
        uri.IsAbsoluteUri
        && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && (AllowedDownloadHosts.Contains(uri.Host) || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    private static string FormatVersion(Version version) => $"{version.Major}.{Math.Max(0, version.Minor)}.{Math.Max(0, version.Build)}.{Math.Max(0, version.Revision)}";
    private static UpdateStageResult Failed(string error) => new(false, Error: error);
    private static UpdateStageResult CleanupAndFail(string? directory, string error)
    {
        Cleanup(directory);
        return Failed(error);
    }

    private static void Cleanup(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;
        try { Directory.Delete(directory, recursive: true); }
        catch { }
    }
}
