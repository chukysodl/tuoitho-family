using System.Text.Json;
using TuoiTho.BrowserHost;
using TuoiTho.Core.Policy;

namespace TuoiTho.Tests;

public sealed class BrowserDeploymentBootstrapperTests
{
    [Fact]
    public void FirstInstallGeneratesIdentityAndSecondInstallKeepsIt()
    {
        using var fixture = new DeploymentFixture();
        var first = BrowserDeploymentBootstrapper.Create(fixture.ExtensionDirectory, fixture.ConfigurationPath, fixture.InitialConfiguration);
        var second = BrowserDeploymentBootstrapper.Create(fixture.ExtensionDirectory, fixture.ConfigurationPath, fixture.InitialConfiguration);

        Assert.True(first.GeneratedNewKey);
        Assert.False(second.GeneratedNewKey);
        Assert.Equal(first.ExtensionId, second.ExtensionId);
        Assert.Equal(first.ExtensionId, BrowserControlConfiguration.Load(fixture.ConfigurationPath).ExtensionId);
        Assert.Equal(first.ExtensionId, BrowserDeploymentBootstrapper.GetExtensionId(fixture.ManifestPath));
    }

    [Fact]
    public void UpdateFromRawSourceManifestPreservesExistingIdentity()
    {
        using var fixture = new DeploymentFixture();
        var first = BrowserDeploymentBootstrapper.Create(fixture.ExtensionDirectory, fixture.ConfigurationPath, fixture.InitialConfiguration);
        var priorManifest = Path.Combine(fixture.Root, "prior-manifest.json");
        File.Copy(fixture.ManifestPath, priorManifest);
        var sourceManifest = Path.Combine(fixture.Root, "source-manifest.json");
        File.WriteAllText(sourceManifest, "{\"manifest_version\":3,\"version\":\"0.2.0\"}");

        File.Copy(sourceManifest, fixture.ManifestPath, overwrite: true);
        var preserved = BrowserDeploymentBootstrapper.PreserveKeyDuringUpdate(priorManifest, fixture.ManifestPath);
        var update = BrowserDeploymentBootstrapper.Create(fixture.ExtensionDirectory, fixture.ConfigurationPath, fixture.InitialConfiguration);

        Assert.Equal(first.ExtensionId, preserved);
        Assert.Equal(first.ExtensionId, update.ExtensionId);
        Assert.DoesNotContain("\"key\"", File.ReadAllText(sourceManifest), StringComparison.Ordinal);
        Assert.Equal(first.ExtensionId, BrowserDeploymentBootstrapper.GetExtensionId(fixture.ManifestPath));
    }

    [Fact]
    public void MismatchedStagedKeyAndConfigurationIsDetected()
    {
        using var fixture = new DeploymentFixture();
        BrowserDeploymentBootstrapper.Create(fixture.ExtensionDirectory, fixture.ConfigurationPath, fixture.InitialConfiguration);
        var invalidButComplete = fixture.InitialConfiguration with { ExtensionId = "abcdefghijklmnopabcdefghijklmnop" };
        File.WriteAllText(fixture.ConfigurationPath, JsonSerializer.Serialize(invalidButComplete));

        Assert.Throws<InvalidOperationException>(() => BrowserDeploymentBootstrapper.Create(fixture.ExtensionDirectory, fixture.ConfigurationPath, fixture.InitialConfiguration));
    }

    [Fact]
    public void RepairCreatesOneCanonicalReplacementWhenKeyIsLostAndPreservesRuntimeSettings()
    {
        using var fixture = new DeploymentFixture();
        var first = BrowserDeploymentBootstrapper.Create(fixture.ExtensionDirectory, fixture.ConfigurationPath, fixture.InitialConfiguration);
        File.WriteAllText(fixture.ManifestPath, "{\"manifest_version\":3,\"version\":\"0.2.0\"}");

        var repaired = BrowserDeploymentBootstrapper.Repair(fixture.ExtensionDirectory, fixture.ConfigurationPath, fixture.InitialConfiguration);
        var repeatedRepair = BrowserDeploymentBootstrapper.Repair(fixture.ExtensionDirectory, fixture.ConfigurationPath, fixture.InitialConfiguration);
        var configuration = BrowserControlConfiguration.Load(fixture.ConfigurationPath);

        Assert.True(repaired.GeneratedNewKey);
        Assert.False(repeatedRepair.GeneratedNewKey);
        Assert.NotEqual(first.ExtensionId, repaired.ExtensionId);
        Assert.Equal(repaired.ExtensionId, repeatedRepair.ExtensionId);
        Assert.Equal("m1-child", configuration.ProfileId);
        Assert.Equal(7, configuration.ManagedSessionId);
        Assert.Equal("S-1-5-21-test", configuration.ManagedUserSid);
        Assert.True(configuration.TestMode);
    }

    [Fact]
    public void DeploymentIdentityOperationsDoNotModifyPolicyData()
    {
        using var fixture = new DeploymentFixture();
        var policyPath = Path.Combine(fixture.Root, "policy-sentinel.sqlite");
        var original = new byte[] { 1, 2, 3, 4, 5 };
        File.WriteAllBytes(policyPath, original);

        BrowserDeploymentBootstrapper.Create(fixture.ExtensionDirectory, fixture.ConfigurationPath, fixture.InitialConfiguration);
        BrowserDeploymentBootstrapper.Repair(fixture.ExtensionDirectory, fixture.ConfigurationPath, fixture.InitialConfiguration);

        Assert.Equal(original, File.ReadAllBytes(policyPath));
    }

    [Fact]
    public void UpdateAndRepairScriptsProtectOneStableNativeOrigin()
    {
        var root = RepositoryRoot();
        var update = File.ReadAllText(Path.Combine(root, "scripts", "M4-BROWSER-UPDATE.ps1"));
        var repair = File.ReadAllText(Path.Combine(root, "scripts", "M4-BROWSER-REPAIR.ps1"));
        var common = File.ReadAllText(Path.Combine(root, "scripts", "M4-BROWSER-COMMON.ps1"));
        var check = File.ReadAllText(Path.Combine(root, "scripts", "M4-BROWSER-CHECK.ps1"));

        Assert.Contains("--preserve-extension-key", common, StringComparison.Ordinal);
        Assert.Contains("allowed_origins = @(\"chrome-extension://$ExtensionId/\")", common, StringComparison.Ordinal);
        Assert.Contains("IDENTITY: PASS", update, StringComparison.Ordinal);
        Assert.Contains("NEW CANONICAL ID", repair, StringComparison.Ordinal);
        Assert.Contains("$manifest.allowed_origins.Count -eq 1", check, StringComparison.Ordinal);
    }

    private static string RepositoryRoot() => TestRepositoryRoot.Get();

    private sealed class DeploymentFixture : IDisposable
    {
        public DeploymentFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "tuoitho-browser-deployment-" + Guid.NewGuid().ToString("N"));
            ExtensionDirectory = Path.Combine(Root, "Extension");
            Directory.CreateDirectory(ExtensionDirectory);
            ManifestPath = Path.Combine(ExtensionDirectory, "manifest.json");
            File.WriteAllText(ManifestPath, "{\"manifest_version\":3,\"version\":\"0.1.0\"}");
            ConfigurationPath = Path.Combine(Root, "browser-control.json");
            InitialConfiguration = new BrowserControlConfiguration("pending", "m1-child", 7, "S-1-5-21-test", TestMode: true);
        }

        public string Root { get; }
        public string ExtensionDirectory { get; }
        public string ManifestPath { get; }
        public string ConfigurationPath { get; }
        public BrowserControlConfiguration InitialConfiguration { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
