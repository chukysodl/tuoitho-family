using TuoiTho.Core.Policy;

namespace TuoiTho.Tests;

public sealed class M5RemoteSetupTests
{
    private static readonly string TestContentRoot = AppContext.BaseDirectory;

    [Fact]
    public void SetupIsFailStopAndWritesOnlyPublicRemoteSettingsToProtectedConfig()
    {
        var setup = File.ReadAllText(Path.Combine(TestContentRoot, "M5-REMOTE-SETUP.ps1"));
        Assert.Contains("Invoke-Supabase @('db', 'push')", setup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Invoke-Supabase @('functions', 'deploy', 'device-gateway'", setup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Invoke-Supabase @('functions', 'deploy', 'parent-gateway'", setup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SupabaseAnonKey = $publicKey", setup, StringComparison.Ordinal);
        Assert.Contains("TuoiTho\\RemoteControl", setup, StringComparison.Ordinal);
        Assert.Contains("remote-control.json", setup, StringComparison.Ordinal);
        Assert.DoesNotContain("SETX", setup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TestMode =", setup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ServiceRoleKey =", setup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("REMOTE DEVICE CONFIG: PASS", setup, StringComparison.Ordinal);
        Assert.Contains("M1-STOP.ps1", setup, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadinessScriptChecksRealRuntimeAndNeverPrintsProjectKey()
    {
        var check = File.ReadAllText(Path.Combine(TestContentRoot, "M5-REMOTE-CHECK.ps1"));
        foreach (var item in new[] { "Service running", "Remote Enabled", "Supabase URL configured", "Public key configured", "Device identity present", "Database reachable", "device-gateway reachable", "parent-gateway reachable", "Pairing path ready", "Status publication working", "Command polling working", "TestMode" })
            Assert.Contains(item, check, StringComparison.Ordinal);
        Assert.Contains("role -eq 'anon'", check, StringComparison.Ordinal);
        Assert.Contains("Test-PublicProjectKey $publicKey", check, StringComparison.Ordinal);
        Assert.Contains("M5 REMOTE CHECK: READY", check, StringComparison.Ordinal);
        Assert.Contains("M5 REMOTE CHECK: NOT READY", check, StringComparison.Ordinal);
        Assert.Contains("RemoteControl.Enabled is false or its config is missing.", check, StringComparison.Ordinal);
        Assert.Contains("No valid public anon/publishable key is configured; value hidden.", check, StringComparison.Ordinal);
        Assert.Contains("Service did not return the current TestMode state.", check, StringComparison.Ordinal);
        Assert.DoesNotContain("Write-Host $publicKey", check, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Write-Host $runtime", check, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PublicReadinessHealthDoesNotExposeSupabaseSecrets()
    {
        var gateway = File.ReadAllText(Path.Combine(TestContentRoot, "infra", "supabase", "functions", "device-gateway", "index.ts"));
        Assert.Contains("action === \"health\"", gateway, StringComparison.Ordinal);
        Assert.Contains("select(\"device_id\").limit(0)", gateway, StringComparison.Ordinal);
        Assert.Contains("database: \"ready\"", gateway, StringComparison.Ordinal);
        var guide = File.ReadAllText(Path.Combine(TestContentRoot, "M5-TEST-GUIDE.txt"));
        Assert.Contains("tắt Wi-Fi", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TestMode", guide, StringComparison.Ordinal);
        Assert.Contains("YouTube/TikTok", guide, StringComparison.Ordinal);
    }
}
