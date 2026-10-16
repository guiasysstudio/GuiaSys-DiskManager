using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GuiaSys.DiskManager.Updates;

public enum UpdateCheckState { Available, UpToDate, NotPublished, Unavailable }

public sealed record UpdateCheckResult(
    UpdateCheckState State, string Message, string? LatestVersion = null, Uri? ReleasePage = null);

/// <summary>
/// Manual, read-only release metadata lookup. NEVER downloads or executes a binary.
/// Official installer download is handled by the user's browser on a vetted release page.
/// Public GitHub releases are required; a private repo cannot be queried anonymously.
/// </summary>
public sealed class ReleaseUpdateChecker(HttpClient client)
{
    public const string Repository = "guiasysstudio/GuiaSys-DiskManager";
    public static readonly Uri MetadataEndpoint =
        new("https://api.github.com/repos/" + Repository + "/releases/latest");
    private const int MaxResponseBytes = 128 * 1024;
    private static readonly Regex VersionTag = new(
        @"^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));

    public async Task<UpdateCheckResult> CheckAsync(Version installed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installed);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            using var request = new HttpRequestMessage(HttpMethod.Get, MetadataEndpoint);
            request.Headers.TryAddWithoutValidation("User-Agent", "GuiaSys-DiskManager-Updater/1");
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            using var response = await client.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new(UpdateCheckState.NotPublished,
                    "Não há lançamento público acessível. O repositório pode ser privado ou não possuir releases.");
            if (response.StatusCode != HttpStatusCode.OK)
                return Unavailable("GitHub não forneceu uma versão pública verificável.");

            // No redirects in the production HttpClient; also reject mocked or unexpected final origins.
            if (response.RequestMessage?.RequestUri is Uri final &&
                !string.Equals(final.AbsoluteUri, MetadataEndpoint.AbsoluteUri, StringComparison.Ordinal))
                return Unavailable("Origem inesperada na consulta de atualizações.");

            if (response.Content.Headers.ContentLength is > MaxResponseBytes)
                return Unavailable("Metadados de atualização excedem o tamanho permitido.");
            using var data = new MemoryStream();
            using (var stream = await response.Content.ReadAsStreamAsync(timeout.Token))
            {
                var buffer = new byte[4096];
                while (true)
                {
                    var length = await stream.ReadAsync(buffer, timeout.Token);
                    if (length == 0) break;
                    if (data.Length + length > MaxResponseBytes)
                        return Unavailable("Metadados de atualização excedem o tamanho permitido.");
                    data.Write(buffer, 0, length);
                }
            }

            using var json = JsonDocument.Parse(data.ToArray());
            if (!TryReadRelease(json.RootElement, out var latest, out var releaseUrl))
                return Unavailable("Release incompleta ou com origem/instalador não verificável.");
            var local = new Version(installed.Major, installed.Minor,
                installed.Build < 0 ? 0 : installed.Build);
            return latest > local
                ? new(UpdateCheckState.Available,
                    $"Nova versão {latest.ToString(3)} disponível. Abra a página oficial e revise o instalador antes de atualizar.",
                    latest.ToString(3), releaseUrl)
                : new(UpdateCheckState.UpToDate,
                    $"Versão instalada {local.ToString(3)}: nenhuma atualização estável mais recente.",
                    latest.ToString(3));
        }
        catch (OperationCanceledException) { return Unavailable("Consulta cancelada ou tempo limite excedido."); }
        catch (HttpRequestException) { return Unavailable("Não foi possível consultar o GitHub. Verifique sua conexão."); }
        catch (IOException) { return Unavailable("Falha na leitura da resposta de atualização."); }
        catch (JsonException) { return Unavailable("Resposta do GitHub não contém JSON válido."); }
    }

    internal static bool TryReadRelease(JsonElement root, out Version version, out Uri releasePage)
    {
        version = new Version(0, 0, 0);
        releasePage = null!;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False ||
            !root.TryGetProperty("prerelease", out var prerelease) || prerelease.ValueKind != JsonValueKind.False ||
            !root.TryGetProperty("html_url", out var html) || html.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return false;

        var tagText = tag.GetString();
        if (tagText is null || tagText.Length > 36) return false;
        var match = VersionTag.Match(tagText);
        if (!match.Success ||
            !int.TryParse(match.Groups[1].Value, out var major) ||
            !int.TryParse(match.Groups[2].Value, out var minor) ||
            !int.TryParse(match.Groups[3].Value, out var patch))
            return false;
        var semver = $"{major}.{minor}.{patch}";
        // Canonical release page and asset must be within the exact official repository.
        var expectedPage = $"https://github.com/{Repository}/releases/tag/v{semver}";
        var expectedAssetName = $"GuiaSys-DiskManager-Setup-{semver}-x64.exe";
        var expectedAssetUrl = $"https://github.com/{Repository}/releases/download/v{semver}/{expectedAssetName}";
        if (!string.Equals(html.GetString(), expectedPage, StringComparison.Ordinal))
            return false;

        var installerFound = false;
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object ||
                !asset.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String ||
                !string.Equals(name.GetString(), expectedAssetName, StringComparison.Ordinal))
                continue;
            if (!asset.TryGetProperty("state", out var state) || state.ValueKind != JsonValueKind.String ||
                state.GetString() != "uploaded" ||
                !asset.TryGetProperty("size", out var size) || !size.TryGetInt64(out var length) ||
                length < 1024 * 1024 ||
                !asset.TryGetProperty("browser_download_url", out var download) ||
                download.ValueKind != JsonValueKind.String ||
                !string.Equals(download.GetString(), expectedAssetUrl, StringComparison.Ordinal))
                return false;
            installerFound = true;
        }
        if (!installerFound || !Uri.TryCreate(expectedPage, UriKind.Absolute, out var page)) return false;
        version = new Version(major, minor, patch);
        releasePage = page;
        return true;
    }

    private static UpdateCheckResult Unavailable(string reason) =>
        new(UpdateCheckState.Unavailable, reason + " Nenhuma atualização automática foi iniciada.");
}
