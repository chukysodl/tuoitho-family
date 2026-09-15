using Microsoft.Extensions.Logging.Abstractions;
using TuoiTho.Core.Policy;
using TuoiTho.Core.Time;
using TuoiTho.Service;

namespace TuoiTho.Tests;

public sealed class AppEnforcementTests
{
    [Fact]
    public void EnforcementDefaultsOffAndNewRuntimeStartsOff()
    {
        Assert.False(new AppEnforcementState().IsArmed);
        var state = new AppEnforcementState();
        state.SetArmed(true);
        Assert.True(state.IsArmed);
        Assert.False(new AppEnforcementState().IsArmed);
    }

    [Fact]
    public async Task ExplicitBlockOnlyActsWhenArmedAndStopsImmediatelyWhenTurnedOff()
    {
        var fixture = Create(blocked: true);
        await fixture.Service.EvaluateOnceAsync("child");
        Assert.Empty(fixture.Controller.Terminated);
        fixture.State.SetArmed(true);
        await fixture.Service.EvaluateOnceAsync("child");
        Assert.Equal([7], fixture.Controller.Terminated);
        fixture.State.SetArmed(false);
        await fixture.Service.EvaluateOnceAsync("child");
        Assert.Equal([7], fixture.Controller.Terminated);
    }

    [Fact]
    public async Task ExplicitAllowAndUnknownRemainAllowedInExplicitBlockOnlyMode()
    {
        var allowed = Create(blocked: false);
        allowed.State.SetArmed(true);
        await allowed.Service.EvaluateOnceAsync("child");
        Assert.Empty(allowed.Controller.Terminated);

        var unknown = Create(blocked: null);
        unknown.State.SetArmed(true);
        await unknown.Service.EvaluateOnceAsync("child");
        Assert.Empty(unknown.Controller.Terminated);
    }

    [Theory]
    [InlineData(AppClassification.SystemProtected, "C:\\Windows\\System32\\svchost.exe", 7)]
    [InlineData(AppClassification.BackgroundHelper, "C:\\Apps\\AudioHelper.exe", 7)]
    [InlineData(AppClassification.UserApplication, "C:\\Apps\\Calculator.exe", 8)]
    [InlineData(AppClassification.UserApplication, "C:\\Program Files\\TuoiTho\\TuoiTho.Parent.exe", 7)]
    [InlineData(AppClassification.UserApplication, "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe", 7)]
    public async Task UnsafeOrRecoveryCandidatesAreNeverEnforced(AppClassification classification, string path, int sessionId)
    {
        var fixture = Create(blocked: true, process: new RunningAppProcess(7, sessionId, path), observationPath: path, classification: classification);
        fixture.State.SetArmed(true);
        await fixture.Service.EvaluateOnceAsync("child");
        Assert.Empty(fixture.Controller.Terminated);
        Assert.Equal(AppEnforcementAuditAction.SkippedSafe, fixture.Audit.Latest!.Action);
    }

    [Fact]
    public async Task MismatchedIdentityAndUnrelatedPidAreNeverTargeted()
    {
        var target = "C:\\Apps\\Calculator.exe";
        var fixture = Create(blocked: true, processes: [new RunningAppProcess(7, 7, target), new RunningAppProcess(8, 7, "C:\\Apps\\Other.exe"), new RunningAppProcess(9, 8, target)], observationPath: target);
        fixture.State.SetArmed(true);
        await fixture.Service.EvaluateOnceAsync("child");
        Assert.Equal([7], fixture.Controller.Terminated);

        var mismatch = Create(blocked: true, process: new RunningAppProcess(7, 7, "C:\\Apps\\Other.exe"), observationPath: "C:\\Apps\\Calculator.exe");
        mismatch.State.SetArmed(true);
        await mismatch.Service.EvaluateOnceAsync("child");
        Assert.Empty(mismatch.Controller.Terminated);
    }

    [Fact]
    public async Task MissingOrNonTestPolicyDisarmsEnforcementFailClosed()
    {
        var fixture = Create(blocked: true);
        fixture.State.SetArmed(true);
        fixture.Policies.Value = null;
        await fixture.Service.EvaluateOnceAsync("child");
        Assert.False(fixture.State.IsArmed);
        Assert.Empty(fixture.Controller.Terminated);
    }

    [Fact]
    public async Task TurningOffDuringGracefulAttemptPreventsFallbackTermination()
    {
        var fixture = Create(blocked: true);
        fixture.State.SetArmed(true);
        fixture.Controller.OnGracefulAttempt = () => fixture.State.SetArmed(false);
        await fixture.Service.EvaluateOnceAsync("child");
        Assert.Empty(fixture.Controller.Terminated);
    }

    [Fact]
    public async Task SecuredParentCommandArmsOnlyForAuthorizedTestModeCaller()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow, TimeZoneInfo.Utc);
        var policy = new DeviceTimePolicy("child", 7, 30, [], DeviceTimePolicy.DefaultWarnings, false, false, true);
        var state = new AppEnforcementState();
        var audit = new AppEnforcementAuditTrail();
        using var controls = new ParentControlService(new Policies(policy), new Usage(), clock, new DeviceTimePolicyEngine(clock), new PolicyChangeSignal(), appEnforcement: state, appEnforcementAudit: audit);
        var rejected = await controls.ExecuteAsync(new(ParentControlAction.EnableM2AppEnforcement, "child", 7), "child", new HashSet<string> { "parent" });
        var accepted = await controls.ExecuteAsync(new(ParentControlAction.EnableM2AppEnforcement, "child", 7), "parent", new HashSet<string> { "parent" });
        Assert.False(rejected.Accepted);
        Assert.True(accepted.Accepted);
        Assert.True(state.IsArmed);
        Assert.Equal(AppEnforcementAuditAction.EnforcementOn, audit.Latest!.Action);
    }
    [Fact]
    public async Task AuditContainsOnlyMinimalEnforcementMetadata()
    {
        var fixture = Create(blocked: true);
        fixture.State.SetArmed(true);
        await fixture.Service.EvaluateOnceAsync("child");
        var names = typeof(AppEnforcementAuditEvent).GetProperties().Select(property => property.Name);
        Assert.DoesNotContain(names, name => name.Contains("Window", StringComparison.OrdinalIgnoreCase) || name.Contains("Key", StringComparison.OrdinalIgnoreCase) || name.Contains("Document", StringComparison.OrdinalIgnoreCase) || name.Contains("Url", StringComparison.OrdinalIgnoreCase) || name.Contains("Screen", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(AppEnforcementAuditAction.RealBlock, fixture.Audit.Latest!.Action);
    }

    private static Fixture Create(bool? blocked, RunningAppProcess? process = null, string? observationPath = null, AppClassification classification = AppClassification.UserApplication, IReadOnlyList<RunningAppProcess>? processes = null)
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow, TimeZoneInfo.Utc);
        var policy = new DeviceTimePolicy("child", 7, 30, [], DeviceTimePolicy.DefaultWarnings, false, false, true);
        var path = observationPath ?? process?.ExecutablePath ?? "C:\\Apps\\Calculator.exe";
        var identity = AppIdentity.FromExecutablePath(path, "Calculator");
        IReadOnlyList<AppRule> rules = blocked is null ? [] : [new AppRule("child", identity, blocked.Value ? AppRuleDecision.Block : AppRuleDecision.Allow)];
        var appsPolicy = new Apps(rules, [new ObservedApp("child", 7, identity, clock.UtcNow, clock.UtcNow, classification)]);
        var source = new Source(processes ?? [process ?? new RunningAppProcess(7, 7, path)]);
        var controller = new Controller();
        var state = new AppEnforcementState();
        var audit = new AppEnforcementAuditTrail();
        var policies = new Policies(policy);
        var service = new AppEnforcementService(policies, appsPolicy, source, controller, state, audit, clock, NullLogger<AppEnforcementService>.Instance);
        return new Fixture(service, state, audit, controller, policies);
    }

    private sealed record Fixture(AppEnforcementService Service, AppEnforcementState State, AppEnforcementAuditTrail Audit, Controller Controller, Policies Policies);
    private sealed class Source(IReadOnlyList<RunningAppProcess> processes) : IAppEnforcementProcessSource { public IReadOnlyList<RunningAppProcess> GetProcesses() => processes; }
    private sealed class Controller : IAppEnforcementProcessController
    {
        public List<int> Terminated { get; } = [];
        public Action? OnGracefulAttempt { get; set; }
        public Task<bool> RequestGracefulCloseAsync(RunningAppProcess process, CancellationToken cancellationToken = default)
        {
            OnGracefulAttempt?.Invoke();
            return Task.FromResult(false);
        }
        public Task<bool> TerminateAsync(RunningAppProcess process, CancellationToken cancellationToken = default) { Terminated.Add(process.ProcessId); return Task.FromResult(true); }
    }
    private sealed class Policies(DeviceTimePolicy policy) : IDeviceTimePolicyStore
    {
        public DeviceTimePolicy? Value { get; set; } = policy;
        public Task<DeviceTimePolicy?> LoadAsync(string profileId, CancellationToken cancellationToken = default) => Task.FromResult(Value);
        public Task SaveAsync(DeviceTimePolicy value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AddGrantAsync(string profileId, TemporaryGrant grant, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearGrantsAsync(string profileId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<TemporaryGrant>> GetGrantsAsync(string profileId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TemporaryGrant>>([]);
    }
    private sealed class Usage : ITimeUsageStore
    {
        public Task<TimeTrackingCheckpoint?> LoadCheckpointAsync(string profileId, CancellationToken cancellationToken = default) => Task.FromResult<TimeTrackingCheckpoint?>(null);
        public Task SaveAsync(string profileId, TimeTrackingCheckpoint checkpoint, IReadOnlyList<DailyUsageSlice> slices, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResetUsageAsync(string profileId, DateOnly date, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TimeSpan> GetUsageAsync(string profileId, DateOnly date, CancellationToken cancellationToken = default) => Task.FromResult(TimeSpan.Zero);
    }
    private sealed class Apps(IReadOnlyList<AppRule> rules, IReadOnlyList<ObservedApp> observed) : IAppPolicyStore
    {
        public Task<DefaultAppPolicy> GetDefaultPolicyAsync(string profileId, CancellationToken cancellationToken = default) => Task.FromResult(DefaultAppPolicy.BlockUnknown);
        public Task SetDefaultPolicyAsync(string profileId, DefaultAppPolicy policy, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<AppRule>> GetRulesAsync(string profileId, CancellationToken cancellationToken = default) => Task.FromResult(rules);
        public Task SaveRuleAsync(AppRule rule, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveRuleAsync(string profileId, AppIdentity identity, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ObservedApp>> GetObservedAppsAsync(string profileId, int sessionId, CancellationToken cancellationToken = default) => Task.FromResult(observed);
        public Task RecordObservationAsync(ObservedApp observation, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}