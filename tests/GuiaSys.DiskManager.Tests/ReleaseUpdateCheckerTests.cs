using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using GuiaSys.DiskManager.Updates;

namespace GuiaSys.DiskManager.Tests;

public sealed class ReleaseUpdateCheckerTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }

    private static string Release(string version = "0.2.0", bool draft = false, bool prerelease = false,
        string? page = null, string? assetUrl = null, string? installerName = null, object? size = null)
    {
        var tag = "v" + version;
        var filename = installerName ?? $"GuiaSys-DiskManager-Setup-{version}-x64.exe";
        return JsonSerializer.Serialize(new
        {
            tag_name = tag, draft, prerelease,
            html_url = page ?? $"https://github.com/guiasysstudio/GuiaSys-DiskManager/releases/tag/{tag}",
            assets = new[]
            {
                new { name = filename, state = "uploaded",
                    size = size ?? (object)61_000_000L,
                    browser_download_url = assetUrl ??
                        $"https://github.com/guiasysstudio/GuiaSys-DiskManager/releases/download/{tag}/{filename}" }
            }
        });
    }
    private static ReleaseUpdateChecker Checker(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(new HttpClient(new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(ReleaseUpdateChecker.MetadataEndpoint, request.RequestUri);
            Assert.True(request.Headers.Contains("User-Agent"));
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        })));

    [Fact]
    public async Task New_public_release_with_expected_installer_returns_only_verified_page()
    {
        var result = await Checker(Release()).CheckAsync(new Version(0, 1, 0, 0));
        Assert.Equal(UpdateCheckState.Available, result.State);
        Assert.Equal("0.2.0", result.LatestVersion);
        Assert.Equal("https://github.com/guiasysstudio/GuiaSys-DiskManager/releases/tag/v0.2.0",
            result.ReleasePage?.AbsoluteUri);
    }

    [Theory]
    [InlineData("0.1.0", "0.1.0", UpdateCheckState.UpToDate)]
    [InlineData("0.1.0", "0.1.1", UpdateCheckState.Available)]
    [InlineData("0.1.9", "0.2.0", UpdateCheckState.Available)]
    [InlineData("0.9.9", "1.0.0", UpdateCheckState.Available)]
    [InlineData("1.0.0", "0.9.9", UpdateCheckState.UpToDate)]
    public async Task Comparison_is_numeric_not_string_based(string local, string newest, UpdateCheckState expected)
    {
        var result = await Checker(Release(newest)).CheckAsync(Version.Parse(local));
        Assert.Equal(expected, result.State);
    }

    [Theory]
    [InlineData("v0.2.0-rc1")]
    [InlineData("v0.2")]
    [InlineData("0.2.0")]
    [InlineData("v00.2.0")]
    [InlineData("v999999999999999999999.0.0")]
    [InlineData("v0.2.0/malware")]
    public async Task Invalid_tag_versions_fail_closed(string tag)
    {
        var json = JsonSerializer.Serialize(new
        {
            tag_name = tag, draft = false, prerelease = false,
            html_url = $"https://github.com/guiasysstudio/GuiaSys-DiskManager/releases/tag/{tag}",
            assets = Array.Empty<object>()
        });
        Assert.Equal(UpdateCheckState.Unavailable,
            (await Checker(json).CheckAsync(new Version(0, 1, 0))).State);
    }

    [Fact]
    public async Task Prereleases_and_drafts_are_not_misrepresented_as_stable()
    {
        Assert.Equal(UpdateCheckState.Unavailable,
            (await Checker(Release(prerelease: true)).CheckAsync(new Version(0, 1, 0))).State);
        Assert.Equal(UpdateCheckState.Unavailable,
            (await Checker(Release(draft: true)).CheckAsync(new Version(0, 1, 0))).State);
    }

    [Theory]
    [InlineData("https://example.com/installer")]
    [InlineData("http://github.com/guiasysstudio/GuiaSys-DiskManager/releases/download/v0.2.0/GuiaSys-DiskManager-Setup-0.2.0-x64.exe")]
    [InlineData("https://github.com/attacker/other/releases/download/v0.2.0/GuiaSys-DiskManager-Setup-0.2.0-x64.exe")]
    public async Task Unexpected_installer_origin_is_rejected(string assetUrl)
    {
        var result = await Checker(Release(assetUrl: assetUrl)).CheckAsync(new Version(0, 1, 0));
        Assert.Equal(UpdateCheckState.Unavailable, result.State);
        Assert.Null(result.ReleasePage);
    }

    [Theory]
    [InlineData("https://example.com/releases/tag/v0.2.0")]
    [InlineData("http://github.com/guiasysstudio/GuiaSys-DiskManager/releases/tag/v0.2.0")]
    [InlineData("https://github.com/guiasysstudio/GuiaSys-DiskManager/releases/tag/v0.2.0?download=1")]
    public async Task Untrusted_browser_destination_is_rejected(string page)
    {
        var result = await Checker(Release(page: page)).CheckAsync(new Version(0, 1, 0));
        Assert.Equal(UpdateCheckState.Unavailable, result.State);
        Assert.Null(result.ReleasePage);
    }

    [Fact]
    public async Task Missing_or_tiny_installer_is_incomplete_release()
    {
        var wrong = await Checker(Release(installerName: "random-setup.exe"))
            .CheckAsync(new Version(0, 1, 0));
        Assert.Equal(UpdateCheckState.Unavailable, wrong.State);
        var tiny = await Checker(Release(size: 400L)).CheckAsync(new Version(0, 1, 0));
        Assert.Equal(UpdateCheckState.Unavailable, tiny.State);
    }

    [Fact]
    public async Task Malformed_installer_size_must_fail_closed_without_throwing()
    {
        var result = await Checker(Release(size: "not a number")).CheckAsync(new Version(0, 1, 0));
        Assert.Equal(UpdateCheckState.Unavailable, result.State);
    }

    [Fact]
    public async Task No_public_release_is_explicit_about_private_repo_limitations()
    {
        var result = await Checker("{}",
            HttpStatusCode.NotFound).CheckAsync(new Version(0, 1, 0));
        Assert.Equal(UpdateCheckState.NotPublished, result.State);
        Assert.Contains("privado", result.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Auth_rate_limit_redirect_and_server_failures_are_unavailable(HttpStatusCode status)
    {
        var result = await Checker(Release(), status).CheckAsync(new Version(0, 1, 0));
        Assert.Equal(UpdateCheckState.Unavailable, result.State);
        Assert.Null(result.ReleasePage);
    }

    [Fact]
    public async Task Malformed_json_and_oversized_metadata_do_not_surface_as_updates()
    {
        Assert.Equal(UpdateCheckState.Unavailable,
            (await Checker("{broken").CheckAsync(new Version(0, 1, 0))).State);
        Assert.Equal(UpdateCheckState.Unavailable,
            (await Checker(new string('x', 128 * 1024 + 1))
                .CheckAsync(new Version(0, 1, 0))).State);
    }

    [Fact]
    public async Task Network_errors_and_cancellation_leave_no_executable_action()
    {
        var fail = new ReleaseUpdateChecker(new HttpClient(new Handler(_ => throw new HttpRequestException("offline"))));
        var result = await fail.CheckAsync(new Version(0, 1, 0));
        Assert.Equal(UpdateCheckState.Unavailable, result.State);
        Assert.Null(result.ReleasePage);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Equal(UpdateCheckState.Unavailable,
            (await Checker(Release()).CheckAsync(new Version(0, 1, 0), cts.Token)).State);
    }
}
