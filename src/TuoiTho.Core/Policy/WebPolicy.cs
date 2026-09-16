namespace TuoiTho.Core.Policy;

public enum BrowserProvider { GenericWeb, YouTube, TikTok }
public enum WebRuleScope { Site, YouTubeChannel, TikTokCreator }
public enum WebRuleDecision { Allow, Block }
public enum BrowserContentType { Site, Channel, Video, ShortForm, Creator, Unknown }

/// <summary>Persisted policy only; it never contains browsing, search, watch, or page-content history.</summary>
public sealed record WebRule(string ProfileId, BrowserProvider Provider, WebRuleScope Scope, WebRuleDecision Decision, string NormalizedKey, string DisplayLabel);
public sealed record BrowserNavigation(BrowserProvider Provider, string Host, string Path, BrowserContentType ContentType, string? ChannelId = null, string? ChannelHandle = null, string? TikTokCreator = null);
public sealed record WebPolicyDecision(bool Allowed, string Reason, WebRule? MatchedRule = null);

public interface IWebPolicyStore
{
    Task<IReadOnlyList<WebRule>> GetRulesAsync(string profileId, CancellationToken cancellationToken = default);
    Task SaveRuleAsync(WebRule rule, CancellationToken cancellationToken = default);
    Task RemoveRuleAsync(string profileId, BrowserProvider provider, WebRuleScope scope, string normalizedKey, CancellationToken cancellationToken = default);
}

public static class WebIdentityNormalizer
{
    public static bool TryNormalizeYouTubeChannel(string value, out string key, out string display)
    {
        key = display = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        value = value.Trim();
        if (value.StartsWith('@')) return Handle(value, out key, out display);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !IsYouTubeHost(uri.Host)) return false;
        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1) return false;
        if (parts[0].StartsWith('@')) return Handle(parts[0], out key, out display);
        if (parts.Length >= 2 && parts[0].Equals("channel", StringComparison.OrdinalIgnoreCase) && IsChannelId(parts[1])) { key = "id:" + parts[1]; display = parts[1]; return true; }
        if (parts.Length >= 2 && (parts[0].Equals("c", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("user", StringComparison.OrdinalIgnoreCase))) return Handle("@" + parts[1], out key, out display);
        return false;
    }

    public static bool TryNormalizeTikTokCreator(string value, out string key, out string display)
    {
        key = display = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        value = value.Trim();
        if (value.StartsWith('@')) return TikTokHandle(value, out key, out display);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !IsTikTokHost(uri.Host)) return false;
        var creator = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return creator is not null && creator.StartsWith('@') && TikTokHandle(creator, out key, out display);
    }

    public static bool IsYouTubeHost(string host) => host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase) || host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase);
    public static bool IsTikTokHost(string host) => host.Equals("tiktok.com", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".tiktok.com", StringComparison.OrdinalIgnoreCase);
    public static bool IsChannelId(string value) => value.Length >= 3 && value.StartsWith("UC", StringComparison.Ordinal) && value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');

    private static bool Handle(string value, out string key, out string display)
    {
        key = display = string.Empty; var handle = value.Trim().TrimStart('@');
        if (handle.Length is < 1 or > 100 || !handle.All(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_')) return false;
        display = "@" + handle; key = "handle:" + display.ToLowerInvariant(); return true;
    }
    private static bool TikTokHandle(string value, out string key, out string display)
    {
        key = display = string.Empty; var handle = value.Trim().TrimStart('@');
        if (handle.Length is < 1 or > 100 || !handle.All(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_')) return false;
        display = "@" + handle; key = "creator:" + display.ToLowerInvariant(); return true;
    }
}

public sealed class WebPolicyEngine
{
    public static WebPolicyDecision Evaluate(BrowserNavigation navigation, IEnumerable<WebRule> rules)
    {
        var selected = rules.Where(rule => rule.Provider == navigation.Provider).ToArray();
        var keys = NavigationKeys(navigation);
        var matched = selected.FirstOrDefault(rule => keys.Contains(rule.NormalizedKey, StringComparer.OrdinalIgnoreCase));
        if (matched is not null) return new(matched.Decision == WebRuleDecision.Allow, matched.Decision == WebRuleDecision.Allow ? "EXPLICIT_ALLOW" : "EXPLICIT_BLOCK", matched);
        var site = selected.FirstOrDefault(rule => rule.Scope == WebRuleScope.Site && rule.NormalizedKey.Equals(SiteKey(navigation.Provider), StringComparison.OrdinalIgnoreCase));
        if (site is not null) return new(site.Decision == WebRuleDecision.Allow, site.Decision == WebRuleDecision.Allow ? "SITE_ALLOW" : "SITE_BLOCK", site);
        return new(true, "NO_MATCH");
    }

    public static string SiteKey(BrowserProvider provider) => provider switch { BrowserProvider.YouTube => "youtube.com", BrowserProvider.TikTok => "tiktok.com", _ => "" };
    private static List<string> NavigationKeys(BrowserNavigation navigation)
    {
        var keys = new List<string>();
        if (navigation.Provider == BrowserProvider.YouTube)
        {
            if (!string.IsNullOrWhiteSpace(navigation.ChannelId) && WebIdentityNormalizer.IsChannelId(navigation.ChannelId)) keys.Add("id:" + navigation.ChannelId);
            if (!string.IsNullOrWhiteSpace(navigation.ChannelHandle) && WebIdentityNormalizer.TryNormalizeYouTubeChannel(navigation.ChannelHandle, out var handle, out _)) keys.Add(handle);
        }
        if (navigation.Provider == BrowserProvider.TikTok && !string.IsNullOrWhiteSpace(navigation.TikTokCreator) && WebIdentityNormalizer.TryNormalizeTikTokCreator(navigation.TikTokCreator, out var creator, out _)) keys.Add(creator);
        return keys;
    }
}

/// <summary>Untrusted browser navigation payload, bounded before deserialization by BrowserHost.</summary>
public sealed record BrowserNavigationRequest(string ExtensionId, string ProfileId, int ManagedSessionId, BrowserProvider Provider, string Host, string Path, BrowserContentType ContentType, string? ChannelId = null, string? ChannelHandle = null, string? TikTokCreator = null);
public sealed record BrowserNavigationResponse(bool Allowed, string Reason, string? DisplayLabel = null, string? Diagnostic = null);

public static class BrowserNavigationValidator
{
    public const int MaximumMessageBytes = 16 * 1024;
    public static bool IsValid(BrowserNavigationRequest request, string expectedExtensionId)
    {
        if (request is null || string.IsNullOrWhiteSpace(expectedExtensionId) || !string.Equals(request.ExtensionId, expectedExtensionId, StringComparison.Ordinal)) return false;
        if (request.ProfileId.Length is < 1 or > 128 || request.ManagedSessionId < 0 || request.Host.Length is < 1 or > 255 || request.Path.Length > 2048) return false;
        return request.Provider switch
        {
            BrowserProvider.YouTube => WebIdentityNormalizer.IsYouTubeHost(request.Host),
            BrowserProvider.TikTok => WebIdentityNormalizer.IsTikTokHost(request.Host),
            _ => false
        };
    }
}
public static class BrowserHostAvailabilityPolicy
{
    public static BrowserNavigationResponse ServiceUnavailable(bool testMode) => testMode
        ? new(true, "SERVICE_UNAVAILABLE", Diagnostic: "Tuổi Thơ chưa kết nối.")
        : new(false, "SERVICE_UNAVAILABLE", Diagnostic: "Tuổi Thơ chưa kết nối.");
}