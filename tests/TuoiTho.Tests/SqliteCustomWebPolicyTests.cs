using TuoiTho.Core.Policy;
using TuoiTho.Storage;

namespace TuoiTho.Tests;

public sealed class SqliteCustomWebPolicyTests
{
    [Fact]
    public async Task CustomRulesAndRevisionPersistAcrossReopen()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tuoitho-web-{Guid.NewGuid():N}.db");
        try
        {
            var database = new SqliteDatabase(path); await database.InitializeAsync();
            var store = new SqliteWebPolicyStore(database);
            var rule = new WebRule("child", BrowserProvider.GenericWeb, WebRuleScope.Domain, WebRuleDecision.Block, "domain:poki.com", "poki.com");
            await store.SaveRuleAsync(rule);
            var first = await store.GetSnapshotAsync("child");
            Assert.Equal(1, first.Revision); Assert.Single(first.Rules);
            await store.SaveRuleAsync(rule with { Decision = WebRuleDecision.Allow });
            await store.RemoveRuleAsync("child", BrowserProvider.GenericWeb, WebRuleScope.Domain, "domain:poki.com");
            var reopened = await new SqliteWebPolicyStore(new SqliteDatabase(path)).GetSnapshotAsync("child");
            Assert.Equal(3, reopened.Revision); Assert.Empty(reopened.Rules);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}