using System.Text.Json;

namespace TuoiTho.Core.Security;

public sealed record AccountProtectionConfiguration(
    bool Enabled,
    string ManagedChildSid,
    string ManagedChildName)
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "TuoiTho",
        "account-protection.json");

    public static AccountProtectionConfiguration? TryLoad(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path)) return null;
            var value = JsonSerializer.Deserialize<AccountProtectionConfiguration>(
                File.ReadAllText(path));
            return value is { Enabled: true } &&
                   !string.IsNullOrWhiteSpace(value.ManagedChildSid)
                ? value
                : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
