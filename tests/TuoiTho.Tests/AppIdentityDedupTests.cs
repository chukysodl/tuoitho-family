using TuoiTho.Core.Policy;
using TuoiTho.Parent;
using TuoiTho.Storage;

namespace TuoiTho.Tests;

public sealed class AppIdentityDedupTests
{
    [Fact]
    public void ParentUiDeduplicatesCaseInsensitiveExecutableIdentityAndKeepsNewest()
    {
        var older = new ParentObservedApp(AppIdentity.FromExecutablePath("C:\\Apps\\Claude.exe", "Claude old"), AppClassification.UserApplication, AppSimulationDecision.WouldAllow, "EXPLICIT_ALLOW", new DateTimeOffset(2026, 9, 16, 9, 0, 0, TimeSpan.Zero), AppRuleDecision.Allow);
        var newer = new ParentObservedApp(AppIdentity.FromExecutablePath("c:\\apps\\CLAUDE.exe", "Claude"), AppClassification.UserApplication, AppSimulationDecision.WouldBlock, "EXPLICIT_BLOCK", new DateTimeOffset(2026, 9, 16, 9, 5, 0, TimeSpan.Zero), AppRuleDecision.Block);
        var result = AppPolicyPanel.DeduplicateByExecutableIdentity([older, newer]);
        var item = Assert.Single(result);
        Assert.Equal("Claude", item.Identity.DisplayLabel);
        Assert.Equal(newer.LastSeenUtc, item.LastSeenUtc);
        Assert.Equal(AppRuleDecision.Block, item.ExplicitRule);
    }

    [Fact]
    public async Task StoreUpsertsObservationCaseInsensitivelyAndPreservesCurrentRule()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tuoitho-dedup-{Guid.NewGuid():N}.db");
        try
        {
            var database = new SqliteDatabase(path);
            await database.InitializeAsync();
            var store = new SqliteAppPolicyStore(database);
            var first = AppIdentity.FromExecutablePath("C:\\Apps\\Claude.exe", "Claude old");
            var latest = AppIdentity.FromExecutablePath("c:\\apps\\CLAUDE.exe", "Claude");
            var firstSeen = new DateTimeOffset(2026, 9, 16, 9, 0, 0, TimeSpan.Zero);
            var lastSeen = firstSeen.AddMinutes(5);
            await store.RecordObservationAsync(new ObservedApp("child", 7, first, firstSeen, firstSeen, AppClassification.UserApplication));
            await store.RecordObservationAsync(new ObservedApp("child", 7, latest, lastSeen, lastSeen, AppClassification.UserApplication));
            await store.SaveRuleAsync(new AppRule("child", first, AppRuleDecision.Block));

            var observed = Assert.Single(await new SqliteAppPolicyStore(new SqliteDatabase(path)).GetObservedAppsAsync("child", 7));
            Assert.Equal("Claude", observed.Identity.DisplayLabel);
            Assert.Equal(lastSeen, observed.LastSeenUtc);
            var rules = await store.GetRulesAsync("child");
            Assert.Equal(AppSimulationDecision.WouldBlock, AppPolicyEngine.Evaluate(latest, rules, DefaultAppPolicy.AllowUnknown).Decision);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}