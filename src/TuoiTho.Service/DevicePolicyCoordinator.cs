using System.Threading.Channels;
using TuoiTho.Core.Policy;
using TuoiTho.Core.Time;

namespace TuoiTho.Service;

public sealed class PolicyChangeSignal
{
    private readonly Channel<bool> channel = Channel.CreateBounded<bool>(1);
    public void Notify() => channel.Writer.TryWrite(true);
    public Task WaitAsync(TimeSpan interval, CancellationToken token) => Task.WhenAny(channel.Reader.ReadAsync(token).AsTask(), Task.Delay(interval, token));
}

public interface IPolicyWarningPublisher { Task PublishAsync(SessionWarning warning, CancellationToken token = default); }
public sealed class LocalPolicyWarningPublisher(LocalSessionWarningPublisher publisher) : IPolicyWarningPublisher
{
    public Task PublishAsync(SessionWarning warning, CancellationToken token = default) => publisher.PublishAsync(warning, token);
}

public sealed class DevicePolicyCoordinator(
    IDeviceTimePolicyStore policies, ITimeUsageStore usage, IClock clock, DeviceTimePolicyEngine engine,
    SafeChildSessionEnforcer enforcer, IPolicyWarningPublisher publisher, PolicyChangeSignal changes)
{
    public async Task<SessionEnforcementResult?> EvaluateAsync(string profileId, CancellationToken token = default)
    {
        var policy = await policies.LoadAsync(profileId, token);
        if (policy is null) return null;
        var localNow = TimeZoneInfo.ConvertTime(clock.UtcNow, clock.LocalTimeZone);
        var used = await usage.GetUsageAsync(profileId, DateOnly.FromDateTime(localNow.DateTime), token);
        var grants = await policies.GetGrantsAsync(profileId, token);
        var decision = engine.Evaluate(policy, used, grants);
        try { await publisher.PublishAsync(new SessionWarning(profileId, policy.ManagedSessionId, decision.RemainingMinutes, decision.Warnings.Count == 0 ? null : decision.Warnings[0], decision.Reason), token); } catch (Exception exception) when (exception is IOException or OperationCanceledException && !token.IsCancellationRequested) { PolicyCoordinatorLog.WarningUnavailable(); }
        return await enforcer.EnforceAsync(policy, policy.ManagedSessionId, decision, token);
    }

    public async Task RunAsync(string profileId, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await EvaluateAsync(profileId, token);
            await changes.WaitAsync(TimeSpan.FromSeconds(30), token);
        }
    }
}
internal static class PolicyCoordinatorLog { public static void WarningUnavailable() { } }
