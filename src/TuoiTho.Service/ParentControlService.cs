using TuoiTho.Core.Policy;
using TuoiTho.Core.Time;

namespace TuoiTho.Service;

public sealed class ParentControlService : IDisposable
{
    private readonly IDeviceTimePolicyStore store;
    private readonly ITimeUsageStore usage;
    private readonly IClock clock;
    private readonly DeviceTimePolicyEngine engine;
    private readonly PolicyChangeSignal changes;
    private readonly ActivitySampleCache? activityCache;
    private readonly WindowsSessionEventSource? sessionEventSource;
    private readonly SessionTimeEngine? timeEngine;
    private readonly IAppPolicyStore? appPolicies;
    private readonly AppPolicyEngine? appPolicyEngine;
    private readonly SemaphoreSlim commandGate = new(1, 1);

    public ParentControlService(
        IDeviceTimePolicyStore store,
        ITimeUsageStore usage,
        IClock clock,
        DeviceTimePolicyEngine engine,
        PolicyChangeSignal changes,
        ActivitySampleCache? activityCache = null,
        WindowsSessionEventSource? sessionEventSource = null,
        SessionTimeEngine? timeEngine = null, IAppPolicyStore? appPolicies = null, AppPolicyEngine? appPolicyEngine = null)
    {
        this.store = store;
        this.usage = usage;
        this.clock = clock;
        this.engine = engine;
        this.changes = changes;
        this.activityCache = activityCache;
        this.sessionEventSource = sessionEventSource;
        this.timeEngine = timeEngine;
        this.appPolicies = appPolicies;
        this.appPolicyEngine = appPolicyEngine;
    }

    public ParentControlService(IDeviceTimePolicyStore store, IClock clock, PolicyChangeSignal changes)
        : this(store, new EmptyUsageStore(), clock, new DeviceTimePolicyEngine(clock), changes) { }

    private sealed class EmptyUsageStore : ITimeUsageStore
    {
        public Task<TimeTrackingCheckpoint?> LoadCheckpointAsync(string profileId, CancellationToken cancellationToken = default) => Task.FromResult<TimeTrackingCheckpoint?>(null);
        public Task SaveAsync(string profileId, TimeTrackingCheckpoint checkpoint, IReadOnlyList<DailyUsageSlice> slices, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResetUsageAsync(string profileId, DateOnly date, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TimeSpan> GetUsageAsync(string profileId, DateOnly date, CancellationToken cancellationToken = default) => Task.FromResult(TimeSpan.Zero);
    }

    public async Task<ParentControlResult> ExecuteAsync(ParentControlCommand command, string sid, IReadOnlySet<string> parents, CancellationToken token = default)
    {
        await commandGate.WaitAsync(token);
        try
        {
            if (string.IsNullOrWhiteSpace(sid) || (!parents.Contains(sid) && !string.Equals(sid, "S-1-5-18", StringComparison.OrdinalIgnoreCase))) return new(false, "UNAUTHORIZED");
            if (string.IsNullOrWhiteSpace(command.ProfileId) || command.ManagedSessionId < 0) return new(false, "INVALID_COMMAND");
            var policy = await store.LoadAsync(command.ProfileId, token);
            if (policy is null || policy.ManagedSessionId != command.ManagedSessionId) return new(false, "PROFILE_OR_SESSION_MISMATCH");
            if (command.Action is ParentControlAction.GetStatus or ParentControlAction.GetApps) return new(true, null, policy, await StatusAsync(policy, token));
            if (command.Action is ParentControlAction.AllowApp or ParentControlAction.BlockApp or ParentControlAction.RemoveAppRule) return await ExecuteAppAsync(command, policy, token);
            if (command.Action == ParentControlAction.ResetM1)
            {
                if (!policy.TestMode || policy.ProfileId != "m1-child") return new(false, "M1_RESET_NOT_ALLOWED");
                var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, clock.LocalTimeZone).DateTime);
                await ResetM1UsageAndRuntimeAsync(policy.ProfileId, date, token);
                await store.ClearGrantsAsync(policy.ProfileId, token);
                policy = policy with { DailyQuotaMinutes = 3, ParentOverride = false, ParentLock = false, TestMode = true };
                await store.SaveAsync(policy, token);
                changes.Notify();
                return new(true, null, policy, await StatusAsync(policy, token));
            }
            switch (command.Action)
            {
                case ParentControlAction.GrantMinutes:
                    if (command.Minutes is null or <= 0 or > 1440) return new(false, "INVALID_GRANT");
                    await store.AddGrantAsync(policy.ProfileId, new TemporaryGrant(command.Minutes.Value, EndOfLocalDay(clock.UtcNow, clock.LocalTimeZone)), token); break;
                case ParentControlAction.EmergencyOverride: policy = policy with { ParentOverride = true, ParentLock = false }; break;
                case ParentControlAction.ClearOverride: policy = policy with { ParentOverride = false }; break;
                case ParentControlAction.SetParentLock: policy = policy with { ParentLock = true, ParentOverride = false }; break;
                case ParentControlAction.ClearParentLock: policy = policy with { ParentLock = false }; break;
                default: return new(false, "UNKNOWN_ACTION");
            }
            await store.SaveAsync(policy, token); changes.Notify(); return new(true, null, policy, await StatusAsync(policy, token));
        }
        finally
        {
            commandGate.Release();
        }
    }

    private async Task<ParentControlResult> ExecuteAppAsync(ParentControlCommand command, DeviceTimePolicy policy, CancellationToken token)
    {
        if (appPolicies is null || appPolicyEngine is null || command.Application is null) return new(false, "INVALID_APP_COMMAND");
        if (command.Application.NormalizedExecutablePath.Length == 0) return new(false, "INVALID_APP_COMMAND");
        if (command.Action == ParentControlAction.RemoveAppRule) await appPolicies.RemoveRuleAsync(policy.ProfileId, command.Application, token);
        else await appPolicies.SaveRuleAsync(new AppRule(policy.ProfileId, command.Application, command.Action == ParentControlAction.AllowApp ? AppRuleDecision.Allow : AppRuleDecision.Block), token);
        changes.Notify();
        return new(true, null, policy, await StatusAsync(policy, token));
    }
    private async Task ResetM1UsageAndRuntimeAsync(string profileId, DateOnly date, CancellationToken token)
    {
        if (timeEngine is not null && await timeEngine.ResetForM1Async(date, () => sessionEventSource?.ReinitializeForM1Reset(), token))
        {
            return;
        }

        // The tracking host is not running yet. Advance the persisted baseline now so startup cannot recover a pre-reset interval.
        var snapshot = sessionEventSource?.ReinitializeForM1Reset();
        var persistedCheckpoint = snapshot is null
            ? await usage.LoadCheckpointAsync(profileId, token)
            : null;
        if (snapshot is not null)
        {
            await usage.ResetUsageAndSaveCheckpointAsync(profileId, date, new TimeTrackingCheckpoint(
                snapshot.SessionId,
                snapshot.State,
                snapshot.OccurredAtUtc,
                snapshot.BootStartedAtUtc), token);
        }
        else if (persistedCheckpoint is not null)
        {
            var baseline = new TimeTrackingCheckpoint(
                persistedCheckpoint.SessionId,
                persistedCheckpoint.State,
                clock.UtcNow,
                persistedCheckpoint.BootStartedAtUtc);
            await usage.ResetUsageAndSaveCheckpointAsync(profileId, date, baseline, token);
        }
        else
        {
            await usage.ResetUsageAsync(profileId, date, token);
        }
    }

    private async Task<ParentControlStatus> StatusAsync(DeviceTimePolicy policy, CancellationToken token)
    {
        var local = TimeZoneInfo.ConvertTime(clock.UtcNow, clock.LocalTimeZone);
        var used = await usage.GetUsageAsync(policy.ProfileId, DateOnly.FromDateTime(local.DateTime), token);
        var grants = await store.GetGrantsAsync(policy.ProfileId, token);
        var active = grants.Where(g => g.ExpiresAtUtc > clock.UtcNow).ToArray();
        var decision = engine.Evaluate(policy, used, active);
        var state = policy.ParentOverride ? "OVERRIDE" : decision.Allowed ? "ALLOWED" : PolicyReasonText.ToDisplayText(decision.Reason);
        var sample = activityCache?.GetFresh(policy.ProfileId, policy.ManagedSessionId, TimeSpan.FromSeconds(15));
        var checkpoint = await usage.LoadCheckpointAsync(policy.ProfileId, token);
        var runtime = sessionEventSource?.GetRuntimeDiagnostics();
        var activityState = runtime?.State.ToString().ToUpperInvariant() ?? checkpoint?.State.ToString().ToUpperInvariant() ?? "UNKNOWN";
        var diagnostics = new ParentActivityDiagnostics(sample is not null, activityState, sample?.IdleSeconds, activityCache?.LastReceivedAtUtc is { } at ? Math.Max(0, (clock.UtcNow - at).TotalSeconds) : null, policy.ManagedSessionId, used.TotalSeconds, checkpoint?.LastObservedAtUtc, runtime?.ExplicitLockLatched ?? false, runtime?.WtsConnectionState, runtime?.WtsSessionFlags, runtime?.SessionNotificationsAvailable ?? false, runtime?.NotificationError, runtime?.IdleThresholdMinutes ?? 5, sample?.RawInputAvailable == true ? "RAW_INPUT" : "UNAVAILABLE", sample?.WindowsIdleSeconds);
        var allowedSeconds = TimeSpan.FromMinutes(policy.DailyQuotaMinutes + active.Sum(g => g.Minutes)).TotalSeconds;
        var remainingSeconds = Math.Max(0, allowedSeconds - used.TotalSeconds);
        var apps = appPolicies is null || appPolicyEngine is null ? null : new ParentAppControlStatus(await appPolicies.GetDefaultPolicyAsync(policy.ProfileId, token), (await appPolicies.GetObservedAppsAsync(policy.ProfileId, policy.ManagedSessionId, token)).Select(a => { var evaluation = AppPolicyEngine.Evaluate(a.Identity, appPolicies.GetRulesAsync(policy.ProfileId, token).GetAwaiter().GetResult(), appPolicies.GetDefaultPolicyAsync(policy.ProfileId, token).GetAwaiter().GetResult()); return new ParentObservedApp(a.Identity, evaluation.Decision, evaluation.Reason); }).ToArray());
        return new(policy.ProfileId, policy.ManagedSessionId, policy.TestMode, (int)Math.Floor(used.TotalMinutes), policy.DailyQuotaMinutes, active.Sum(g => g.Minutes), decision.RemainingMinutes, state, diagnostics, allowedSeconds, remainingSeconds, apps);
    }

    public void Dispose() => commandGate.Dispose();

    private static DateTimeOffset EndOfLocalDay(DateTimeOffset utc, TimeZoneInfo zone) { var local = TimeZoneInfo.ConvertTime(utc, zone); var next = local.Date.AddDays(1); return new DateTimeOffset(next, zone.GetUtcOffset(next)).ToUniversalTime(); }
}