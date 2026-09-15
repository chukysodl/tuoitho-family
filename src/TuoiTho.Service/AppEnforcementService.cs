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
                // Inaccessible processes are never enforcement candidates.
            }
            finally
            {
                process.Dispose();
            }
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
        catch (TimeoutException)
        {
            return false;
        }
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
        catch (Exception)
        {
            return null;
        }
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

    public AppEnforcementAuditEvent? Latest
    {
        get { lock (gate) return events.Count == 0 ? null : events.Last(); }
    }
}

public sealed class AppEnforcementState
{
    private int armed;

    public bool IsArmed => Volatile.Read(ref armed) == 1;
    public AppEnforcementMode Mode => IsArmed ? AppEnforcementMode.ExplicitBlockOnly : AppEnforcementMode.Simulation;
    public void SetArmed(bool value) => Interlocked.Exchange(ref armed, value ? 1 : 0);
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
        "powershell.exe", "pwsh.exe", "cmd.exe", "windowsterminal.exe", "codex.exe", "claude.exe"
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await EvaluateOnceAsync(Environment.GetEnvironmentVariable("TuoiTho__ProfileId") ?? "m1-child", stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    // A missing/corrupt policy or a transient store failure must fail closed.
                    state.SetArmed(false);
                    AppEnforcementLog.Disabled(logger, exception);
                }
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            state.SetArmed(false);
        }
    }

    public async Task EvaluateOnceAsync(string profileId, CancellationToken cancellationToken = default)
    {
        if (!state.IsArmed) return;
        var policy = await policies.LoadAsync(profileId, cancellationToken);
        if (policy is null || !policy.TestMode)
        {
            state.SetArmed(false);
            return;
        }

        var rules = await appPolicies.GetRulesAsync(profileId, cancellationToken);
        var blocked = rules.Where(rule => rule.Enabled && rule.Decision == AppRuleDecision.Block)
            .ToDictionary(rule => rule.Identity.NormalizedExecutablePath, StringComparer.OrdinalIgnoreCase);
        if (blocked.Count == 0) return;

        var observed = await appPolicies.GetObservedAppsAsync(profileId, policy.ManagedSessionId, cancellationToken);
        foreach (var process in processSource.GetProcesses())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!blocked.TryGetValue(process.ExecutablePath, out var rule)) continue;

            var observation = observed.FirstOrDefault(item => string.Equals(item.Identity.NormalizedExecutablePath, process.ExecutablePath, StringComparison.OrdinalIgnoreCase));
            var classification = WindowsManagedSessionAppDiscovery.Classify(process.ExecutablePath, process.SessionId, policy.ManagedSessionId, hasVisibleTopLevelWindow: true);
            if (process.SessionId != policy.ManagedSessionId || observation?.Classification != AppClassification.UserApplication || classification != AppClassification.UserApplication || IsM2RecoveryTool(process.ExecutablePath))
            {
                Record(profileId, process, "EXPLICIT_BLOCK", AppEnforcementAuditAction.SkippedSafe);
                continue;
            }

            if (!string.Equals(rule.Identity.NormalizedExecutablePath, process.ExecutablePath, StringComparison.OrdinalIgnoreCase))
            {
                Record(profileId, process, "EXPLICIT_BLOCK", AppEnforcementAuditAction.SkippedSafe);
                continue;
            }

            // A parent can turn the switch off while this scan is in progress.
            if (!state.IsArmed) return;
            if (await processController.RequestGracefulCloseAsync(process, cancellationToken))
            {
                Record(profileId, process, "EXPLICIT_BLOCK", AppEnforcementAuditAction.GracefulClose);
                Record(profileId, process, "EXPLICIT_BLOCK", AppEnforcementAuditAction.RealBlock);
                continue;
            }

            if (!state.IsArmed) return;
            if (await processController.TerminateAsync(process, cancellationToken))
            {
                Record(profileId, process, "EXPLICIT_BLOCK", AppEnforcementAuditAction.Terminate);
                Record(profileId, process, "EXPLICIT_BLOCK", AppEnforcementAuditAction.RealBlock);
            }
            else
            {
                Record(profileId, process, "EXPLICIT_BLOCK", AppEnforcementAuditAction.SkippedSafe);
            }
        }
    }

    private static bool IsM2RecoveryTool(string executablePath) => M2RecoveryTools.Contains(Path.GetFileName(executablePath));

    private void Record(string profileId, RunningAppProcess process, string decision, AppEnforcementAuditAction action)
    {
        var item = new AppEnforcementAuditEvent(clock.UtcNow, profileId, process.SessionId, process.ExecutablePath, decision, action);
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