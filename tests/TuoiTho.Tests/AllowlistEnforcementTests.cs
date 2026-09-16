using Microsoft.Extensions.Logging.Abstractions;
using TuoiTho.Core.Policy;
using TuoiTho.Core.Time;
using TuoiTho.Service;

namespace TuoiTho.Tests;

public sealed class AllowlistEnforcementTests
{
    [Fact]
    public async Task UnknownUserApplicationClosesOnlyWhenAllowlistIsArmed()
    {
        var fixture = Create();
        await fixture.Enforcer.EvaluateOnceAsync("child");
        Assert.Empty(fixture.Controller.Terminated);
        fixture.State.ArmAllowlistForTest(fixture.Clock.UtcNow, TimeSpan.FromMinutes(10));
        await fixture.Enforcer.EvaluateOnceAsync("child");
        Assert.Equal([7], fixture.Controller.Terminated);
        Assert.Equal("BLOCK_UNKNOWN", fixture.Audit.Latest!.Decision);
    }

    [Theory]
    [InlineData(AppClassification.SystemProtected, "C:\\Windows\\System32\\svchost.exe")]
    [InlineData(AppClassification.BackgroundHelper, "C:\\Apps\\AudioHelper.exe")]
    [InlineData(AppClassification.UserApplication, "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe")]
    [InlineData(AppClassification.UserApplication, "C:\\Program Files\\TuoiTho\\TuoiTho.Parent.exe")]
    public async Task ProtectedBackgroundAndRecoveryAppsAreNeverClosed(AppClassification classification, string path)
    {
        var fixture = Create(path, classification);
        fixture.State.ArmAllowlistForTest(fixture.Clock.UtcNow, TimeSpan.FromMinutes(10));
        await fixture.Enforcer.EvaluateOnceAsync("child");
        Assert.Empty(fixture.Controller.Terminated);
    }

    [Fact]
    public async Task ForeignSessionIsNeverClosed()
    {
        var fixture = Create(process: new RunningAppProcess(7, 8, "C:\\Apps\\Paint.exe"));
        fixture.State.ArmAllowlistForTest(fixture.Clock.UtcNow, TimeSpan.FromMinutes(10));
        await fixture.Enforcer.EvaluateOnceAsync("child");
        Assert.Empty(fixture.Controller.Terminated);
    }

    [Fact]
    public async Task ExactCurrentAllowRuleKeepsApplicationRunning()
    {
        var fixture = Create();
        fixture.Apps.Rules.Add(new AppRule("child", fixture.Identity, AppRuleDecision.Allow));
        fixture.State.ArmAllowlistForTest(fixture.Clock.UtcNow, TimeSpan.FromMinutes(10));
        await fixture.Enforcer.EvaluateOnceAsync("child");
        Assert.Empty(fixture.Controller.Terminated);
    }

    [Fact]
    public async Task ExplicitBlockClosesInAllowlistMode()
    {
        var fixture = Create();
        fixture.Apps.Rules.Add(new AppRule("child", fixture.Identity, AppRuleDecision.Block));
        fixture.State.ArmAllowlistForTest(fixture.Clock.UtcNow, TimeSpan.FromMinutes(10));
        await fixture.Enforcer.EvaluateOnceAsync("child");
        Assert.Equal([7], fixture.Controller.Terminated);
        Assert.Equal("EXPLICIT_BLOCK", fixture.Audit.Latest!.Decision);
    }

    [Fact]
    public async Task SafetyLeaseAutoDisarmsAndDoesNotPersistAcrossNewRuntime()
    {
        var fixture = Create();
        fixture.State.ArmAllowlistForTest(fixture.Clock.UtcNow, TimeSpan.FromMinutes(10));
        fixture.Clock.UtcNow += TimeSpan.FromMinutes(10);
        await fixture.Enforcer.EvaluateOnceAsync("child");
        Assert.False(fixture.State.IsArmed);
        Assert.Empty(fixture.Controller.Terminated);
        Assert.False(new AppEnforcementState().IsArmed);
    }

    [Fact]
    public async Task PreflightRefusesUnresolvedAndBulkAllowsOnlyCurrentUserApps()
    {
        var fixture = Create();
        fixture.Discovery.Result = Discovery(fixture.Identity, AppClassification.UserApplication, AppIdentity.FromExecutablePath("C:\\Apps\\Helper.exe", "Helper"), AppClassification.BackgroundHelper);
        var rejected = await fixture.Controls.ExecuteAsync(new(ParentControlAction.EnableM2AllowlistEnforcement, "child", 7), "parent", new HashSet<string> { "parent" });
        Assert.False(rejected.Accepted);
        Assert.Contains("Còn 1 ứng dụng", rejected.Message);
        Assert.False(fixture.State.IsArmed);

        var bulk = await fixture.Controls.ExecuteAsync(new(ParentControlAction.BulkAllowRunningApps, "child", 7), "parent", new HashSet<string> { "parent" });
        Assert.True(bulk.Accepted);
        Assert.Single(fixture.Apps.Rules);
        Assert.Equal(AppRuleDecision.Allow, fixture.Apps.Rules.Single().Decision);
        var enabled = await fixture.Controls.ExecuteAsync(new(ParentControlAction.EnableM2AllowlistEnforcement, "child", 7), "parent", new HashSet<string> { "parent" });
        Assert.True(enabled.Accepted);
        Assert.Equal(AppEnforcementMode.AllowlistProduction, fixture.State.Mode);
    }

    [Fact]
    public async Task BulkAllowPreservesExplicitBlockAndUnauthorizedParentIsRejected()
    {
        var fixture = Create();
        fixture.Discovery.Result = Discovery(fixture.Identity, AppClassification.UserApplication);
        fixture.Apps.Rules.Add(new AppRule("child", fixture.Identity, AppRuleDecision.Block));
        var rejected = await fixture.Controls.ExecuteAsync(new(ParentControlAction.BulkAllowRunningApps, "child", 7), "child", new HashSet<string> { "parent" });
        Assert.False(rejected.Accepted);
        var bulk = await fixture.Controls.ExecuteAsync(new(ParentControlAction.BulkAllowRunningApps, "child", 7), "parent", new HashSet<string> { "parent" });
        Assert.True(bulk.Accepted);
        Assert.Single(fixture.Apps.Rules);
        Assert.Equal(AppRuleDecision.Block, fixture.Apps.Rules.Single().Decision);
    }

    [Fact]
    public async Task StoreFailureDisarmsRatherThanGuessing()
    {
        var fixture = Create();
        fixture.Apps.FailRules = true;
        fixture.State.ArmAllowlistForTest(fixture.Clock.UtcNow, TimeSpan.FromMinutes(10));
        await fixture.Enforcer.StartAsync(CancellationToken.None);
        try
        {
            var disarmed = await Task.Run(() => SpinWait.SpinUntil(() => !fixture.State.Snapshot(fixture.Clock.UtcNow).Armed, TimeSpan.FromSeconds(2)));
            Assert.True(disarmed);
            Assert.Empty(fixture.Controller.Terminated);
        }
        finally { await fixture.Enforcer.StopAsync(CancellationToken.None); }
    }

    private static Fixture Create(string path = "C:\\Apps\\Paint.exe", AppClassification classification = AppClassification.UserApplication, RunningAppProcess? process = null)
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 16, 9, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc);
        var identity = AppIdentity.FromExecutablePath(path, Path.GetFileNameWithoutExtension(path));
        var policy = new DeviceTimePolicy("child", 7, 30, [], DeviceTimePolicy.DefaultWarnings, false, false, true);
        var apps = new Apps([new ObservedApp("child", 7, identity, clock.UtcNow, clock.UtcNow, classification)]);
        var state = new AppEnforcementState();
        var audit = new AppEnforcementAuditTrail();
        var source = new Source([process ?? new RunningAppProcess(7, 7, path)]);
        var controller = new Controller();
        var policies = new Policies(policy);
        var enforcer = new AppEnforcementService(policies, apps, source, controller, state, audit, clock, NullLogger<AppEnforcementService>.Instance);
        var discovery = new DiscoverySource { Result = Discovery(identity, classification) };
        var controls = new ParentControlService(policies, new Usage(), clock, new DeviceTimePolicyEngine(clock), new PolicyChangeSignal(), appPolicies: apps, appPolicyEngine: new AppPolicyEngine(), appDiscovery: discovery, appEnforcement: state, appEnforcementAudit: audit);
        return new(clock, identity, apps, state, audit, controller, enforcer, controls, discovery);
    }

    private static AppDiscoveryResult Discovery(AppIdentity first, AppClassification firstClassification, AppIdentity? second = null, AppClassification secondClassification = AppClassification.BackgroundHelper)
    {
        var now = DateTimeOffset.UtcNow;
        var apps = new List<DiscoveredApplication> { new(first, firstClassification) };
        if (second is not null) apps.Add(new(second, secondClassification));
        return new(now, apps.Count, apps.Count, 0, null, apps, apps.Count(item => item.Classification == AppClassification.UserApplication), apps.Count(item => item.Classification == AppClassification.BackgroundHelper), apps.Count(item => item.Classification == AppClassification.SystemProtected));
    }

    private sealed record Fixture(FakeClock Clock, AppIdentity Identity, Apps Apps, AppEnforcementState State, AppEnforcementAuditTrail Audit, Controller Controller, AppEnforcementService Enforcer, ParentControlService Controls, DiscoverySource Discovery);
    private sealed class Source(IReadOnlyList<RunningAppProcess> processes) : IAppEnforcementProcessSource { public IReadOnlyList<RunningAppProcess> GetProcesses() => processes; }
    private sealed class Controller : IAppEnforcementProcessController { public List<int> Terminated { get; } = []; public Task<bool> RequestGracefulCloseAsync(RunningAppProcess process, CancellationToken cancellationToken = default) => Task.FromResult(false); public Task<bool> TerminateAsync(RunningAppProcess process, CancellationToken cancellationToken = default) { Terminated.Add(process.ProcessId); return Task.FromResult(true); } }
    private sealed class Policies(DeviceTimePolicy policy) : IDeviceTimePolicyStore { public Task<DeviceTimePolicy?> LoadAsync(string profileId, CancellationToken cancellationToken = default) => Task.FromResult<DeviceTimePolicy?>(policy); public Task SaveAsync(DeviceTimePolicy value, CancellationToken cancellationToken = default) => Task.CompletedTask; public Task AddGrantAsync(string profileId, TemporaryGrant grant, CancellationToken cancellationToken = default) => Task.CompletedTask; public Task ClearGrantsAsync(string profileId, CancellationToken cancellationToken = default) => Task.CompletedTask; public Task<IReadOnlyList<TemporaryGrant>> GetGrantsAsync(string profileId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TemporaryGrant>>([]); }
    private sealed class Usage : ITimeUsageStore { public Task<TimeTrackingCheckpoint?> LoadCheckpointAsync(string profileId, CancellationToken cancellationToken = default) => Task.FromResult<TimeTrackingCheckpoint?>(null); public Task SaveAsync(string profileId, TimeTrackingCheckpoint checkpoint, IReadOnlyList<DailyUsageSlice> slices, CancellationToken cancellationToken = default) => Task.CompletedTask; public Task ResetUsageAsync(string profileId, DateOnly date, CancellationToken cancellationToken = default) => Task.CompletedTask; public Task<TimeSpan> GetUsageAsync(string profileId, DateOnly date, CancellationToken cancellationToken = default) => Task.FromResult(TimeSpan.Zero); }
    private sealed class Apps(IReadOnlyList<ObservedApp> observed) : IAppPolicyStore { public List<AppRule> Rules { get; } = []; public bool FailRules { get; set; } public Task<DefaultAppPolicy> GetDefaultPolicyAsync(string profileId, CancellationToken cancellationToken = default) => Task.FromResult(DefaultAppPolicy.BlockUnknown); public Task SetDefaultPolicyAsync(string profileId, DefaultAppPolicy policy, CancellationToken cancellationToken = default) => Task.CompletedTask; public Task<IReadOnlyList<AppRule>> GetRulesAsync(string profileId, CancellationToken cancellationToken = default) => FailRules ? Task.FromException<IReadOnlyList<AppRule>>(new InvalidOperationException("db")) : Task.FromResult<IReadOnlyList<AppRule>>(Rules); public Task SaveRuleAsync(AppRule rule, CancellationToken cancellationToken = default) { Rules.RemoveAll(item => string.Equals(item.Identity.NormalizedExecutablePath, rule.Identity.NormalizedExecutablePath, StringComparison.OrdinalIgnoreCase)); Rules.Add(rule); return Task.CompletedTask; } public Task RemoveRuleAsync(string profileId, AppIdentity identity, CancellationToken cancellationToken = default) { Rules.RemoveAll(item => string.Equals(item.Identity.NormalizedExecutablePath, identity.NormalizedExecutablePath, StringComparison.OrdinalIgnoreCase)); return Task.CompletedTask; } public Task<IReadOnlyList<ObservedApp>> GetObservedAppsAsync(string profileId, int sessionId, CancellationToken cancellationToken = default) => Task.FromResult(observed); public Task RecordObservationAsync(ObservedApp observation, CancellationToken cancellationToken = default) => Task.CompletedTask; }
    private sealed class DiscoverySource : IManagedSessionAppDiscovery { public AppDiscoveryResult Result { get; set; } = new(DateTimeOffset.UtcNow, 0, 0, 0, null, []); public AppDiscoveryResult Discover(int sessionId) => Result; }
}