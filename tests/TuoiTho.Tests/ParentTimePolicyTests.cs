using TuoiTho.Core.Policy;
using TuoiTho.Core.Time;
using TuoiTho.Service;

namespace TuoiTho.Tests;

public sealed class ParentTimePolicyTests
{
    private static readonly DateTimeOffset Monday = new(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SavingQuotaAndSchedulePersistsWithoutResettingUsage()
    {
        var clock = new FakeClock(Monday, TimeZoneInfo.Utc);
        var policy = Policy(30);
        var store = new MemoryStore(policy);
        var usage = new FakeTimeUsageStore();
        await usage.SaveAsync("child", new TimeTrackingCheckpoint(7, SessionActivityState.Active, Monday, Monday), [new DailyUsageSlice(DateOnly.FromDateTime(Monday.Date), TimeSpan.FromMinutes(45))]);
        var service = new ParentControlService(store, usage, clock, new DeviceTimePolicyEngine(clock), new PolicyChangeSignal());
        var windows = new[] { new AllowedUsageWindow(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(10, 0)), new AllowedUsageWindow(DayOfWeek.Monday, new TimeOnly(19, 0), new TimeOnly(20, 30)) };

        var result = await service.ExecuteAsync(new(ParentControlAction.SaveTimePolicy, "child", 7, DailyQuotaMinutes: 60, Windows: windows), "parent", Parents);

        Assert.True(result.Accepted);
        Assert.Equal(45, result.Status!.UsedMinutes);
        Assert.Equal(60, result.Status.QuotaMinutes);
        Assert.Equal(15, result.Status.RemainingMinutes);
        Assert.Equal(windows, (await store.LoadAsync("child"))!.Windows);
    }

    [Fact]
    public async Task LoweringQuotaBelowAlreadyUsedExhaustsImmediatelyAndRaisingRecalculates()
    {
        var clock = new FakeClock(Monday, TimeZoneInfo.Utc);
        var store = new MemoryStore(Policy(90)); var usage = new FakeTimeUsageStore();
        await usage.SaveAsync("child", new TimeTrackingCheckpoint(7, SessionActivityState.Active, Monday, Monday), [new DailyUsageSlice(DateOnly.FromDateTime(Monday.Date), TimeSpan.FromMinutes(45))]);
        var service = new ParentControlService(store, usage, clock, new DeviceTimePolicyEngine(clock), new PolicyChangeSignal());
        var windows = Policy(90).Windows;
        var lower = await service.ExecuteAsync(new(ParentControlAction.SaveTimePolicy, "child", 7, DailyQuotaMinutes: 30, Windows: windows), "parent", Parents);
        Assert.Equal("QUOTA_EXHAUSTED", lower.Status!.State);
        var raised = await service.ExecuteAsync(new(ParentControlAction.SaveTimePolicy, "child", 7, DailyQuotaMinutes: 60, Windows: windows), "parent", Parents);
        Assert.Equal("ALLOWED", raised.Status!.State);
        Assert.Equal(15, raised.Status.RemainingMinutes);
    }

    [Fact]
    public void WeeklyScheduleSupportsTwoWindowsAndRejectsInvalidOrOverlappingValues()
    {
        var windows = new[] { new AllowedUsageWindow(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(10, 0)), new AllowedUsageWindow(DayOfWeek.Monday, new TimeOnly(19, 0), new TimeOnly(20, 30)) };
        Assert.Null(WeeklyScheduleValidator.Validate(windows));
        Assert.Contains("kết thúc", WeeklyScheduleValidator.Validate([new AllowedUsageWindow(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(9, 0))])!);
        Assert.Contains("chồng lấn", WeeklyScheduleValidator.Validate([new AllowedUsageWindow(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(10, 0)), new AllowedUsageWindow(DayOfWeek.Monday, new TimeOnly(9, 30), new TimeOnly(11, 0))])!);
        Assert.Contains("tối đa", WeeklyScheduleValidator.Validate([.. windows, new AllowedUsageWindow(DayOfWeek.Monday, new TimeOnly(21, 0), new TimeOnly(22, 0))])!);
    }

    [Fact]
    public void EngineUsesLocalDayBoundariesAndDeterministicPriority()
    {
        var clock = new FakeClock(Monday, TimeZoneInfo.Utc); var engine = new DeviceTimePolicyEngine(clock);
        var windows = new[] { new AllowedUsageWindow(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(10, 0)) };
        Assert.True(engine.Evaluate(Policy(30) with { Windows = windows }, TimeSpan.Zero, []).Allowed);
        clock.UtcNow = Monday.AddHours(2);
        Assert.Equal(AccessDenyReason.OutsideSchedule, engine.Evaluate(Policy(30) with { Windows = windows }, TimeSpan.FromMinutes(30), []).Reason);
        Assert.Equal(AccessDenyReason.ParentLock, engine.Evaluate(Policy(30) with { Windows = windows, ParentLock = true }, TimeSpan.FromMinutes(30), []).Reason);
        Assert.True(engine.Evaluate(Policy(30) with { Windows = windows, ParentOverride = true }, TimeSpan.FromMinutes(30), []).Allowed);
        clock.UtcNow = Monday.AddDays(1);
        Assert.Equal(AccessDenyReason.OutsideSchedule, engine.Evaluate(Policy(30) with { Windows = windows }, TimeSpan.Zero, []).Reason);
    }

    [Theory]
    [InlineData("ALLOWED", "Được phép sử dụng")]
    [InlineData("PARENT_LOCK", "Đã khóa bởi phụ huynh")]
    [InlineData("QUOTA_EXHAUSTED", "Đã hết thời gian hôm nay")]
    [InlineData("OUTSIDE_SCHEDULE", "Ngoài khung giờ được phép")]
    public void ParentStatesMapToFriendlyVietnamese(string state, string expected) => Assert.Equal(expected, ParentPolicyStateText.ToVietnamese(state));

    private static DeviceTimePolicy Policy(int quota) => new("child", 7, quota, [new AllowedUsageWindow(DayOfWeek.Monday, TimeOnly.MinValue, new TimeOnly(23, 59))], DeviceTimePolicy.DefaultWarnings, false, false, true) { ManagedUserSid = "child" };
    private static readonly IReadOnlySet<string> Parents = new HashSet<string> { "parent" };

    private sealed class MemoryStore(DeviceTimePolicy policy) : IDeviceTimePolicyStore
    {
        private DeviceTimePolicy value = policy;
        public Task<DeviceTimePolicy?> LoadAsync(string profileId, CancellationToken cancellationToken = default) => Task.FromResult<DeviceTimePolicy?>(value);
        public Task SaveAsync(DeviceTimePolicy policy, CancellationToken cancellationToken = default) { value = policy; return Task.CompletedTask; }
        public Task AddGrantAsync(string profileId, TemporaryGrant grant, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<TemporaryGrant>> GetGrantsAsync(string profileId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TemporaryGrant>>([]);
    }
}