using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DisplayModeSwitcher;

public enum UpdateCheckState
{
    NoPublishedRelease,
    UpToDate,
    UpdateAvailable,
    Failed
}

public sealed record AvailableRelease(
    string TagName,
    string DisplayName,
    Version Version,
    Uri ReleasePage,
    string? Notes);

public sealed record UpdateCheckResult(
    UpdateCheckState State,
    AvailableRelease? Release = null,
    string? Error = null);

public interface IUpdateChecker
{
    Task<UpdateCheckResult> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default);
}

/// <summary>
/// Liest ausschließlich öffentliche Release-Metadaten. Downloads und das
/// Ersetzen lokaler Dateien gehören absichtlich nicht in diese Komponente.
/// </summary>
public sealed class GitHubReleaseUpdateChecker : IUpdateChecker
{
    private const string ReleasesEndpoint = "https://api.github.com/repos/WhiskyGuerra/DisplayModeSwitcher/releases?per_page=20";
    private readonly HttpClient _httpClient;

    public GitHubReleaseUpdateChecker(HttpClient httpClient) =>
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public async Task<UpdateCheckResult> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesEndpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.ParseAdd("DisplayModeSwitcher-UpdateCheck/1.0");
            request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new(UpdateCheckState.Failed, Error: DescribeHttpError(response.StatusCode));

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var releases = await JsonSerializer.DeserializeAsync<GitHubRelease[]>(stream, cancellationToken: cancellationToken).ConfigureAwait(false) ?? [];
            var latest = releases
                .Where(item => !item.Draft && !item.Prerelease && TryParseReleaseVersion(item.TagName, out _))
                .Select(item => (Release: item, Version: ParseReleaseVersion(item.TagName)))
                .OrderByDescending(item => item.Version)
                .FirstOrDefault();

            if (latest.Release is null)
                return new(UpdateCheckState.NoPublishedRelease);
            if (latest.Version <= Normalize(currentVersion))
                return new(UpdateCheckState.UpToDate);
            if (!Uri.TryCreate(latest.Release.HtmlUrl, UriKind.Absolute, out var page)
                || !string.Equals(page.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(page.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                return new(UpdateCheckState.Failed, Error: "GitHub lieferte für das Release keine sichere Projektseite.");

            return new(UpdateCheckState.UpdateAvailable, new AvailableRelease(
                latest.Release.TagName,
                string.IsNullOrWhiteSpace(latest.Release.Name) ? latest.Release.TagName : latest.Release.Name.Trim(),
                latest.Version,
                page,
                latest.Release.Body));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(UpdateCheckState.Failed, Error: "Die Updateprüfung hat zu lange gedauert.");
        }
        catch (HttpRequestException ex)
        {
            return new(UpdateCheckState.Failed, Error: $"GitHub konnte nicht erreicht werden: {ex.Message}");
        }
        catch (JsonException)
        {
            return new(UpdateCheckState.Failed, Error: "GitHub lieferte eine unlesbare Release-Antwort.");
        }
    }

    internal static bool TryParseReleaseVersion(string? tagName, out Version version)
    {
        version = new Version();
        if (string.IsNullOrWhiteSpace(tagName)) return false;
        var value = tagName.Trim();
        if (value.StartsWith('v') || value.StartsWith('V')) value = value[1..];
        if (value.Contains('-', StringComparison.Ordinal) || value.Contains('+', StringComparison.Ordinal)) return false;
        if (!Version.TryParse(value, out var parsed) || parsed.Major < 0 || parsed.Minor < 0) return false;
        version = Normalize(parsed);
        return true;
    }

    private static Version ParseReleaseVersion(string tagName)
    {
        _ = TryParseReleaseVersion(tagName, out var version);
        return version;
    }

    private static Version Normalize(Version version) => new(
        version.Major,
        Math.Max(0, version.Minor),
        Math.Max(0, version.Build),
        Math.Max(0, version.Revision));

    private static string DescribeHttpError(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.NotFound => "GitHub stellt für das Projekt keine öffentlichen Release-Daten bereit. Das Repository oder Release ist möglicherweise privat, nicht vorhanden oder noch nicht veröffentlicht.",
        HttpStatusCode.Forbidden => "GitHub hat die Updateprüfung abgelehnt oder das anonyme Abfragelimit ist erreicht.",
        _ => $"Die GitHub-Updateprüfung ist mit HTTP-Status {(int)statusCode} fehlgeschlagen."
    };

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease);
}
