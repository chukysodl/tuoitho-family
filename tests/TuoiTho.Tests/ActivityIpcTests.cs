using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TuoiTho.Core.Time;
using TuoiTho.Service;

namespace TuoiTho.Tests;

public sealed class ActivityIpcTests
{
    [Fact]
    public void ActivityPipeAclAllowsOnlyChildAndLocalSystem()
    {
        var child = WindowsIdentity.GetCurrent().User!;
        var rules = ActivityPipeSecurity.Create(child).GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToArray();
        Assert.Contains(rules, rule => rule.IdentityReference == child && (rule.PipeAccessRights & PipeAccessRights.WriteData) != 0);
        Assert.Contains(rules, rule => rule.IdentityReference == new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
        foreach (var type in new[] { WellKnownSidType.WorldSid, WellKnownSidType.AuthenticatedUserSid, WellKnownSidType.BuiltinUsersSid })
            Assert.DoesNotContain(rules, rule => rule.IdentityReference == new SecurityIdentifier(type, null));
    }

    [Fact]
    public async Task RealPipeReadThenImpersonationAuthenticatesCurrentCaller()
    {
        var sid = WindowsIdentity.GetCurrent().User!;
        var name = $"TuoiTho.Activity.Test.{Guid.NewGuid():N}";
        await using var server = ActivityPipeSecurity.CreateServer(name, sid);
        var accepted = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            using var reader = new StreamReader(server, leaveOpen: true);
            var sample = await ActivitySampleListener.ReadSampleAsync(reader, CancellationToken.None);
            return (sample, ActivitySampleListener.GetAuthenticatedSid(server));
        });
        await using var client = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.Asynchronous);
        await client.ConnectAsync(1000);
        using var writer = new StreamWriter(client, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync("{\"ProfileId\":\"child\",\"SessionId\":7,\"ObservedAtUtc\":\"2026-09-12T00:00:00Z\",\"IdleSeconds\":1}");
        var result = await accepted.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("child", result.sample!.ProfileId);
        Assert.Equal(sid.Value, result.Item2);
    }

    [Fact]
    public async Task MalformedSampleFailsSafely()
    {
        using var reader = new StringReader("{invalid");
        Assert.Null(await ActivitySampleListener.ReadSampleAsync(reader, CancellationToken.None));
    }

    [Fact]
    public void CacheRejectsMismatchedAndStaleSamples()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc);
        var cache = new ActivitySampleCache(clock);
        Assert.False(cache.TryAccept(new("other", 7, clock.UtcNow, 1), "child", 7));
        Assert.False(cache.TryAccept(new("child", 8, clock.UtcNow, 1), "child", 7));
        Assert.True(cache.TryAccept(new("child", 7, clock.UtcNow, 1), "child", 7));
        Assert.NotNull(cache.GetFresh("child", 7, TimeSpan.FromSeconds(15)));
        clock.UtcNow = clock.UtcNow.AddSeconds(16);
        Assert.Null(cache.GetFresh("child", 7, TimeSpan.FromSeconds(15)));
    }

    [Fact]
    public void FreshAgentSampleOverridesUnreliableWtsLockedFlagWithoutExplicitLockEvent()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc);
        var cache = new ActivitySampleCache(clock);
        cache.TryAccept(new("child", 7, clock.UtcNow, 2), "child", 7);
        var open = new WindowsSessionActivity(SessionActivityState.Unknown, null, null, null, null, null, true, new(232, 1, 7, 0, 1, 0, 0, null, null, null));
        var provider = new AgentReportedSessionActivityProvider(_ => open, cache, clock, new WindowsTimeTrackingOptions { ProfileId = "child", IdleThresholdMinutes = 5, IdlePollIntervalSeconds = 5 });
        Assert.Equal(SessionActivityState.Active, provider.GetActivity(7).State);
        cache.TryAccept(new("child", 7, clock.UtcNow, 301), "child", 7);
        Assert.Equal(SessionActivityState.Idle, provider.GetActivity(7).State);
        var wtsLockedFlag = new AgentReportedSessionActivityProvider(_ => new(SessionActivityState.Locked, null, null, null, null, null, true, new(232, 1, 7, 0, 0, 0, 0, null, null, null)), cache, clock, new WindowsTimeTrackingOptions { ProfileId = "child", IdleThresholdMinutes = 5, IdlePollIntervalSeconds = 5 });
        cache.TryAccept(new("child", 7, clock.UtcNow, .2), "child", 7);
        Assert.Equal(SessionActivityState.Active, wtsLockedFlag.GetActivity(7).State);
    }
}