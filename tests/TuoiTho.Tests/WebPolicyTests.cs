using TuoiTho.Core.Policy;

namespace TuoiTho.Tests;

public sealed class WebPolicyTests
{
    [Theory]
    [InlineData("@MrBeast", "handle:@mrbeast")]
    [InlineData("https://www.youtube.com/@MrBeast", "handle:@mrbeast")]
    [InlineData("https://www.youtube.com/channel/UCabc123", "id:UCabc123")]
    [InlineData("https://www.youtube.com/c/Example", "handle:@example")]
    public void YouTubeNormalizesChannelInputs(string input, string expected)
    {
        Assert.True(WebIdentityNormalizer.TryNormalizeYouTubeChannel(input, out var key, out _));
        Assert.Equal(expected, key);
    }

    [Fact]
    public void UnicodeYouTubeHandleFormsNormalizeToOneIdentity()
    {
        var inputs = new[]
        {
            "@VịtBéoTV",
            "https://www.youtube.com/@VịtBéoTV",
            "https://www.youtube.com/@V%E1%BB%8BtB%C3%A9oTV",
            "https://www.youtube.com/@V%E1%BB%8BtB%C3%A9oTV/",
            "https://www.youtube.com/@V%E1%BB%8BtB%C3%A9oTV?sub_confirmation=1"
        };
        var keys = new List<string>();
        foreach (var input in inputs)
        {
            Assert.True(WebIdentityNormalizer.TryNormalizeYouTubeChannel(input, out var key, out var display));
            Assert.Equal("@VịtBéoTV", display);
            keys.Add(key);
        }
        Assert.Single(keys.Distinct(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("https://www.youtube.com/@bad%")]
    [InlineData("https://www.youtube.com/@bad%GG")]
    [InlineData("@bad%2")]
    public void MalformedPercentEncodingIsRejected(string input) => Assert.False(WebIdentityNormalizer.TryNormalizeYouTubeChannel(input, out _, out _));

    [Theory]
    [InlineData("@Creator", "creator:@creator")]
    [InlineData("https://www.tiktok.com/@Creator", "creator:@creator")]
    [InlineData("https://www.tiktok.com/@Creator/video/7", "creator:@creator")]
    [InlineData("https://www.tiktok.com/@V%E1%BB%8BtB%C3%A9o/video/7", "creator:@vịtbéo")]
    public void TikTokNormalizesCreatorInputs(string input, string expected)
    {
        Assert.True(WebIdentityNormalizer.TryNormalizeTikTokCreator(input, out var key, out _));
        Assert.Equal(expected, key);
    }

    [Fact]
    public void YouTubeChannelRuleBlocksChannelVideoAndShortsButNotOtherChannels()
    {
        var rule = new WebRule("child", BrowserProvider.YouTube, WebRuleScope.YouTubeChannel, WebRuleDecision.Block, "handle:@blocked", "@blocked");
        foreach (var type in new[] { BrowserContentType.Channel, BrowserContentType.Video, BrowserContentType.ShortForm, BrowserContentType.Playable })
            Assert.False(WebPolicyEngine.Evaluate(new(BrowserProvider.YouTube, "www.youtube.com", "/watch", type, ChannelHandle: "@blocked"), [rule]).Allowed);
        Assert.True(WebPolicyEngine.Evaluate(new(BrowserProvider.YouTube, "www.youtube.com", "/playables/other", BrowserContentType.Playable, ChannelHandle: "@other"), [rule]).Allowed);
        Assert.True(WebPolicyEngine.Evaluate(new(BrowserProvider.YouTube, "www.youtube.com", "/playables/unknown", BrowserContentType.Playable), [rule]).Allowed);
    }

    [Fact]
    public void YouTubePlayableMatchesPublisherChannelIdAndLeavesUnknownPublisherUnmatched()
    {
        var rule = new WebRule("child", BrowserProvider.YouTube, WebRuleScope.YouTubeChannel, WebRuleDecision.Block, "id:UCsaygames", "SayGames");
        var blocked = WebPolicyEngine.Evaluate(new(BrowserProvider.YouTube, "www.youtube.com", "/playables/vehicle-masters", BrowserContentType.Playable, ChannelId: "UCsaygames"), [rule]);
        var unknown = WebPolicyEngine.Evaluate(new(BrowserProvider.YouTube, "www.youtube.com", "/playables/unknown", BrowserContentType.Playable), [rule]);

        Assert.False(blocked.Allowed);
        Assert.Equal("EXPLICIT_BLOCK", blocked.Reason);
        Assert.True(unknown.Allowed);
        Assert.Equal("NO_MATCH", unknown.Reason);
    }
    [Fact]
    public void SiteAndTikTokCreatorPoliciesAreDeterministic()
    {
        var rules = new[]
        {
            new WebRule("child", BrowserProvider.YouTube, WebRuleScope.Site, WebRuleDecision.Block, "youtube.com", "YouTube"),
            new WebRule("child", BrowserProvider.TikTok, WebRuleScope.TikTokCreator, WebRuleDecision.Block, "creator:@blocked", "@blocked")
        };
        Assert.False(WebPolicyEngine.Evaluate(new(BrowserProvider.YouTube, "youtube.com", "/", BrowserContentType.Site), rules).Allowed);
        Assert.False(WebPolicyEngine.Evaluate(new(BrowserProvider.TikTok, "tiktok.com", "/@blocked/video/1", BrowserContentType.Creator, TikTokCreator: "@blocked"), rules).Allowed);
    }

    [Fact]
    public void AvailabilityBypassIsOnlyTestModeAndIsNotPersistent()
    {
        var test = BrowserHostAvailabilityPolicy.ServiceUnavailable(true);
        var prod = BrowserHostAvailabilityPolicy.ServiceUnavailable(false);
        Assert.True(test.Allowed);
        Assert.Equal("Tuổi Thơ chưa kết nối.", test.Diagnostic);
        Assert.False(prod.Allowed);
        Assert.Equal("SERVICE_UNAVAILABLE", prod.Reason);
    }

    [Fact]
    public void PolicySyncCarriesNoArbitraryNavigationUrl()
    {
        var sync = new BrowserNavigationRequest("extension", "child", 7, BrowserProvider.GenericWeb, string.Empty, string.Empty, BrowserContentType.Unknown, IsPolicySync: true);
        Assert.True(BrowserNavigationValidator.IsValid(sync, "extension"));
        Assert.False(BrowserNavigationValidator.IsValid(sync with { Host = "poki.com" }, "extension"));
        Assert.False(BrowserNavigationValidator.IsValid(sync with { Provider = BrowserProvider.YouTube }, "extension"));
    }

    [Fact]
    public void DnrRuntimeDiagnosticIsBoundedAndCannotContainUrlData()
    {
        var request = new BrowserNavigationRequest("extension", "child", 7, BrowserProvider.GenericWeb, string.Empty, string.Empty, BrowserContentType.Unknown, IsDiagnosticProbe: true, DnrSyncState: "FAIL", DnrRuleCount: 1, CustomRuleCount: 1, DnrError: "RULE_LIMIT");
        Assert.True(BrowserNavigationValidator.IsValid(request, "extension"));
        Assert.False(BrowserNavigationValidator.IsValid(request with { DnrError = "https://example.com/private" }, "extension"));
        Assert.False(BrowserNavigationValidator.IsValid(request with { DnrRuleCount = 5001 }, "extension"));
    }

    [Fact]
    public void BrowserRequestRejectsWrongExtensionOversizeIdentityAndCannotCarryCommands()
    {
        var request = new BrowserNavigationRequest("wrong", "child", 7, BrowserProvider.YouTube, "youtube.com", "/watch", BrowserContentType.Video);
        Assert.False(BrowserNavigationValidator.IsValid(request, "right"));
        var invalidOwnerState = new BrowserNavigationRequest("right", "child", 7, BrowserProvider.YouTube, "youtube.com", "/shorts/1", BrowserContentType.ShortForm, OwnerState: "UNTRUSTED");
        Assert.False(BrowserNavigationValidator.IsValid(invalidOwnerState, "right"));
        var validShortOwnerState = new BrowserNavigationRequest("right", "child", 7, BrowserProvider.YouTube, "youtube.com", "/shorts/1", BrowserContentType.ShortForm, OwnerState: "SHORT_OWNER_FOUND", ShortContainer: "ytd-reel-video-renderer", OwnerCandidateCount: 1, OwnerSource: "ANCHOR");
        var nonShortOwnerState = validShortOwnerState with { ContentType = BrowserContentType.Video };
        Assert.True(BrowserNavigationValidator.IsValid(validShortOwnerState, "right"));
        Assert.False(BrowserNavigationValidator.IsValid(nonShortOwnerState, "right"));
        Assert.False(BrowserNavigationValidator.IsValid(validShortOwnerState with { OwnerSource = "UNTRUSTED" }, "right"));
        Assert.False(BrowserNavigationValidator.IsValid(validShortOwnerState with { ShortContainer = "<script>" }, "right"));
        Assert.False(BrowserNavigationValidator.IsValid(validShortOwnerState with { OwnerCandidateCount = 17 }, "right"));
        Assert.DoesNotContain(typeof(BrowserNavigationRequest).GetProperties(), property => property.Name.Contains("Action", StringComparison.OrdinalIgnoreCase) || property.Name.Contains("Command", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(WebRule).GetProperties(), property => property.Name.Contains("History", StringComparison.OrdinalIgnoreCase) || property.Name.Contains("Cookie", StringComparison.OrdinalIgnoreCase) || property.Name.Contains("Search", StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith("web:child:", new WebRule("child", BrowserProvider.GenericWeb, WebRuleScope.Domain, WebRuleDecision.Block, "domain:poki.com", "poki.com").StableId, StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("example.com", WebRuleScope.Domain, "domain:example.com")]
    [InlineData("www.example.com", WebRuleScope.Domain, "domain:www.example.com")]
    [InlineData("https://example.com/games/", WebRuleScope.PathPrefix, "path:example.com/games/")]
    [InlineData("http://example.com:80/games/", WebRuleScope.PathPrefix, "path:example.com/games/")]
    [InlineData("https://münich.example/", WebRuleScope.Domain, "domain:xn--mnich-kva.example")]
    public void CustomWebsiteInputsNormalizeSafely(string input, WebRuleScope scope, string expected)
    {
        Assert.True(WebIdentityNormalizer.TryNormalizeCustomWebsite(input, scope, out var identity));
        Assert.Equal(expected, identity.NormalizedKey);
    }

    [Theory]
    [InlineData("https://user:password@example.com/")]
    [InlineData("chrome://settings")]
    [InlineData("example.com/%")]
    [InlineData("not a host")]
    public void UnsafeOrMalformedCustomWebsiteInputsAreRejected(string input)
        => Assert.False(WebIdentityNormalizer.TryNormalizeCustomWebsite(input, WebRuleScope.Domain, out _));

    [Fact]
    public void CustomDomainCoversSubdomainsButNeverSuffixLookalikes()
    {
        var block = new WebRule("child", BrowserProvider.GenericWeb, WebRuleScope.Domain, WebRuleDecision.Block, "domain:example.com", "example.com");
        Assert.False(WebPolicyEngine.Evaluate(new(BrowserProvider.GenericWeb, "example.com", "/", BrowserContentType.Site), [block]).Allowed);
        Assert.False(WebPolicyEngine.Evaluate(new(BrowserProvider.GenericWeb, "play.example.com", "/", BrowserContentType.Site), [block]).Allowed);
        Assert.True(WebPolicyEngine.Evaluate(new(BrowserProvider.GenericWeb, "notexample.com", "/", BrowserContentType.Site), [block]).Allowed);
        Assert.True(WebPolicyEngine.Evaluate(new(BrowserProvider.GenericWeb, "example.com.evil.test", "/", BrowserContentType.Site), [block]).Allowed);
    }

    [Fact]
    public void CustomPathAndAllowExceptionUseDeterministicSpecificity()
    {
        var rules = new[]
        {
            new WebRule("child", BrowserProvider.GenericWeb, WebRuleScope.Domain, WebRuleDecision.Block, "domain:example.com", "example.com"),
            new WebRule("child", BrowserProvider.GenericWeb, WebRuleScope.PathPrefix, WebRuleDecision.Allow, "path:example.com/learning/", "example.com/learning/")
        };
        var allowed = WebPolicyEngine.Evaluate(new(BrowserProvider.GenericWeb, "example.com", "/learning/lesson", BrowserContentType.Site), rules);
        var blocked = WebPolicyEngine.Evaluate(new(BrowserProvider.GenericWeb, "example.com", "/games/abc", BrowserContentType.Site), rules);
        Assert.True(allowed.Allowed); Assert.Equal("CUSTOM_ALLOW", allowed.Reason);
        Assert.False(blocked.Allowed); Assert.Equal("CUSTOM_BLOCK", blocked.Reason);
    }
}
