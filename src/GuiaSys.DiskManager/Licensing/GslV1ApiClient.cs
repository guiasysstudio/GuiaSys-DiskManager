using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace GuiaSys.DiskManager.Licensing;

/// <summary>Strict HTTPS-only GSL-v1 adapter. No retry of mutating requests with a new requestId.</summary>
public sealed class GslV1ApiClient(HttpClient client)
{
    public async Task<JsonElement> PostAsync(string path, object body, CancellationToken cancellationToken)
    {
        if (path is not (
            GslV1Protocol.ProjectConfig or GslV1Protocol.TrialStart or GslV1Protocol.TrialValidate or
            GslV1Protocol.Activate or GslV1Protocol.Validate or GslV1Protocol.Deactivate))
            throw new ArgumentOutOfRangeException(nameof(path));

        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(new Uri(GslV1Protocol.ApiBase), path.TrimStart('/')))
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(12));
        HttpResponseMessage response;
        try { response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new GslV1ApiException("network_error"); }
        catch (HttpRequestException) { throw new GslV1ApiException("network_error"); }

        using (response)
        {
            const int limit = 64 * 1024;
            if (response.Content.Headers.ContentLength > limit) throw new GslV1ApiException("invalid_response");
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[4096];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) != 0)
            {
                if (buffer.Length + read > limit) throw new GslV1ApiException("invalid_response");
                buffer.Write(chunk, 0, read);
            }
            try
            {
                using var json = JsonDocument.Parse(buffer.ToArray());
                var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("ok", out var ok) ||
                    ok.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new GslV1ApiException("invalid_response");
                if (!response.IsSuccessStatusCode || !ok.GetBoolean())
                {
                    string code = "server_error";
                    if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String &&
                        err.GetString() is { Length: > 0 } value && value.Length <= 80)
                        code = value;
                    int? retryAfter = null;
                    if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Object &&
                        details.TryGetProperty("retryAfterSeconds", out var retry) &&
                        retry.ValueKind == JsonValueKind.Number && retry.TryGetInt32(out var seconds) && seconds is > 0 and <= 86400)
                        retryAfter = seconds;
                    throw new GslV1ApiException(code, (int)response.StatusCode, retryAfter);
                }
                return root.Clone();
            }
            catch (JsonException) { throw new GslV1ApiException("invalid_response"); }
        }
    }
}
