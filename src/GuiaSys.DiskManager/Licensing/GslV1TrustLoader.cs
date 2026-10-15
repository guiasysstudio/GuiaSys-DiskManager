using System.Text.Json;

namespace GuiaSys.DiskManager.Licensing;

/// <summary>Public, build/distribution-pinned ES256 key. Never trust an unpinned key downloaded at runtime.</summary>
public static class GslV1TrustLoader
{
    public static GslV1Trust? Load(string? path = null)
    {
        var source = path ?? Path.Combine(AppContext.BaseDirectory, "gsl-v1-trust.json");
        if (!File.Exists(source)) return null;
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(source));
            var root = json.RootElement;
            var jwk = root.GetProperty("publicJwk");
            var trust = new GslV1Trust(
                root.GetProperty("projectId").GetString() ?? "",
                root.GetProperty("keyId").GetString() ?? "",
                jwk.GetProperty("x").GetString() ?? "",
                jwk.GetProperty("y").GetString() ?? "");
            return jwk.GetProperty("kty").GetString() == "EC" &&
                jwk.GetProperty("crv").GetString() == "P-256" && trust.IsValid ? trust : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException
            or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }
}
