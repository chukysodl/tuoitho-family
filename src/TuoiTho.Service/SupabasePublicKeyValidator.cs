using System.Text;
using System.Text.Json;

namespace TuoiTho.Service;

/// <summary>Accepts only Supabase publishable keys or legacy anon JWTs; never service-role or secret keys.</summary>
public static class SupabasePublicKeyValidator
{
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length is < 20 or > 512
            || value.StartsWith("sb_secret_", StringComparison.OrdinalIgnoreCase)
            || value.Contains("service_role", StringComparison.OrdinalIgnoreCase)) return false;
        if (value.StartsWith("sb_publishable_", StringComparison.Ordinal)) return true;

        var parts = value.Split('.');
        if (parts.Length != 3) return false;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            return document.RootElement.TryGetProperty("role", out var role) && role.GetString() == "anon";
        }
        catch (Exception exception) when (exception is FormatException or JsonException or InvalidOperationException) { return false; }
    }
}
