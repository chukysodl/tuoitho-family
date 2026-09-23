using System.Text.Json;

namespace TuoiTho.Core.Policy;

/// <summary>
/// Shared machine-local browser-control identity. The installer creates this file and Windows ACLs
/// protect it for the managed user and LocalSystem; it never comes from webpage or extension input.
/// </summary>
public sealed record BrowserControlConfiguration(
    string ExtensionId,
    string ProfileId,
    int ManagedSessionId,
    string ManagedUserSid,
    bool TestMode)
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "TuoiTho",
        "browser-control.json");

    public static BrowserControlConfiguration Load(string? path = null)
    {
        var file = path ?? DefaultPath;
        if (!File.Exists(file))
        {
            throw new InvalidOperationException("Browser control configuration is unavailable.");
        }

        var configuration = JsonSerializer.Deserialize<BrowserControlConfiguration>(File.ReadAllText(file))
            ?? throw new InvalidOperationException("Browser control configuration is invalid.");
        configuration.Validate();
        return configuration;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ExtensionId) ||
            string.IsNullOrWhiteSpace(ProfileId) ||
            ManagedSessionId < 0 ||
            string.IsNullOrWhiteSpace(ManagedUserSid))
        {
            throw new InvalidOperationException("Browser control configuration is incomplete.");
        }
    }
}
