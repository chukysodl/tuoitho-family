using TuoiTho.Core.Policy;
using TuoiTho.Service;

namespace TuoiTho.Tests;

public sealed class BrowserRuntimeAndContentTests
{
    [Fact]
    public void RuntimeStatusIsEphemeralAndContainsNoNavigationData()
    {
        var cache = new BrowserRuntimeStatusCache();
        var initial = cache.Snapshot();
        Assert.False(initial.ExtensionConnected);
        Assert.Null(initial.LastCheckedAtUtc);
        cache.Record(new BrowserNavigationResponse(false, "EXPLICIT_BLOCK"));
        var status = cache.Snapshot();
        Assert.True(status.ExtensionConnected);
        Assert.Equal("CHẶN", status.LastResult);
        Assert.DoesNotContain(typeof(ParentBrowserRuntimeStatus).GetProperties(), property => property.Name.Contains("Url", StringComparison.OrdinalIgnoreCase) || property.Name.Contains("Path", StringComparison.OrdinalIgnoreCase) || property.Name.Contains("History", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ShortOwnerDiagnosticIsRuntimeOnlyAndClearedByTheNextNavigation()
    {
        var cache = new BrowserRuntimeStatusCache();
        cache.Record(new BrowserNavigationResponse(false, "EXPLICIT_BLOCK"), "SHORT_OWNER_FOUND", "ytd-reel-video-renderer", 1, "ANCHOR");
        var shortStatus = cache.Snapshot();
        Assert.Equal("SHORT_OWNER_FOUND", shortStatus.ShortOwnerState);
        Assert.Equal("ytd-reel-video-renderer", shortStatus.ShortContainer);
        Assert.Equal(1, shortStatus.ShortOwnerCandidateCount);
        Assert.Equal("ANCHOR", shortStatus.ShortOwnerSource);

        cache.Record(new BrowserNavigationResponse(true, "NO_MATCH"));
        Assert.Null(cache.Snapshot().ShortOwnerState);
        Assert.DoesNotContain(typeof(ParentBrowserRuntimeStatus).GetProperties(), property => property.Name.Contains("Url", StringComparison.OrdinalIgnoreCase) || property.Name.Contains("Path", StringComparison.OrdinalIgnoreCase) || property.Name.Contains("History", StringComparison.OrdinalIgnoreCase));
    }
    [Fact]
    public void YouTubeContentScriptUsesScopedOwnersAndSafeOverlay()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "browser-extension", "content", "youtube.js"));
        Assert.Contains("ytd-video-owner-renderer", script, StringComparison.Ordinal);
        Assert.Contains("ytd-reel-video-renderer[is-active]", script, StringComparison.Ordinal);
        Assert.Contains("/playables/", script, StringComparison.Ordinal);
        Assert.Contains("contentType: \"Playable\"", script, StringComparison.Ordinal);
        Assert.Contains("ytd-playables-player-page-renderer", script, StringComparison.Ordinal);
        Assert.Contains("root.querySelector(selector)", script, StringComparison.Ordinal);
        Assert.Contains("candidateBelongsToCurrentShort", script, StringComparison.Ordinal);
        Assert.Contains("VISIBLE_HANDLE_TEXT", script, StringComparison.Ordinal);
        Assert.Contains("ownerCandidateCount", script, StringComparison.Ordinal);
        Assert.Contains("channelIdFrom(root)", script, StringComparison.Ordinal);
        Assert.Contains("identity.contentType === \"Playable\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("document.querySelector('a[href^=\"/@\"]')", script, StringComparison.Ordinal);
        Assert.Contains("new MutationObserver", script, StringComparison.Ordinal);
        Assert.Contains("media.pause()", script, StringComparison.Ordinal);
        Assert.Contains("suppressBlockedInput", script, StringComparison.Ordinal);
        Assert.Contains("Nội dung này chưa được phụ huynh cho phép.", script, StringComparison.Ordinal);
        Assert.Contains("Tuổi Thơ chưa kết nối — kiểm soát Web đang tạm ngưng.", script, StringComparison.Ordinal);
        Assert.DoesNotContain("document.documentElement.innerHTML", script, StringComparison.Ordinal);
    }

    [Fact]
    public void M4CheckSeparatesRegistrationRuntimeAndPolicyProbe()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "M4-BROWSER-CHECK.ps1"));
        Assert.Contains("A. Native Host", script, StringComparison.Ordinal);
        Assert.Contains("B. Extension runtime", script, StringComparison.Ordinal);
        Assert.Contains("C. BrowserHost -> Service", script, StringComparison.Ordinal);
        Assert.Contains("D. Đánh giá chính sách", script, StringComparison.Ordinal);
        Assert.Contains("--service-probe", script, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "TuoiTho.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("TuoiTho.sln was not found.");
    }
}
