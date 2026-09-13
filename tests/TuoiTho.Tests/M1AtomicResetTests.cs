using TuoiTho.Core.Policy;
using TuoiTho.Core.Time;
using TuoiTho.Service;

namespace TuoiTho.Tests;

public sealed class M1AtomicResetTests
{
    [Fact]
    public async Task ResetM1CompletesOnlyAfterUsageGrantsAndLiveBaselineAreReset()
    {
        var start = new DateTimeOffset(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);
        var resetAt = start.AddMinutes(121);
        var clock = new FakeClock(resetAt, TimeZoneInfo.Utc);
        var usage = new FakeTimeUsageStore();
        using var tracking = new SessionTimeEngine(usage, clock, "m1-child");
        await tracking.InitializeAsync(Snapshot(SessionActivityState.Active, start));
        await tracking.ApplyObservationAsync(Snapshot(SessionActivityState.Active, start.AddMinutes(120)));
        await usage.SaveAsync("other-child", new TimeTrackingCheckpoint(8, SessionActivityState.Active, resetAt, start), [new DailyUsageSlice(new DateOnly(2026, 9, 14), TimeSpan.FromMinutes(9))]);

        var policy = new DeviceTimePolicy("m1-child", 7, 30, [new AllowedUsageWindow(DayOfWeek.Monday, TimeOnly.MinValue, new TimeOnly(23, 59))], DeviceTimePolicy.DefaultWarnings, true, true, true)
        {
            ManagedUserSid = "S-1-5-21-child"
        };
        var policies = new MemoryPolicyStore(policy, [new TemporaryGrant(30, resetAt.AddHours(1))]);
        var service = new ParentControlService(policies, usage, clock, new DeviceTimePolicyEngine(clock), new PolicyChangeSignal(), timeEngine: tracking);

        var result = await service.ExecuteAsync(new ParentControlCommand(ParentControlAction.ResetM1, "m1-child", 7), "parent", new HashSet<string> { "parent" });

        Assert.True(result.Accepted);
        Assert.Equal(0, result.Status!.UsedMinutes);
        Assert.Equal(3, result.Status.QuotaMinutes);
        Assert.Equal(0, result.Status.GrantMinutes);
        Assert.Equal(180d, result.Status.RemainingSeconds);
        Assert.Equal("ALLOWED", result.Status.State);
        Assert.True(result.Policy!.TestMode);
        Assert.False(result.Policy.ParentOverride);
        Assert.False(result.Policy.ParentLock);
        Assert.Empty(await policies.GetGrantsAsync("m1-child"));
        Assert.Equal(TimeSpan.Zero, await usage.GetUsageAsync("m1-child", new DateOnly(2026, 9, 14)));
        var baseline = (await usage.LoadCheckpointAsync("m1-child"))!;
        Assert.Equal(resetAt, baseline.LastObservedAtUtc);
        Assert.Equal(SessionActivityState.Active, baseline.State);
        Assert.Equal(TimeSpan.FromMinutes(9), await usage.GetUsageAsync("other-child", new DateOnly(2026, 9, 14)));

        await tracking.ApplyObservationAsync(Snapshot(SessionActivityState.Active, resetAt.AddSeconds(5)));
        Assert.Equal(TimeSpan.FromSeconds(5), await usage.GetUsageAsync("m1-child", new DateOnly(2026, 9, 14)));
    }

    private static SessionSnapshot Snapshot(SessionActivityState state, DateTimeOffset at) => new(7, state, at, at.AddHours(-1));

    private sealed class MemoryPolicyStore(DeviceTimePolicy policy, IReadOnlyList<TemporaryGrant> grants) : IDeviceTimePolicyStore
    {
        private DeviceTimePolicy current = policy;
        private readonly List<TemporaryGrant> activeGrants = [.. grants];
        public Task<DeviceTimePolicy?> LoadAsync(string profileId, CancellationToken cancellationToken = default) => Task.FromResult<DeviceTimePolicy?>(profileId == current.ProfileId ? current : null);
        public Task SaveAsync(DeviceTimePolicy value, CancellationToken cancellationToken = default) { current = value; return Task.CompletedTask; }
        public Task AddGrantAsync(string profileId, TemporaryGrant grant, CancellationToken cancellationToken = default) { activeGrants.Add(grant); return Task.CompletedTask; }
        public Task<IReadOnlyList<TemporaryGrant>> GetGrantsAsync(string profileId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TemporaryGrant>>(profileId == current.ProfileId ? activeGrants : []);
        public Task ClearGrantsAsync(string profileId, CancellationToken cancellationToken = default) { if (profileId == current.ProfileId) activeGrants.Clear(); return Task.CompletedTask; }
    }
}