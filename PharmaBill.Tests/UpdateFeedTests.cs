using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PharmaBill.App.Services;
using PharmaBill.Data.Persistence;
using Xunit;

namespace PharmaBill.Tests;

// The Local Market edition's update check: https://pharmaremit.com/updates/latest.json, every field untrusted.
public sealed class UpdateFeedTests : IDisposable
{
    private const string Good = """
        {
          "version": "1.0.13",
          "released": "2026-11-05",
          "downloadUrl": "https://pharmaremit.com/download/PharmaRemit_Setup_1.0.13.0.exe",
          "sizeBytes": 79253601,
          "sha256": "6F1C2B0D9E8A7C6B5A4F3E2D1C0B9A8F7E6D5C4B3A2F1E0D9C8B7A6F5E4D3C2B",
          "minimumSupportedVersion": "1.0.10",
          "notes": ["Faster stock search", "GSTR-1 includes credit notes"]
        }
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"PharmaBill.UpdateFeed.{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // Versions compare number by number, never as text.
    [Theory]
    [InlineData("1.0.10", "1.0.9", 1)]
    [InlineData("1.0.9", "1.0.10", -1)]
    [InlineData("1.0.12", "1.0.12.0", 0)]
    [InlineData("1.0.12.0", "1.0.12", 0)]
    [InlineData("1.0.12.1", "1.0.12", 1)]
    [InlineData("2", "1.99.99.99", 1)]
    [InlineData("1.10.0", "1.9.9", 1)]
    [InlineData("1.0.0100", "1.0.99", 1)]
    [InlineData("0.0.0", "0", 0)]
    public void Versions_CompareComponentByComponent(string left, string right, int expected)
    {
        Assert.Equal(expected, Math.Sign(UpdateFeed.CompareVersions(left, right)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("v1.0.13")]
    [InlineData("1.0.13-beta")]
    [InlineData("1..13")]
    [InlineData("1.0.13.")]
    [InlineData("1.0.1.2.3")]
    [InlineData("-1.0.0")]
    [InlineData("1.0.+3")]
    [InlineData("1.0.٣")]
    [InlineData("99999999999.0")]
    public void NotAVersion_IsRejected(string text)
    {
        Assert.False(UpdateFeed.TryParseVersion(text, out _));
        Assert.False(UpdateFeed.IsNewer(text, "1.0.0"));
        Assert.Throws<FormatException>(() => UpdateFeed.CompareVersions(text, "1.0.0"));
    }

    [Fact]
    public void AGoodFile_IsRead()
    {
        PublishedRelease release = Assert.IsType<PublishedRelease>(UpdateFeed.Parse(Good));
        Assert.Equal("1.0.13", release.Version);
        Assert.Equal(new DateOnly(2026, 11, 5), release.Released);
        Assert.Equal(79253601, release.SizeBytes);
        Assert.Equal("75.6 MB", release.SizeText);
        Assert.Equal("5 Nov 2026", release.ReleasedText);
        Assert.Equal(64, release.Sha256.Length);
        Assert.Equal(["Faster stock search", "GSTR-1 includes credit notes"], release.Notes);
    }

    // Missing, malformed, any field absent or of the wrong kind: exactly as if there were no update.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("\"1.0.13\"")]
    [InlineData("<html>404</html>")]
    public void AFileThatIsNotTheFeed_MeansNoUpdate(string? text)
    {
        Assert.Null(UpdateFeed.Parse(text));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("released")]
    [InlineData("downloadUrl")]
    [InlineData("sizeBytes")]
    [InlineData("sha256")]
    [InlineData("minimumSupportedVersion")]
    [InlineData("notes")]
    public void AnyFieldMissing_MeansNoUpdate(string field)
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(Good)!.AsObject();
        json.Remove(field);
        Assert.Null(UpdateFeed.Parse(json.ToJsonString()));
    }

    [Theory]
    [InlineData("version", "\"v1.0.13\"")]
    [InlineData("version", "1.13")]
    [InlineData("released", "\"next week\"")]
    [InlineData("released", "20261105")]
    [InlineData("downloadUrl", "\"http://pharmaremit.com/download/setup.exe\"")]
    [InlineData("downloadUrl", "\"ftp://pharmaremit.com/setup.exe\"")]
    [InlineData("downloadUrl", "\"file:///C:/Windows/System32/cmd.exe\"")]
    [InlineData("downloadUrl", "\"https://user:pass@pharmaremit.com/setup.exe\"")]
    [InlineData("downloadUrl", "\"/download/setup.exe\"")]
    [InlineData("downloadUrl", "\"javascript:alert(1)\"")]
    [InlineData("sizeBytes", "\"79253601\"")]
    [InlineData("sizeBytes", "0")]
    [InlineData("sizeBytes", "-5")]
    [InlineData("sizeBytes", "1.5")]
    [InlineData("sizeBytes", "99999999999999")]
    [InlineData("sha256", "\"abc\"")]
    [InlineData("sha256", "\"ZZ1C2B0D9E8A7C6B5A4F3E2D1C0B9A8F7E6D5C4B3A2F1E0D9C8B7A6F5E4D3C2B\"")]
    [InlineData("minimumSupportedVersion", "\"latest\"")]
    [InlineData("notes", "\"Faster\"")]
    [InlineData("notes", "[1, 2]")]
    [InlineData("notes", "null")]
    public void AnyFieldOfTheWrongKind_MeansNoUpdate(string field, string value)
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(Good)!.AsObject();
        json[field] = System.Text.Json.Nodes.JsonNode.Parse(value);
        Assert.Null(UpdateFeed.Parse(json.ToJsonString()));
    }

    [Fact]
    public void Notes_AreShownAsPlainText_AndKeptShort()
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(Good)!.AsObject();
        json["notes"] = System.Text.Json.Nodes.JsonNode.Parse("[\"  line\\u0007 one \", \"\", \"" + new string('x', 1000) + "\"]");
        PublishedRelease release = UpdateFeed.Parse(json.ToJsonString())!;
        Assert.Equal("line one", release.Notes[0]);
        Assert.Equal(2, release.Notes.Count);
        Assert.True(release.Notes[1].Length <= 401);
        Assert.Null(UpdateFeed.Parse(new string(' ', UpdateFeed.MaxBytes) + Good));
    }

    // ---- The service: one plain GET, five seconds at most, at most once a day, nothing on screen when offline. ----

    private sealed class Server(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return answer(request, cancellationToken);
        }

        public static Server Answering(HttpStatusCode status, string body = "") =>
            new((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }));
    }

    private DateTimeOffset _now = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);

    private WebsiteUpdateService Service(Server server, string current = "1.0.12.0", List<string>? opened = null)
    {
        var service = new WebsiteUpdateService(current, new UpdateCheckPreferencesStore(new DatabaseStorageOptions(_root)), NullLogger<WebsiteUpdateService>.Instance, server, () => _now);
        if (opened != null)
        {
            service.OpenInBrowser = opened.Add;
        }
        return service;
    }

    [Fact]
    public async Task ANewerVersion_IsAvailable_AndOnlyItsDownloadPageIsOpened()
    {
        Server server = Server.Answering(HttpStatusCode.OK, Good);
        var opened = new List<string>();
        using WebsiteUpdateService service = Service(server, opened: opened);

        AppUpdateAvailability result = await service.CheckForUpdatesAsync();

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal(UpdateChannel.Website, result.Channel);
        Assert.Equal("1.0.13", result.Release!.Version);
        Assert.Contains("1.0.13", result.StatusMessage);
        // A plain GET of the static file: nothing about this shop or PC is sent.
        HttpRequestMessage request = Assert.Single(server.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(UpdateFeed.Url, request.RequestUri!.ToString());
        Assert.Empty(request.Headers);
        Assert.Null(request.Content);

        await service.LaunchUpdateAsync();
        Assert.Equal(["https://pharmaremit.com/download/PharmaRemit_Setup_1.0.13.0.exe"], opened);
    }

    [Theory]
    [InlineData("1.0.13")]
    [InlineData("1.0.13.0")]
    [InlineData("1.0.14")]
    public async Task TheSameOrAnOlderVersion_IsNoUpdate(string current)
    {
        using WebsiteUpdateService service = Service(Server.Answering(HttpStatusCode.OK, Good), current);
        AppUpdateAvailability result = await service.CheckForUpdatesAsync();
        Assert.False(result.IsUpdateAvailable);
        Assert.Equal(UpdateChannel.Website, result.Channel);
        Assert.StartsWith("You're on the latest version", result.StatusMessage);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "")]
    [InlineData(HttpStatusCode.OK, "{ \"version\": \"9.9.9\" }")]
    [InlineData(HttpStatusCode.OK, "<html>Coming soon</html>")]
    public async Task AMissingOrMalformedFile_IsNoUpdate(HttpStatusCode status, string body)
    {
        var opened = new List<string>();
        using WebsiteUpdateService service = Service(Server.Answering(status, body), opened: opened);
        AppUpdateAvailability result = await service.CheckForUpdatesAsync(force: true);
        Assert.False(result.IsUpdateAvailable);
        Assert.Null(result.Release);
        Assert.Equal(UpdateChannel.Website, result.Channel);
        await service.LaunchUpdateAsync();
        Assert.Empty(opened);
    }

    [Fact]
    public async Task Offline_SaysCouldNotReach_AndShowsNoUpdate()
    {
        using WebsiteUpdateService service = Service(new Server((_, _) => throw new HttpRequestException("No such host is known.")));
        AppUpdateAvailability result = await service.CheckForUpdatesAsync(force: true);
        Assert.False(result.IsUpdateAvailable);
        Assert.Equal(UpdateChannel.None, result.Channel);
        Assert.StartsWith("Could not reach the update server", result.StatusMessage);

        using WebsiteUpdateService failing = Service(Server.Answering(HttpStatusCode.InternalServerError));
        Assert.Equal(UpdateChannel.None, (await failing.CheckForUpdatesAsync(force: true)).Channel);
    }

    [Fact]
    public async Task AServerThatDoesNotAnswer_IsGivenUpOnAfterFiveSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), WebsiteUpdateService.Timeout);
        using WebsiteUpdateService service = Service(new Server(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromMinutes(5), token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        AppUpdateAvailability result = await service.CheckForUpdatesAsync(force: true);
        Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(9));
        Assert.Equal(UpdateChannel.None, result.Channel);
    }

    [Fact]
    public async Task TheServerIsAskedAtMostOnceADay_UnlessTheUserAsks_AndTheTimeIsKept()
    {
        Server server = Server.Answering(HttpStatusCode.OK, Good);
        using (WebsiteUpdateService first = Service(server))
        {
            Assert.True((await first.CheckForUpdatesAsync()).IsUpdateAvailable);
        }

        // A later start the same day: no request, and the update found earlier still shows.
        _now = _now.AddHours(23);
        using (WebsiteUpdateService again = Service(server))
        {
            AppUpdateAvailability cached = await again.CheckForUpdatesAsync();
            Assert.True(cached.IsUpdateAvailable);
            Assert.Equal("1.0.13", cached.Release!.Version);
            Assert.Single(server.Requests);

            // "Check for updates" in About ignores the limit.
            await again.CheckForUpdatesAsync(force: true);
            Assert.Equal(2, server.Requests.Count);
        }

        _now = _now.AddHours(25);
        using (WebsiteUpdateService nextDay = Service(server))
        {
            await nextDay.CheckForUpdatesAsync();
            Assert.Equal(3, server.Requests.Count);
        }
        Assert.Contains("LastCheckedUtc", File.ReadAllText(Path.Combine(_root, "update-check-preferences.json")));
    }

    [Fact]
    public async Task ADismissedVersion_StaysHidden_UntilALaterOne()
    {
        using WebsiteUpdateService service = Service(Server.Answering(HttpStatusCode.OK, Good));
        await service.CheckForUpdatesAsync();
        service.Dismiss("1.0.13");
        Assert.True(service.IsDismissed("1.0.13"));
        Assert.True(service.IsDismissed("1.0.13.0"));
        Assert.False(service.IsDismissed("1.0.14"));
    }
}
