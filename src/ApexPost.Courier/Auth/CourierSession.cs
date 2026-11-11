using System.Text;
using System.Text.Json;

namespace ApexPost.Courier.Auth;

/// <summary>
/// Reads display information from a JWT payload. This is not validation (the API validates the
/// signature, issuer, audience and lifetime); it only tells the app which courierId to stamp on scans.
/// </summary>
public static class JwtPayloadReader
{
    public static bool TryRead(string token, out string? courierId, out DateTimeOffset? expiresAtUtc)
    {
        courierId = null;
        expiresAtUtc = null;
        var parts = token.Split('.');
        if (parts.Length != 3)
            return false;

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            var root = document.RootElement;
            if (root.TryGetProperty("courierId", out var courier) && courier.ValueKind == JsonValueKind.String)
                courierId = courier.GetString();
            if (root.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds))
                expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(seconds);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return false;
        }
    }
}
