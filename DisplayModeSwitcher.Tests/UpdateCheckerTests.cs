using System.Net;
using System.Text;
using DisplayModeSwitcher;

public static class UpdateCheckerTests
{
    public static void FindsNewestStableRelease()
    {
        const string json = """
        [
          { "tag_name": "v1.1.0", "name": "Preview", "html_url": "https://github.com/WhiskyGuerra/DisplayModeSwitcher/releases/tag/v1.1.0", "body": "Neu", "draft": false, "prerelease": false },
          { "tag_name": "v2.0.0-beta", "name": "Beta", "html_url": "https://github.com/WhiskyGuerra/DisplayModeSwitcher/releases/tag/v2.0.0-beta", "body": null, "draft": false, "prerelease": true },
          { "tag_name": "v9.0.0", "name": "Entwurf", "html_url": "https://github.com/WhiskyGuerra/DisplayModeSwitcher/releases/tag/v9.0.0", "body": null, "draft": true, "prerelease": false }
        ]
        """;
        var handler = new FakeHandler(HttpStatusCode.OK, json);
        using var http = new HttpClient(handler);
        var result = new GitHubReleaseUpdateChecker(http).CheckAsync(new Version(1, 0, 0, 0)).GetAwaiter().GetResult();

        Expect(result.State == UpdateCheckState.UpdateAvailable && result.Release?.Version == new Version(1, 1, 0, 0));
        Expect(handler.Request?.RequestUri?.Host == "api.github.com");
        Expect(handler.Request?.Headers.UserAgent.ToString().Contains("DisplayModeSwitcher", StringComparison.Ordinal) == true);
        Expect(handler.Request?.Headers.Contains("X-GitHub-Api-Version") == true);
    }

    public static void IgnoresInvalidAndOlderReleases()
    {
        const string json = """
        [
          { "tag_name": "release-next", "name": "Ungültig", "html_url": "https://github.com/example", "body": null, "draft": false, "prerelease": false },
          { "tag_name": "v1.0.0", "name": "Aktuell", "html_url": "https://github.com/WhiskyGuerra/DisplayModeSwitcher/releases/tag/v1.0.0", "body": null, "draft": false, "prerelease": false }
        ]
        """;
        using var http = new HttpClient(new FakeHandler(HttpStatusCode.OK, json));
        var result = new GitHubReleaseUpdateChecker(http).CheckAsync(new Version(1, 0, 0, 0)).GetAwaiter().GetResult();
        Expect(result.State == UpdateCheckState.UpToDate);
        Expect(!GitHubReleaseUpdateChecker.TryParseReleaseVersion("v2.0.0-beta", out _));
    }

    public static void ReportsApiFailuresWithoutThrowing()
    {
        using var http = new HttpClient(new FakeHandler(HttpStatusCode.Forbidden, "{}"));
        var result = new GitHubReleaseUpdateChecker(http).CheckAsync(new Version(1, 0)).GetAwaiter().GetResult();
        Expect(result.State == UpdateCheckState.Failed && result.Error!.Contains("Abfragelimit", StringComparison.Ordinal));

        using var missingHttp = new HttpClient(new FakeHandler(HttpStatusCode.NotFound, "{}"));
        var missing = new GitHubReleaseUpdateChecker(missingHttp).CheckAsync(new Version(1, 0)).GetAwaiter().GetResult();
        Expect(missing.State == UpdateCheckState.Failed && missing.Error!.Contains("möglicherweise privat", StringComparison.Ordinal));
    }

    public static void DistinguishesMissingStableRelease()
    {
        const string json = """
        [
          { "tag_name": "v2.0.0-beta", "name": "Beta", "html_url": "https://github.com/WhiskyGuerra/DisplayModeSwitcher/releases/tag/v2.0.0-beta", "body": null, "draft": false, "prerelease": true }
        ]
        """;
        using var http = new HttpClient(new FakeHandler(HttpStatusCode.OK, json));
        var result = new GitHubReleaseUpdateChecker(http).CheckAsync(new Version(1, 0)).GetAwaiter().GetResult();
        Expect(result.State == UpdateCheckState.NoPublishedRelease);
    }

    private static void Expect(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Erwartung nicht erfüllt.");
    }

    private sealed class FakeHandler(HttpStatusCode statusCode, string response) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            });
        }
    }
}
