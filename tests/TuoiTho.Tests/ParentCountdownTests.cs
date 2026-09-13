using TuoiTho.Core.Policy;
using TuoiTho.Core.Time;
using TuoiTho.Parent;
using TuoiTho.Service;

namespace TuoiTho.Tests;

public sealed class ParentCountdownTests
{
    [Fact]
    public async Task ExactThreeMinuteQuotaStartsAt180Seconds()
    {
        var fixture = Create();
        var status = await fixture.StatusAsync();
        Assert.Equal(180d, status.AllowedSeconds);
        Assert.Equal(180d, status.RemainingSeconds);
        Assert.Equal("00:03:00", ParentControlForm.FormatCountdown(status.RemainingSeconds));
    }

    [Theory]
    [InlineData(1, 179)]
    [InlineData(61, 119)]
    public async Task ActiveUsageProducesExactRemainingSeconds(int usedSeconds, int remainingSeconds)
    {
        var fixture = Create();
        await fixture.SaveUsageAsync(TimeSpan.FromSeconds(usedSeconds));
        var status = await fixture.StatusAsync();
        Assert.Equal(remainingSeconds, status.RemainingSeconds);
        Assert.Equal(remainingSeconds == 179 ? "00:02:59" : "00:01:59", ParentControlForm.FormatCountdown(status.RemainingSeconds));
    }

    [Fact]
    public async Task GrantAdds900ExactSecondsImmediately()
    {
        var fixture = Create();
        await fixture.SaveUsageAsync(TimeSpan.FromSeconds(1));
        var grant = await fixture.Service.ExecuteAsync(new(ParentControlAction.GrantMinutes, "child", 7, 15), "parent", fixture.Parents);
        Assert.True(grant.Accepted);
        Assert.Equal(1080d, grant.Status!.AllowedSeconds);
        Assert.Equal(1079d, grant.Status.RemainingSeconds);
    }

    [Fact]
    public async Task IdleHeartbeatLeavesRemainingSecondsUnchanged()
    {
        var fixture = Create();
        await fixture.SaveUsageAsync(TimeSpan.FromSeconds(1));
        var before = await fixture.StatusAsync();
        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddSeconds(60);
        await fixture.Usage.SaveAsync("child", new TimeTrackingCheckpoint(7, SessionActivityState.Idle, fixture.Clock.UtcNow, fixture.Clock.UtcNow.AddHours(-1)), []);
        var after = await fixture.StatusAsync();
        Assert.Equal(before.RemainingSeconds, after.RemainingSeconds);
    }

    [Fact]
    public async Task ExactQuotaExhaustionReportsZeroAndQuotaExhausted()
    {
        var fixture = Create();
        await fixture.SaveUsageAsync(TimeSpan.FromSeconds(180));
        var status = await fixture.StatusAsync();
        Assert.Equal(0d, status.RemainingSeconds);
        Assert.Equal("00:00:00", ParentControlForm.FormatCountdown(status.RemainingSeconds));
        Assert.Equal("QUOTA_EXHAUSTED", status.State);
    }

    [Fact]
    public async Task ResetM1RestoresExactlyThreeMinutes()
    {
        var fixture = Create(profileId: "m1-child", quotaMinutes: 30, parentLock: true, parentOverride: false);
        await fixture.SaveUsageAsync(TimeSpan.FromSeconds(120));
        await fixture.Store.AddGrantAsync("m1-child", new TemporaryGrant(15, fixture.Clock.UtcNow.AddHours(1)));
        var result = await fixture.Service.ExecuteAsync(new(ParentControlAction.ResetM1, "m1-child", 7), "parent", fixture.Parents);
        Assert.True(result.Accepted);
        Assert.Equal(180d, result.Status!.AllowedSeconds);
        Assert.Equal(180d, result.Status.RemainingSeconds);
        Assert.Equal("00:03:00", ParentControlForm.FormatCountdown(result.Status.RemainingSeconds));
        Assert.True(result.Status.TestMode);
    }

    private static Fixture Create(string profileId = "child", int quotaMinutes = 3, bool parentLock = false, bool parentOverride = false)
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 14, 9, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc);
        var policy = new DeviceTimePolicy(profileId, 7, quotaMinutes, [new AllowedUsageWindow(DayOfWeek.Monday, new TimeOnly(0, 0), new TimeOnly(23, 59))], DeviceTimePolicy.DefaultWarnings, parentLock, parentOverride, true) { ManagedUserSid = "S-1-5-21-child" };
        var store = new MemoryStore(policy);
        var usage = new FakeTimeUsageStore();
        return new Fixture(clock, store, usage, new ParentControlService(store, usage, clock, new DeviceTimePolicyEngine(clock), new PolicyChangeSignal()), profileId);
    }

    private sealed class Fixture(FakeClock clock, MemoryStore store, FakeTimeUsageStore usage, ParentControlService service, string profileId)
    {
        public FakeClock Clock { get; } = clock;
        public MemoryStore Store { get; } = store;
        public FakeTimeUsageStore Usage { get; } = usage;
        public ParentControlService Service { get; } = service;
        public HashSet<string> Parents { get; } = ["parent"];
        public Task<ParentControlStatus> StatusAsync() => GetStatusAsync(Service, profileId, Parents);
        public Task SaveUsageAsync(TimeSpan duration) => Usage.SaveAsync(profileId, new TimeTrackingCheckpoint(7, SessionActivityState.Active, Clock.UtcNow, Clock.UtcNow.AddHours(-1)), [new DailyUsageSlice(DateOnly.FromDateTime(Clock.UtcNow.UtcDateTime), duration)]);
    }

    private static async Task<ParentControlStatus> GetStatusAsync(ParentControlService service, string profileId, IReadOnlySet<string> parents)
    {
        var result = await service.ExecuteAsync(new(ParentControlAction.GetStatus, profileId, 7), "parent", parents);
        Assert.True(result.Accepted);
        return result.Status!;
    }

    private sealed class MemoryStore(DeviceTimePolicy policy) : IDeviceTimePolicyStore
    {
        private DeviceTimePolicy current = policy;
        private readonly List<TemporaryGrant> grants = [];
        public Task<DeviceTimePolicy?> LoadAsync(string profileId, CancellationToken cancellationToken = default) => Task.FromResult<DeviceTimePolicy?>(current);
        public Task SaveAsync(DeviceTimePolicy value, CancellationToken cancellationToken = default) { current = value; return Task.CompletedTask; }
        public Task AddGrantAsync(string profileId, TemporaryGrant grant, CancellationToken cancellationToken = default) { grants.Add(grant); return Task.CompletedTask; }
        public Task<IReadOnlyList<TemporaryGrant>> GetGrantsAsync(string profileId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TemporaryGrant>>(grants);
        public Task ClearGrantsAsync(string profileId, CancellationToken cancellationToken = default) { grants.Clear(); return Task.CompletedTask; }
    }
}