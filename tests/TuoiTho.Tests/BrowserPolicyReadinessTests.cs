using TuoiTho.Core.Policy;
using TuoiTho.Service;

namespace TuoiTho.Tests;

public sealed class BrowserPolicyReadinessTests
{
    [Fact]
    public void ReadinessIsMemoryOnlyAndRecoversFromDegradedToReady()
    {
        var cache = new BrowserRuntimeStatusCache();
        cache.SetBrowserPolicyReadiness("DEGRADED", "CONFIG_MISSING");
        var degraded = cache.Snapshot();
        Assert.Equal("DEGRADED", degraded.BrowserPolicyState);
        Assert.Equal("CONFIG_MISSING", degraded.BrowserPolicyError);
        cache.SetBrowserPolicyReadiness("READY");
        var ready = cache.Snapshot();
        Assert.Equal("READY", ready.BrowserPolicyState);
        Assert.Null(ready.BrowserPolicyError);
        Assert.DoesNotContain(typeof(ParentBrowserRuntimeStatus).GetProperties(), item => item.Name.Contains("Url", StringComparison.OrdinalIgnoreCase) || item.Name.Contains("History", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BrowserListenerAndM1ScriptsUseBoundedRecoveryAndExactPathCleanup()
    {
        var root = RepositoryRoot();
        var listener = File.ReadAllText(Path.Combine(root, "src", "TuoiTho.Service", "BrowserPolicyListener.cs"));
        var start = File.ReadAllText(Path.Combine(root, "scripts", "M1-START.ps1"));
        var stop = File.ReadAllText(Path.Combine(root, "scripts", "M1-STOP.ps1"));
        var host = File.ReadAllText(Path.Combine(root, "src", "TuoiTho.BrowserHost", "Program.cs"));
        Assert.Contains("RetryDelay", listener, StringComparison.Ordinal);
        Assert.Contains("CONFIG_MISSING", listener, StringComparison.Ordinal);
        Assert.Contains("PIPE_CREATE_FAILED", listener, StringComparison.Ordinal);
        Assert.Contains("Test-M1BrowserPolicyReady", start, StringComparison.Ordinal);
        Assert.Contains("BrowserPolicy: PASS", start, StringComparison.Ordinal);
        Assert.Contains("Stop-ExactPathFallback", stop, StringComparison.Ordinal);
        Assert.DoesNotContain("taskkill", stop, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dotnet.exe", stop, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TimeSpan.FromSeconds(5)", host, StringComparison.Ordinal);
        Assert.Contains("PIPE_CONNECT_TIMEOUT", host, StringComparison.Ordinal);
        Assert.Contains("SERVICE_RESPONSE_TIMEOUT", host, StringComparison.Ordinal);
        Assert.Contains("INVALID_RESPONSE", host, StringComparison.Ordinal);
    }

    private static string RepositoryRoot() => TestRepositoryRoot.Get();
}
