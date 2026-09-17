using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TuoiTho.Core.Policy;

public enum BrowserProvider { GenericWeb, YouTube, TikTok }
public enum WebRuleScope { Site, YouTubeChannel, TikTokCreator, Domain, PathPrefix }
public enum WebRuleDecision { Allow, Block }
public enum BrowserContentType { Site, Channel, Video, ShortForm, Playable, Creator, Unknown }

/// <summary>Persisted parent-authored policy only; it never contains browsing, search, watch, or page-content history.</summary>
public sealed record WebRule(string ProfileId, BrowserProvider Provider, WebRuleScope Scope, WebRuleDecision Decision, string NormalizedKey, string DisplayLabel, string? RuleId = null)
{
    /// <summary>Deterministic parent-policy identity. It is not derived from a browsing event.</summary>
    public string StableId => RuleId ?? $"web:{ProfileId}:{Provider}:{Scope}:{NormalizedKey}";
}
public sealed record BrowserNavigation(BrowserProvider Provider, string Host, string Path, BrowserContentType ContentType, string? ChannelId = null, string? ChannelHandle = null, string? TikTokCreator = null);
public sealed record WebPolicyDecision(bool Allowed, string Reason, WebRule? MatchedRule = null);
public sealed record WebPolicySnapshot(long Revision, IReadOnlyList<WebRule> Rules);
public sealed record CustomWebsiteIdentity(string Host, string? PathPrefix, string DisplayValue)
{
    public string NormalizedKey => PathPrefix is null ? $"domain:{Host}" : $"path:{Host}{PathPrefix}";
}

public interface IWebPolicyStore
{
    Task<IReadOnlyList<WebRule>> GetRulesAsync(string profileId, CancellationToken cancellationToken = default);
    Task<WebPolicySnapshot> GetSnapshotAsync(string profileId, CancellationToken cancellationToken = default);
    Task SaveRuleAsync(WebRule rule, CancellationToken cancellationToken = default);
    Task RemoveRuleAsync(string profileId, BrowserProvider provider, WebRuleScope scope, string normalizedKey, CancellationToken cancellationToken = default);
}

public static class WebIdentityNormalizer
{
    private static readonly IdnMapping Idn = new();
    private static readonly Regex HostLabel = new("^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static bool TryNormalizeYouTubeChannel(string value, out string key, out string display)
    {
        key = display = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        value = value.Trim();
        if (value.StartsWith('@')) return TryDecodePathSegment(value, out var decodedHandle) && Handle(decodedHandle, out key, out display);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !IsYouTubeHost(uri.Host)) return false;
        if (!TryGetDecodedPathSegments(uri, out var parts) || parts.Length < 1) return false;
        if (parts[0].StartsWith('@')) return Handle(parts[0], out key, out display);
        if (parts.Length >= 2 && parts[0].Equals("channel", StringComparison.OrdinalIgnoreCase) && IsChannelId(parts[1]))
        {
            key = "id:" + parts[1]; display = parts[1]; return true;
        }
        if (parts.Length >= 2 && (parts[0].Equals("c", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("user", StringComparison.OrdinalIgnoreCase))) return Handle("@" + parts[1], out key, out display);
        return false;
    }

    public static bool TryNormalizeTikTokCreator(string value, out string key, out string display)
    {
        key = display = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        value = value.Trim();
        if (value.StartsWith('@')) return TryDecodePathSegment(value, out var decodedHandle) && TikTokHandle(decodedHandle, out key, out display);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !IsTikTokHost(uri.Host)) return false;
        if (!TryGetDecodedPathSegments(uri, out var parts) || parts.Length < 1) return false;
        return parts[0].StartsWith('@') && TikTokHandle(parts[0], out key, out display);
    }

    /// <summary>Normalizes only a parent-entered website rule. It rejects credentials, internal schemes and malformed percent escapes.</summary>
    public static bool TryNormalizeCustomWebsite(string value, WebRuleScope scope, out CustomWebsiteIdentity identity)
    {
        identity = default!;
        if (scope is not (WebRuleScope.Domain or WebRuleScope.PathPrefix) || string.IsNullOrWhiteSpace(value)) return false;
        value = value.Trim();
        if (!HasOnlyValidPercentEscapes(value)) return false;
        if (value.StartsWith("chrome:", StringComparison.OrdinalIgnoreCase) || value.StartsWith("edge:", StringComparison.OrdinalIgnoreCase) || value.StartsWith("about:", StringComparison.OrdinalIgnoreCase) || value.StartsWith("chrome-extension:", StringComparison.OrdinalIgnoreCase)) return false;
        var absolute = value.Contains("://", StringComparison.Ordinal) ? value : "https://" + value;
        if (!Uri.TryCreate(absolute, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment)) return false;
        if (!TryNormalizeHost(uri.Host, out var host, out var friendlyHost)) return false;
        var escapedPath = uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped);
        if (!TryDecodePath(escapedPath, out var path)) return false;
        if (scope == WebRuleScope.Domain)
        {
            identity = new(host, null, friendlyHost);
            return true;
        }
        if (string.IsNullOrEmpty(path) || path == "/") return false;
        path = NormalizePath(path);
        identity = new(host, path, friendlyHost + path);
        return true;
    }

    public static bool TryNormalizeHost(string value, out string host, out string friendlyHost)
    {
        host = friendlyHost = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var raw = value.Trim().Trim('[', ']');
        if (System.Net.IPAddress.TryParse(raw, out var address))
        {
            host = address.ToString().ToLowerInvariant();
            if (host.Contains(':')) host = "[" + host + "]";
            friendlyHost = host;
            return true;
        }
        try { host = Idn.GetAscii(raw).TrimEnd('.').ToLowerInvariant(); friendlyHost = Idn.GetUnicode(host).Normalize(NormalizationForm.FormC); }
        catch (ArgumentException) { return false; }
        if (host.Length is < 1 or > 253 || host.Split('.').Any(label => !HostLabel.IsMatch(label))) return false;
        return true;
    }

    public static bool IsYouTubeHost(string host) => host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase) || host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase);
    public static bool IsTikTokHost(string host) => host.Equals("tiktok.com", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".tiktok.com", StringComparison.OrdinalIgnoreCase);
    public static bool IsChannelId(string value) => value.Length >= 3 && value.StartsWith("UC", StringComparison.Ordinal) && value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');

    private static bool TryGetDecodedPathSegments(Uri uri, out string[] parts)
    {
        var rawPath = uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped).Trim('/');
        if (string.IsNullOrEmpty(rawPath)) { parts = []; return true; }
        var rawParts = rawPath.Split('/', StringSplitOptions.RemoveEmptyEntries); parts = new string[rawParts.Length];
        for (var index = 0; index < rawParts.Length; index++) if (!TryDecodePathSegment(rawParts[index], out parts[index])) return false;
        return true;
    }

    private static bool TryDecodePath(string value, out string decoded)
    {
        decoded = string.Empty;
        foreach (var index in Enumerable.Range(0, value.Length).Where(index => value[index] == '%')) if (index + 2 >= value.Length || !IsHex(value[index + 1]) || !IsHex(value[index + 2])) return false;
        try { decoded = Uri.UnescapeDataString(value).Normalize(NormalizationForm.FormC); if (!decoded.StartsWith('/')) decoded = "/" + decoded; return decoded.IndexOfAny(['\\', '\0']) < 0; }
        catch (UriFormatException) { return false; }
    }

    private static string NormalizePath(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString);
        return "/" + string.Join('/', segments) + (path.EndsWith('/') ? "/" : "");
    }

    private static bool TryDecodePathSegment(string value, out string decoded)
    {
        decoded = string.Empty;
        for (var index = 0; index < value.Length; index++) { if (value[index] != '%') continue; if (index + 2 >= value.Length || !IsHex(value[index + 1]) || !IsHex(value[index + 2])) return false; index += 2; }
        try { decoded = Uri.UnescapeDataString(value).Normalize(NormalizationForm.FormC); return decoded.IndexOfAny(['/', '\\', '\0']) < 0; } catch (UriFormatException) { return false; }
    }
    private static bool IsHex(char value) => value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
    private static bool HasOnlyValidPercentEscapes(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '%') continue;
            if (index + 2 >= value.Length || !IsHex(value[index + 1]) || !IsHex(value[index + 2])) return false;
            index += 2;
        }
        return true;
    }
    private static bool Handle(string value, out string key, out string display)
    { key = display = string.Empty; var handle = value.Trim().TrimStart('@').Normalize(NormalizationForm.FormC); if (handle.Length is < 1 or > 100 || !handle.All(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_')) return false; display = "@" + handle; key = "handle:" + display.ToLowerInvariant().Normalize(NormalizationForm.FormC); return true; }
    private static bool TikTokHandle(string value, out string key, out string display)
    { key = display = string.Empty; var handle = value.Trim().TrimStart('@').Normalize(NormalizationForm.FormC); if (handle.Length is < 1 or > 100 || !handle.All(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_')) return false; display = "@" + handle; key = "creator:" + display.ToLowerInvariant().Normalize(NormalizationForm.FormC); return true; }
}

public sealed class WebPolicyEngine
{
    public static WebPolicyDecision Evaluate(BrowserNavigation navigation, IEnumerable<WebRule> rules)
    {
        var selected = rules.Where(rule => rule.Provider == navigation.Provider).ToArray();
        if (navigation.Provider == BrowserProvider.GenericWeb)
        {
            if (!WebIdentityNormalizer.TryNormalizeHost(navigation.Host, out var host, out _)) return new(true, "NO_MATCH");
            var path = string.IsNullOrWhiteSpace(navigation.Path) ? "/" : navigation.Path;
            var matching = selected.Where(rule => MatchesCustomRule(rule, host, path)).OrderByDescending(CustomSpecificity).ThenBy(rule => rule.NormalizedKey, StringComparer.Ordinal).ToArray();
            var matched = matching.FirstOrDefault();
            return matched is null ? new(true, "NO_MATCH") : new(matched.Decision == WebRuleDecision.Allow, matched.Decision == WebRuleDecision.Allow ? "CUSTOM_ALLOW" : "CUSTOM_BLOCK", matched);
        }
        var keys = NavigationKeys(navigation);
        var matchedContent = selected.FirstOrDefault(rule => keys.Contains(rule.NormalizedKey, StringComparer.OrdinalIgnoreCase));
        if (matchedContent is not null) return new(matchedContent.Decision == WebRuleDecision.Allow, matchedContent.Decision == WebRuleDecision.Allow ? "EXPLICIT_ALLOW" : "EXPLICIT_BLOCK", matchedContent);
        var site = selected.FirstOrDefault(rule => rule.Scope == WebRuleScope.Site && rule.NormalizedKey.Equals(SiteKey(navigation.Provider), StringComparison.OrdinalIgnoreCase));
        return site is null ? new(true, "NO_MATCH") : new(site.Decision == WebRuleDecision.Allow, site.Decision == WebRuleDecision.Allow ? "SITE_ALLOW" : "SITE_BLOCK", site);
    }

    public static string SiteKey(BrowserProvider provider) => provider switch { BrowserProvider.YouTube => "youtube.com", BrowserProvider.TikTok => "tiktok.com", _ => "" };
    private static bool MatchesCustomRule(WebRule rule, string host, string path)
    {
        if (rule.Scope == WebRuleScope.Domain && TryParseCustomKey(rule.NormalizedKey, "domain:", out var ruleHost, out _)) return HostMatches(host, ruleHost);
        if (rule.Scope == WebRuleScope.PathPrefix && TryParseCustomKey(rule.NormalizedKey, "path:", out ruleHost, out var prefix)) return HostMatches(host, ruleHost) && path.StartsWith(prefix!, StringComparison.Ordinal);
        return false;
    }
    private static int CustomSpecificity(WebRule rule) => rule.Scope == WebRuleScope.PathPrefix ? 10000 + rule.NormalizedKey.Length : rule.NormalizedKey.Length;
    private static bool HostMatches(string host, string ruleHost) => host.Equals(ruleHost, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + ruleHost, StringComparison.OrdinalIgnoreCase);
    private static bool TryParseCustomKey(string key, string prefix, out string host, out string? path)
    { host = string.Empty; path = null; if (!key.StartsWith(prefix, StringComparison.Ordinal)) return false; var rest = key[prefix.Length..]; var slash = rest.IndexOf('/'); host = slash < 0 ? rest : rest[..slash]; path = slash < 0 ? null : rest[slash..]; return WebIdentityNormalizer.TryNormalizeHost(host, out host, out _); }
    private static List<string> NavigationKeys(BrowserNavigation navigation)
    { var keys = new List<string>(); if (navigation.Provider == BrowserProvider.YouTube) { if (!string.IsNullOrWhiteSpace(navigation.ChannelId) && WebIdentityNormalizer.IsChannelId(navigation.ChannelId)) keys.Add("id:" + navigation.ChannelId); if (!string.IsNullOrWhiteSpace(navigation.ChannelHandle) && WebIdentityNormalizer.TryNormalizeYouTubeChannel(navigation.ChannelHandle, out var handle, out _)) keys.Add(handle); } if (navigation.Provider == BrowserProvider.TikTok && !string.IsNullOrWhiteSpace(navigation.TikTokCreator) && WebIdentityNormalizer.TryNormalizeTikTokCreator(navigation.TikTokCreator, out var creator, out _)) keys.Add(creator); return keys; }
}

/// <summary>Untrusted browser request, bounded before deserialization by BrowserHost. Custom policy sync sends no visited URL.</summary>
public sealed record BrowserNavigationRequest(string ExtensionId, string ProfileId, int ManagedSessionId, BrowserProvider Provider, string Host, string Path, BrowserContentType ContentType, string? ChannelId = null, string? ChannelHandle = null, string? TikTokCreator = null, bool IsDiagnosticProbe = false, string? OwnerState = null, string? ShortContainer = null, int? OwnerCandidateCount = null, string? OwnerSource = null, bool IsPolicySync = false);
public sealed record BrowserNavigationResponse(bool Allowed, string Reason, string? DisplayLabel = null, string? Diagnostic = null, long? PolicyRevision = null, IReadOnlyList<WebRule>? CustomRules = null);

public static class BrowserNavigationValidator
{
    public const int MaximumMessageBytes = 16 * 1024;
    public static bool IsValid(BrowserNavigationRequest request, string expectedExtensionId)
    {
        if (request is null || string.IsNullOrWhiteSpace(expectedExtensionId) || !string.Equals(request.ExtensionId, expectedExtensionId, StringComparison.Ordinal) || request.ProfileId.Length is < 1 or > 128 || request.ManagedSessionId < 0) return false;
        if (request.IsPolicySync) return request.Provider == BrowserProvider.GenericWeb && string.IsNullOrEmpty(request.Host) && string.IsNullOrEmpty(request.Path) && request.ContentType == BrowserContentType.Unknown && request.ChannelId is null && request.ChannelHandle is null && request.TikTokCreator is null;
        if (request.Host.Length is < 1 or > 255 || request.Path.Length > 2048) return false;
        if (request.OwnerState is not null && request.OwnerState is not ("SHORT_OWNER_FOUND" or "SHORT_OWNER_UNKNOWN")) return false;
        if (request.OwnerState is not null && request.ContentType != BrowserContentType.ShortForm) return false;
        if (request.ContentType != BrowserContentType.ShortForm && (request.ShortContainer is not null || request.OwnerCandidateCount is not null || request.OwnerSource is not null)) return false;
        if (request.ShortContainer is not null && !Regex.IsMatch(request.ShortContainer, "^[a-z0-9-]{1,64}$")) return false;
        if (request.OwnerCandidateCount is < 0 or > 16) return false;
        if (request.OwnerSource is not null && request.OwnerSource is not ("ANCHOR" or "CHANNEL_ID" or "VISIBLE_HANDLE_TEXT" or "NONE")) return false;
        return request.Provider switch { BrowserProvider.YouTube => WebIdentityNormalizer.IsYouTubeHost(request.Host), BrowserProvider.TikTok => WebIdentityNormalizer.IsTikTokHost(request.Host), _ => false };
    }
}

public static class BrowserHostAvailabilityPolicy
{
    public static BrowserNavigationResponse ServiceUnavailable(bool testMode) => testMode ? new(true, "SERVICE_UNAVAILABLE", Diagnostic: "Tuổi Thơ chưa kết nối.") : new(false, "SERVICE_UNAVAILABLE", Diagnostic: "Tuổi Thơ chưa kết nối.");
}
