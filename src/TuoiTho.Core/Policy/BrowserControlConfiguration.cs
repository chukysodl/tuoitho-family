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
    public IReadOnlyList<string>? AdditionalExtensionIds { get; init; }

    public IEnumerable<string> AllowedExtensionIds
    {
        get
        {
            yield return ExtensionId;
            if (AdditionalExtensionIds is null) yield break;
            foreach (var id in AdditionalExtensionIds)
                if (!string.Equals(id, ExtensionId, StringComparison.Ordinal))
                    yield return id;
        }
    }

    public bool AllowsExtensionId(string? extensionId) =>
        !string.IsNullOrWhiteSpace(extensionId) &&
        AllowedExtensionIds.Contains(extensionId, StringComparer.Ordinal);

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
            (AdditionalExtensionIds?.Any(string.IsNullOrWhiteSpace) ?? false) ||
            string.IsNullOrWhiteSpace(ProfileId) ||
            ManagedSessionId < 0 ||
            string.IsNullOrWhiteSpace(ManagedUserSid))
        {
            throw new InvalidOperationException("Browser control configuration is incomplete.");
        }
    }
}
