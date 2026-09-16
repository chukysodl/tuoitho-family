using TuoiTho.Core.Policy;
using TuoiTho.Core.Time;

namespace TuoiTho.Service;

public sealed class ParentControlService : IDisposable
{
    private static readonly TimeSpan M2TestSafetyLease = TimeSpan.FromMinutes(10);
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
    private readonly IManagedSessionAppDiscovery? appDiscovery;
    private readonly AppEnforcementState? appEnforcement;
    private readonly AppEnforcementAuditTrail? appEnforcementAudit;
    private ParentAppDiscoveryDiagnostics? lastAppDiscovery;
    private readonly SemaphoreSlim commandGate = new(1, 1);

    public ParentControlService(IDeviceTimePolicyStore store, ITimeUsageStore usage, IClock clock, DeviceTimePolicyEngine engine, PolicyChangeSignal changes,
        ActivitySampleCache? activityCache = null, WindowsSessionEventSource? sessionEventSource = null, SessionTimeEngine? timeEngine = null,
        IAppPolicyStore? appPolicies = null, AppPolicyEngine? appPolicyEngine = null, IManagedSessionAppDiscovery? appDiscovery = null,
        AppEnforcementState? appEnforcement = null, AppEnforcementAuditTrail? appEnforcementAudit = null)
    {
        this.store = store; this.usage = usage; this.clock = clock; this.engine = engine; this.changes = changes;
        this.activityCache = activityCache; this.sessionEventSource = sessionEventSource; this.timeEngine = timeEngine;
        this.appPolicies = appPolicies; this.appPolicyEngine = appPolicyEngine; this.appDiscovery = appDiscovery;
        this.appEnforcement = appEnforcement; this.appEnforcementAudit = appEnforcementAudit;
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
            if (command.Action == ParentControlAction.RefreshApps) return await RefreshAppsAsync(policy, token);
            if (command.Action is ParentControlAction.EnableM2AppEnforcement or ParentControlAction.DisableM2AppEnforcement) return await SetExplicitBlockOnlyAsync(command.Action == ParentControlAction.EnableM2AppEnforcement, policy, token);
            if (command.Action is ParentControlAction.EnableM2AllowlistEnforcement or ParentControlAction.DisableM2AllowlistEnforcement) return await SetAllowlistAsync(command.Action == ParentControlAction.EnableM2AllowlistEnforcement, policy, token);
            if (command.Action == ParentControlAction.BulkAllowRunningApps) return await BulkAllowRunningAppsAsync(policy, token);
            if (command.Action is ParentControlAction.AllowApp or ParentControlAction.BlockApp or ParentControlAction.RemoveAppRule) return await ExecuteAppAsync(command, policy, token);
            if (command.Action == ParentControlAction.ResetM1)
            {
                if (!policy.TestMode || policy.ProfileId != "m1-child") return new(false, "M1_RESET_NOT_ALLOWED");
                appEnforcement?.Disarm();
                var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, clock.LocalTimeZone).DateTime);
                await ResetM1UsageAndRuntimeAsync(policy.ProfileId, date, token);
                await store.ClearGrantsAsync(policy.ProfileId, token);
                policy = policy with { DailyQuotaMinutes = 3, ParentOverride = false, ParentLock = false, TestMode = true };
                await store.SaveAsync(policy, token); changes.Notify();
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
        finally { commandGate.Release(); }
    }

    private async Task<ParentControlResult> SetExplicitBlockOnlyAsync(bool enabled, DeviceTimePolicy policy, CancellationToken token)
    {
        if (!policy.TestMode || appEnforcement is null || appEnforcementAudit is null) return new(false, "APP_ENFORCEMENT_TESTMODE_REQUIRED", policy, await StatusAsync(policy, token));
        appEnforcement.SetArmed(enabled);
        RecordEnforcement(policy, "EXPLICIT_BLOCK_ONLY", enabled ? AppEnforcementAuditAction.EnforcementOn : AppEnforcementAuditAction.EnforcementOff);
        changes.Notify();
        return new(true, null, policy, await StatusAsync(policy, token));
    }

    private async Task<ParentControlResult> SetAllowlistAsync(bool enabled, DeviceTimePolicy policy, CancellationToken token)
    {
        if (!policy.TestMode || appEnforcement is null || appEnforcementAudit is null) return new(false, "APP_ENFORCEMENT_TESTMODE_REQUIRED", policy, await StatusAsync(policy, token));
        if (!enabled)
        {
            appEnforcement.Disarm();
            RecordEnforcement(policy, "ALLOWLIST_TESTMODE", AppEnforcementAuditAction.EnforcementOff);
            changes.Notify();
            return new(true, null, policy, await StatusAsync(policy, token), "Đã tắt danh sách cho phép thử nghiệm.");
        }

        var scan = await ScanAndPersistAsync(policy, token);
        if (scan.DiscoveryError is not null) return new(false, "DISCOVERY_FAILED", policy, await StatusAsync(policy, token));
        var rules = await appPolicies!.GetRulesAsync(policy.ProfileId, token);
        var unresolved = scan.Applications.Where(app => app.Classification == AppClassification.UserApplication)
            .Where(app => !rules.Any(rule => rule.Enabled && string.Equals(rule.Identity.NormalizedExecutablePath, app.Identity.NormalizedExecutablePath, StringComparison.OrdinalIgnoreCase)))
            .Select(app => app.Identity.DisplayLabel).Distinct(StringComparer.CurrentCultureIgnoreCase).ToArray();
        if (unresolved.Length > 0)
        {
            var message = $"Còn {unresolved.Length} ứng dụng đang mở chưa được duyệt. Hãy Cho phép/Chặn chúng trước hoặc dùng ‘Cho phép các ứng dụng đang mở’.";
            return new(false, "ALLOWLIST_PREFLIGHT_UNRESOLVED", policy, await StatusAsync(policy, token), message);
        }
        appEnforcement.ArmAllowlistForTest(clock.UtcNow, M2TestSafetyLease);
        RecordEnforcement(policy, "ALLOWLIST_TESTMODE", AppEnforcementAuditAction.EnforcementOn);
        changes.Notify();
        return new(true, null, policy, await StatusAsync(policy, token), "Đã bật danh sách cho phép thử nghiệm trong 10 phút.");
    }

    private async Task<ParentControlResult> BulkAllowRunningAppsAsync(DeviceTimePolicy policy, CancellationToken token)
    {
        if (!policy.TestMode || appPolicies is null || appDiscovery is null) return new(false, "APP_ENFORCEMENT_TESTMODE_REQUIRED", policy, await StatusAsync(policy, token));
        var scan = await ScanAndPersistAsync(policy, token);
        if (scan.DiscoveryError is not null) return new(false, "DISCOVERY_FAILED", policy, await StatusAsync(policy, token));
        var rules = await appPolicies.GetRulesAsync(policy.ProfileId, token);
        var added = new List<string>();
        foreach (var app in scan.Applications.Where(item => item.Classification == AppClassification.UserApplication))
        {
            if (rules.Any(rule => rule.Enabled && string.Equals(rule.Identity.NormalizedExecutablePath, app.Identity.NormalizedExecutablePath, StringComparison.OrdinalIgnoreCase))) continue;
            await appPolicies.SaveRuleAsync(new AppRule(policy.ProfileId, app.Identity, AppRuleDecision.Allow), token);
            added.Add(app.Identity.DisplayLabel);
        }
        changes.Notify();
        var detail = added.Count == 0 ? "Không có ứng dụng đang mở nào cần cho phép thêm." : $"Đã cho phép {added.Count} ứng dụng: {string.Join(", ", added)}.";
        return new(true, null, policy, await StatusAsync(policy, token), detail);
    }

    private async Task<ParentControlResult> RefreshAppsAsync(DeviceTimePolicy policy, CancellationToken token)
    {
        try
        {
            var scan = await ScanAndPersistAsync(policy, token);
            return new(scan.DiscoveryError is null, scan.DiscoveryError is null ? null : "DISCOVERY_FAILED", policy, await StatusAsync(policy, token));
        }
        catch (Exception exception)
        {
            lastAppDiscovery = new ParentAppDiscoveryDiagnostics(clock.UtcNow, 0, 0, 0, exception.Message);
            return new(false, "DISCOVERY_FAILED", policy, await StatusAsync(policy, token));
        }
    }

    private async Task<AppDiscoveryResult> ScanAndPersistAsync(DeviceTimePolicy policy, CancellationToken token)
    {
        if (appPolicies is null || appDiscovery is null) throw new InvalidOperationException("App discovery is unavailable.");
        var scan = appDiscovery.Discover(policy.ManagedSessionId);
        foreach (var app in scan.Applications)
            await appPolicies.RecordObservationAsync(new ObservedApp(policy.ProfileId, policy.ManagedSessionId, app.Identity, scan.LastScanAtUtc, scan.LastScanAtUtc, app.Classification), token);
        lastAppDiscovery = new ParentAppDiscoveryDiagnostics(scan.LastScanAtUtc, scan.ProcessesExamined, scan.AppsDiscovered, scan.AppsSkippedInaccessible, scan.DiscoveryError, scan.UserApplications, scan.BackgroundHelpers, scan.SystemProtected);
        return scan;
    }

    private async Task<ParentControlResult> ExecuteAppAsync(ParentControlCommand command, DeviceTimePolicy policy, CancellationToken token)
    {
        if (appPolicies is null || appPolicyEngine is null || command.Application is null || command.Application.NormalizedExecutablePath.Length == 0) return new(false, "INVALID_APP_COMMAND");
        if (command.Action == ParentControlAction.RemoveAppRule) await appPolicies.RemoveRuleAsync(policy.ProfileId, command.Application, token);
        else await appPolicies.SaveRuleAsync(new AppRule(policy.ProfileId, command.Application, command.Action == ParentControlAction.AllowApp ? AppRuleDecision.Allow : AppRuleDecision.Block), token);
        changes.Notify(); return new(true, null, policy, await StatusAsync(policy, token));
    }

    private async Task ResetM1UsageAndRuntimeAsync(string profileId, DateOnly date, CancellationToken token)
    {
        if (timeEngine is not null && await timeEngine.ResetForM1Async(date, () => sessionEventSource?.ReinitializeForM1Reset(), token)) return;
        var snapshot = sessionEventSource?.ReinitializeForM1Reset();
        var persistedCheckpoint = snapshot is null ? await usage.LoadCheckpointAsync(profileId, token) : null;
        if (snapshot is not null) await usage.ResetUsageAndSaveCheckpointAsync(profileId, date, new TimeTrackingCheckpoint(snapshot.SessionId, snapshot.State, snapshot.OccurredAtUtc, snapshot.BootStartedAtUtc), token);
        else if (persistedCheckpoint is not null) await usage.ResetUsageAndSaveCheckpointAsync(profileId, date, new TimeTrackingCheckpoint(persistedCheckpoint.SessionId, persistedCheckpoint.State, clock.UtcNow, persistedCheckpoint.BootStartedAtUtc), token);
        else await usage.ResetUsageAsync(profileId, date, token);
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
        ParentAppControlStatus? apps = null;
        if (appPolicies is not null && appPolicyEngine is not null)
        {
            var defaultAppPolicy = await appPolicies.GetDefaultPolicyAsync(policy.ProfileId, token);
            var rules = await appPolicies.GetRulesAsync(policy.ProfileId, token);
            var observed = (await appPolicies.GetObservedAppsAsync(policy.ProfileId, policy.ManagedSessionId, token))
                .GroupBy(app => app.Identity.NormalizedExecutablePath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(app => app.LastSeenUtc).First()).ToArray();
            var enforcementRuntime = appEnforcement?.Snapshot(clock.UtcNow) ?? new AppEnforcementRuntimeState(AppEnforcementMode.Simulation, null);
            int? leaseSeconds = enforcementRuntime.LeaseExpiresAtUtc is { } expiry ? Math.Max(0, (int)Math.Ceiling((expiry - clock.UtcNow).TotalSeconds)) : null;
            var enforcement = new ParentAppEnforcementStatus(enforcementRuntime.Mode, enforcementRuntime.Armed, appEnforcementAudit?.Latest, enforcementRuntime.LeaseExpiresAtUtc, leaseSeconds);
            apps = new ParentAppControlStatus(defaultAppPolicy, observed.Select(a =>
            {
                var explicitRule = rules.FirstOrDefault(rule => rule.Enabled && string.Equals(rule.Identity.NormalizedExecutablePath, a.Identity.NormalizedExecutablePath, StringComparison.OrdinalIgnoreCase))?.Decision;
                var evaluation = AppPolicyEngine.Evaluate(a.Identity, rules, defaultAppPolicy, a.Classification);
                return new ParentObservedApp(a.Identity, a.Classification, evaluation.Decision, evaluation.Reason, a.LastSeenUtc, explicitRule, AppEnforcementText(a.Classification, explicitRule, enforcement));
            }).ToArray(), lastAppDiscovery, enforcement, policy.TestMode);
        }
        return new(policy.ProfileId, policy.ManagedSessionId, policy.TestMode, (int)Math.Floor(used.TotalMinutes), policy.DailyQuotaMinutes, active.Sum(g => g.Minutes), decision.RemainingMinutes, state, diagnostics, allowedSeconds, remainingSeconds, apps);
    }

    private void RecordEnforcement(DeviceTimePolicy policy, string decision, AppEnforcementAuditAction action)
        => appEnforcementAudit?.Record(new AppEnforcementAuditEvent(clock.UtcNow, policy.ProfileId, policy.ManagedSessionId, string.Empty, decision, action));

    public void Dispose() => commandGate.Dispose();

    private static string AppEnforcementText(AppClassification classification, AppRuleDecision? explicitRule, ParentAppEnforcementStatus enforcement)
    {
        if (classification != AppClassification.UserApplication) return "ĐƯỢC BẢO VỆ";
        if (explicitRule == AppRuleDecision.Allow) return "ĐƯỢC PHÉP";
        if (explicitRule == AppRuleDecision.Block) return enforcement.Armed ? "BỊ CHẶN BỞI PHỤ HUYNH" : "CHẶN — CHƯA BẬT THỰC THI";
        return enforcement.Mode == AppEnforcementMode.AllowlistProduction && enforcement.Armed ? "CHƯA ĐƯỢC CHO PHÉP — BỊ CHẶN" : "CHƯA DUYỆT — CHƯA BẬT THỰC THI";
    }
    private static DateTimeOffset EndOfLocalDay(DateTimeOffset utc, TimeZoneInfo zone) { var local = TimeZoneInfo.ConvertTime(utc, zone); var next = local.Date.AddDays(1); return new DateTimeOffset(next, zone.GetUtcOffset(next)).ToUniversalTime(); }
}