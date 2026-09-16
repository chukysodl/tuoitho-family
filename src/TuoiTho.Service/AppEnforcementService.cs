using System.Collections.Concurrent;
using System.Diagnostics;
using TuoiTho.Core.Policy;
using TuoiTho.Core.Time;

namespace TuoiTho.Service;

public sealed record RunningAppProcess(int ProcessId, int SessionId, string ExecutablePath);

public interface IAppEnforcementProcessSource
{
    IReadOnlyList<RunningAppProcess> GetProcesses();
}

public interface IAppEnforcementProcessController
{
    Task<bool> RequestGracefulCloseAsync(RunningAppProcess process, CancellationToken cancellationToken = default);
    Task<bool> TerminateAsync(RunningAppProcess process, CancellationToken cancellationToken = default);
}

public sealed class WindowsAppEnforcementProcessSource : IAppEnforcementProcessSource
{
    public IReadOnlyList<RunningAppProcess> GetProcesses()
    {
        var processes = new List<RunningAppProcess>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(path)) processes.Add(new RunningAppProcess(process.Id, process.SessionId, Path.GetFullPath(path)));
            }
            catch (Exception)
            {
                // Inaccessible processes are not candidates. We never guess an executable identity.
            }
            finally { process.Dispose(); }
        }
        return processes;
    }
}

public sealed class WindowsAppEnforcementProcessController : IAppEnforcementProcessController
{
    public async Task<bool> RequestGracefulCloseAsync(RunningAppProcess process, CancellationToken cancellationToken = default)
    {
        using var native = OpenMatchingProcess(process);
        if (native is null || native.HasExited || !native.CloseMainWindow()) return false;
        try
        {
            await native.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromMilliseconds(750), cancellationToken);
            return true;
        }
        catch (TimeoutException) { return false; }
    }

    public async Task<bool> TerminateAsync(RunningAppProcess process, CancellationToken cancellationToken = default)
    {
        using var native = OpenMatchingProcess(process);
        if (native is null || native.HasExited) return false;
        native.Kill(entireProcessTree: false);
        await native.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        return true;
    }

    private static Process? OpenMatchingProcess(RunningAppProcess expected)
    {
        try
        {
            var process = Process.GetProcessById(expected.ProcessId);
            if (process.HasExited || process.SessionId != expected.SessionId) { process.Dispose(); return null; }
            var path = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(path) || !string.Equals(Path.GetFullPath(path), expected.ExecutablePath, StringComparison.OrdinalIgnoreCase)) { process.Dispose(); return null; }
            return process;
        }
        catch (Exception) { return null; }
    }
}

public sealed class AppEnforcementAuditTrail
{
    private readonly object gate = new();
    private readonly Queue<AppEnforcementAuditEvent> events = new();
    public void Record(AppEnforcementAuditEvent item)
    {
        lock (gate)
        {
            events.Enqueue(item);
            while (events.Count > 20) events.Dequeue();
        }
    }
    public AppEnforcementAuditEvent? Latest { get { lock (gate) return events.Count == 0 ? null : events.Last(); } }
}

public sealed record AppEnforcementRuntimeState(AppEnforcementMode Mode, DateTimeOffset? LeaseExpiresAtUtc)
{
    public bool Armed => Mode != AppEnforcementMode.Simulation;
}

/// <summary>Volatile service-only switch. It is intentionally never persisted.</summary>
public sealed class AppEnforcementState
{
    private readonly object gate = new();
    private AppEnforcementMode mode = AppEnforcementMode.Simulation;
    private DateTimeOffset? leaseExpiresAtUtc;

    public bool IsArmed => Snapshot(DateTimeOffset.UtcNow).Armed;
    public AppEnforcementMode Mode => Snapshot(DateTimeOffset.UtcNow).Mode;

    public AppEnforcementRuntimeState Snapshot(DateTimeOffset now)
    {
        lock (gate)
        {
            if (leaseExpiresAtUtc is { } expiry && now >= expiry)
            {
                mode = AppEnforcementMode.Simulation;
                leaseExpiresAtUtc = null;
            }
            return new(mode, leaseExpiresAtUtc);
        }
    }

    // Compatibility for the verified B1 explicit-block command. It has no lease.
    public void SetArmed(bool value)
    {
        lock (gate)
        {
            mode = value ? AppEnforcementMode.ExplicitBlockOnly : AppEnforcementMode.Simulation;
            leaseExpiresAtUtc = null;
        }
    }

    public void ArmAllowlistForTest(DateTimeOffset now, TimeSpan lease)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lease, TimeSpan.Zero);
        lock (gate)
        {
            mode = AppEnforcementMode.AllowlistProduction;
            leaseExpiresAtUtc = now.Add(lease);
        }
    }

    public void Disarm() => SetArmed(false);
}

public sealed class AppEnforcementService(
    IDeviceTimePolicyStore policies,
    IAppPolicyStore appPolicies,
    IAppEnforcementProcessSource processSource,
    IAppEnforcementProcessController processController,
    AppEnforcementState state,
    AppEnforcementAuditTrail audit,
    IClock clock,
    ILogger<AppEnforcementService> logger) : BackgroundService
{
    private static readonly HashSet<string> M2RecoveryTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "powershell.exe", "pwsh.exe", "cmd.exe", "windowsterminal.exe", "taskmgr.exe", "codex.exe", "claude.exe"
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await EvaluateOnceAsync(Environment.GetEnvironmentVariable("TuoiTho__ProfileId") ?? "m1-child", stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception exception)
                {
                    // Store or policy failure means we cannot verify the allowlist: disarm instead of guessing.
                    state.Disarm();
                    AppEnforcementLog.Disabled(logger, exception);
                }
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { state.Disarm(); }
    }

    public async Task EvaluateOnceAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var runtime = state.Snapshot(clock.UtcNow);
        if (!runtime.Armed) return;
        var policy = await policies.LoadAsync(profileId, cancellationToken);
        if (policy is null || !policy.TestMode)
        {
            state.Disarm();
            return;
        }
        // A lease can expire while loading policy. This is intentionally checked again.
        runtime = state.Snapshot(clock.UtcNow);
        if (!runtime.Armed)
        {
            Record(policy, new RunningAppProcess(0, policy.ManagedSessionId, string.Empty), "LEASE_EXPIRED", AppEnforcementAuditAction.EnforcementOff);
            return;
        }

        var rules = await appPolicies.GetRulesAsync(profileId, cancellationToken);
        var observed = await appPolicies.GetObservedAppsAsync(profileId, policy.ManagedSessionId, cancellationToken);
        foreach (var process in processSource.GetProcesses())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.SessionId != policy.ManagedSessionId)
            {
                Record(policy, process, "SKIPPED_SAFE", AppEnforcementAuditAction.SkippedSafe);
                continue;
            }
            var observation = observed.FirstOrDefault(item => string.Equals(item.Identity.NormalizedExecutablePath, process.ExecutablePath, StringComparison.OrdinalIgnoreCase));
            var classification = WindowsManagedSessionAppDiscovery.Classify(process.ExecutablePath, process.SessionId, policy.ManagedSessionId, hasVisibleTopLevelWindow: true);
            if (observation?.Classification != AppClassification.UserApplication || classification != AppClassification.UserApplication || IsM2RecoveryTool(process.ExecutablePath))
            {
                Record(policy, process, "SKIPPED_SAFE", AppEnforcementAuditAction.SkippedSafe);
                continue;
            }

            var rule = rules.FirstOrDefault(item => item.Enabled && string.Equals(item.Identity.NormalizedExecutablePath, process.ExecutablePath, StringComparison.OrdinalIgnoreCase));
            var decision = rule?.Decision == AppRuleDecision.Block ? "EXPLICIT_BLOCK"
                : rule?.Decision == AppRuleDecision.Allow ? "ALLOW"
                : runtime.Mode == AppEnforcementMode.AllowlistProduction ? "BLOCK_UNKNOWN" : "ALLOW";
            if (decision == "ALLOW") continue;
            await EnforceIfStillEligibleAsync(policy, process, decision, cancellationToken);
        }
    }

    private async Task EnforceIfStillEligibleAsync(DeviceTimePolicy policy, RunningAppProcess process, string decision, CancellationToken token)
    {
        // Re-read mutable policy before a destructive action. A parent rule change wins immediately.
        var runtime = state.Snapshot(clock.UtcNow);
        if (!runtime.Armed || (decision == "BLOCK_UNKNOWN" && runtime.Mode != AppEnforcementMode.AllowlistProduction)) return;
        var rules = await appPolicies.GetRulesAsync(policy.ProfileId, token);
        var currentRule = rules.FirstOrDefault(item => item.Enabled && string.Equals(item.Identity.NormalizedExecutablePath, process.ExecutablePath, StringComparison.OrdinalIgnoreCase));
        if (currentRule?.Decision == AppRuleDecision.Allow) return;
        if (decision == "EXPLICIT_BLOCK" && currentRule?.Decision != AppRuleDecision.Block) return;
        if (decision == "BLOCK_UNKNOWN" && currentRule is not null) return;
        if (process.SessionId != policy.ManagedSessionId || IsM2RecoveryTool(process.ExecutablePath))
        {
            Record(policy, process, decision, AppEnforcementAuditAction.SkippedSafe);
            return;
        }
        var classification = WindowsManagedSessionAppDiscovery.Classify(process.ExecutablePath, process.SessionId, policy.ManagedSessionId, hasVisibleTopLevelWindow: true);
        if (classification != AppClassification.UserApplication)
        {
            Record(policy, process, decision, AppEnforcementAuditAction.SkippedSafe);
            return;
        }

        if (await processController.RequestGracefulCloseAsync(process, token))
        {
            Record(policy, process, decision, AppEnforcementAuditAction.GracefulClose);
            Record(policy, process, decision, AppEnforcementAuditAction.RealBlock);
            return;
        }
        runtime = state.Snapshot(clock.UtcNow);
        if (!runtime.Armed || (decision == "BLOCK_UNKNOWN" && runtime.Mode != AppEnforcementMode.AllowlistProduction)) return;
        if (await processController.TerminateAsync(process, token))
        {
            Record(policy, process, decision, AppEnforcementAuditAction.Terminate);
            Record(policy, process, decision, AppEnforcementAuditAction.RealBlock);
        }
        else Record(policy, process, decision, AppEnforcementAuditAction.SkippedSafe);
    }

    private static bool IsM2RecoveryTool(string executablePath) => M2RecoveryTools.Contains(Path.GetFileName(executablePath));
    private void Record(DeviceTimePolicy policy, RunningAppProcess process, string decision, AppEnforcementAuditAction action)
    {
        var item = new AppEnforcementAuditEvent(clock.UtcNow, policy.ProfileId, process.SessionId, process.ExecutablePath, decision, action);
        audit.Record(item);
        AppEnforcementLog.Event(logger, item.ProfileId, item.SessionId, item.ExecutablePath, item.Decision, item.Action);
    }
}

internal static partial class AppEnforcementLog
{
    [LoggerMessage(EventId = 2500, Level = LogLevel.Information, Message = "M2 app enforcement {Action} profile {ProfileId} session {SessionId} executable {ExecutablePath} decision {Decision}")]
    public static partial void Event(ILogger logger, string profileId, int sessionId, string executablePath, string decision, AppEnforcementAuditAction action);
    [LoggerMessage(EventId = 2501, Level = LogLevel.Error, Message = "M2 app enforcement disabled because its required state could not be verified.")]
    public static partial void Disabled(ILogger logger, Exception exception);
}