using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using ModPulse.Models;
using ModPulse.Services;

namespace ModPulse.Tests;

/// <summary>
/// Tests for the GitHub Releases self-update check.
/// </summary>
public sealed class UpdateCheckServiceTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? RequestedUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestedUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private static string Release(string tag)
    {
        return $$"""{ "tag_name": "{{tag}}", "html_url": "https://github.com/RuKira/ModPulse/releases/tag/{{tag}}" }""";
    }

    [Theory]
    [InlineData("v1.1.0", "1.0.0", ModPulseUpdateStatus.UpdateAvailable)]
    [InlineData("1.1.0", "1.0.0", ModPulseUpdateStatus.UpdateAvailable)]
    [InlineData("v1.0.0", "1.0.0", ModPulseUpdateStatus.UpToDate)]
    [InlineData("v1.0.0", "1.2.0", ModPulseUpdateStatus.UpToDate)]
    [InlineData("nightly", "1.0.0", ModPulseUpdateStatus.Unavailable)]
    public void Interpret_compares_latest_release_tag_with_running_version(
        string tag,
        string current,
        ModPulseUpdateStatus expected
    )
    {
        var result = UpdateCheckService.Interpret(Release(tag), current);

        Assert.Equal(expected, result.Status);
        Assert.Equal(current, result.CurrentVersion);
    }

    [Fact]
    public void Interpret_links_to_the_release_page_when_an_update_exists()
    {
        var result = UpdateCheckService.Interpret(Release("v2.0.0"), "1.0.0");

        Assert.Equal("2.0.0", result.LatestVersion);
        Assert.Equal("https://github.com/RuKira/ModPulse/releases/tag/v2.0.0", result.DownloadLink);
    }

    [Fact]
    public async Task CheckAsync_queries_the_ModPulse_latest_release_endpoint()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Release("v99.0.0"));
        var service = new UpdateCheckService(new HttpClient(handler), NullLogger<UpdateCheckService>.Instance);

        var result = await service.CheckAsync(new SemanticVersioning.Version(4, 1, 6));

        Assert.Equal(ModPulseUpdateStatus.UpdateAvailable, result.Status);
        Assert.Equal("https://api.github.com/repos/RuKira/ModPulse/releases/latest", handler.RequestedUri?.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task CheckAsync_reports_unavailable_when_no_release_or_api_error(HttpStatusCode status)
    {
        var handler = new StubHandler(status, "{}");
        var service = new UpdateCheckService(new HttpClient(handler), NullLogger<UpdateCheckService>.Instance);

        var result = await service.CheckAsync(new SemanticVersioning.Version(4, 1, 6));

        Assert.Equal(ModPulseUpdateStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task CheckAsync_reports_unavailable_on_malformed_json()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "not json");
        var service = new UpdateCheckService(new HttpClient(handler), NullLogger<UpdateCheckService>.Instance);

        var result = await service.CheckAsync(new SemanticVersioning.Version(4, 1, 6));

        Assert.Equal(ModPulseUpdateStatus.Unavailable, result.Status);
    }
}
