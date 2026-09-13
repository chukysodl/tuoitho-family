using TuoiTho.Core.Policy;
using TuoiTho.Core.Time;

namespace TuoiTho.Service;

public sealed class ParentControlService(IDeviceTimePolicyStore store, ITimeUsageStore usage, IClock clock, DeviceTimePolicyEngine engine, PolicyChangeSignal changes, ActivitySampleCache? activityCache = null, WindowsSessionEventSource? sessionEventSource = null)
{
    public ParentControlService(IDeviceTimePolicyStore store, IClock clock, PolicyChangeSignal changes) : this(store, new EmptyUsageStore(), clock, new DeviceTimePolicyEngine(clock), changes) { }
    private sealed class EmptyUsageStore : ITimeUsageStore
    {
        public Task<TimeTrackingCheckpoint?> LoadCheckpointAsync(string profileId, CancellationToken cancellationToken = default) => Task.FromResult<TimeTrackingCheckpoint?>(null);
        public Task SaveAsync(string profileId, TimeTrackingCheckpoint checkpoint, IReadOnlyList<DailyUsageSlice> slices, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResetUsageAsync(string profileId, DateOnly date, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TimeSpan> GetUsageAsync(string profileId, DateOnly date, CancellationToken cancellationToken = default) => Task.FromResult(TimeSpan.Zero);
    }

    public async Task<ParentControlResult> ExecuteAsync(ParentControlCommand command, string sid, IReadOnlySet<string> parents, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(sid) || (!parents.Contains(sid) && !string.Equals(sid, "S-1-5-18", StringComparison.OrdinalIgnoreCase))) return new(false, "UNAUTHORIZED");
        if (string.IsNullOrWhiteSpace(command.ProfileId) || command.ManagedSessionId < 0) return new(false, "INVALID_COMMAND");
        var policy = await store.LoadAsync(command.ProfileId, token);
        if (policy is null || policy.ManagedSessionId != command.ManagedSessionId) return new(false, "PROFILE_OR_SESSION_MISMATCH");
        if (command.Action == ParentControlAction.GetStatus) return new(true, null, policy, await StatusAsync(policy, token));
        if (command.Action == ParentControlAction.ResetM1)
        {
            if (!policy.TestMode || policy.ProfileId != "m1-child") return new(false, "M1_RESET_NOT_ALLOWED");
            var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, clock.LocalTimeZone).DateTime);
            await usage.ResetUsageAsync(policy.ProfileId, date, token);
            await store.ClearGrantsAsync(policy.ProfileId, token);
            policy = policy with { DailyQuotaMinutes = 3, ParentOverride = false, ParentLock = false, TestMode = true };
            await store.SaveAsync(policy, token);
            if (sessionEventSource?.ReinitializeForM1Reset() is { } snapshot)
            {
                await usage.SaveAsync(policy.ProfileId, new TimeTrackingCheckpoint(snapshot.SessionId, snapshot.State, snapshot.OccurredAtUtc, snapshot.BootStartedAtUtc), [], token);
            }
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
        var diagnostics = new ParentActivityDiagnostics(sample is not null, activityState, sample?.IdleSeconds, activityCache?.LastReceivedAtUtc is { } at ? Math.Max(0, (clock.UtcNow - at).TotalSeconds) : null, policy.ManagedSessionId, used.TotalSeconds, checkpoint?.LastObservedAtUtc, runtime?.ExplicitLockLatched ?? false, runtime?.WtsConnectionState, runtime?.WtsSessionFlags, runtime?.SessionNotificationsAvailable ?? false, runtime?.NotificationError);
        return new(policy.ProfileId, policy.ManagedSessionId, policy.TestMode, (int)Math.Floor(used.TotalMinutes), policy.DailyQuotaMinutes, active.Sum(g => g.Minutes), decision.RemainingMinutes, state, diagnostics);
    }

    private static DateTimeOffset EndOfLocalDay(DateTimeOffset utc, TimeZoneInfo zone) { var local = TimeZoneInfo.ConvertTime(utc, zone); var next = local.Date.AddDays(1); return new DateTimeOffset(next, zone.GetUtcOffset(next)).ToUniversalTime(); }
}