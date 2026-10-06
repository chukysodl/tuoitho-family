using TuoiTho.Core.Policy;

namespace TuoiTho.Tests;

public sealed class TimeGuardrailTests
{
    private static readonly DateTimeOffset Monday = new(2026, 10, 5, 5, 30, 0, TimeSpan.Zero);

    [Fact]
    public void BlockedScheduleSupportsOvernightAndAdjacentRanges()
    {
        var windows = new[]
        {
            new BlockedUsageWindow(new TimeOnly(0, 0), new TimeOnly(6, 0)),
            new BlockedUsageWindow(new TimeOnly(18, 0), new TimeOnly(21, 0)),
            new BlockedUsageWindow(new TimeOnly(23, 0), new TimeOnly(0, 0))
        };

        Assert.Null(BlockedScheduleValidator.Validate(windows));
        Assert.True(windows[0].Contains(new TimeOnly(5, 30)));
        Assert.True(windows[2].Contains(new TimeOnly(23, 30)));
        Assert.False(windows[2].Contains(new TimeOnly(22, 59)));
    }

    [Fact]
    public void BlockedScheduleRejectsOverlaps()
    {
        var error = BlockedScheduleValidator.Validate(
        [
            new BlockedUsageWindow(new TimeOnly(23, 0), new TimeOnly(6, 0)),
            new BlockedUsageWindow(new TimeOnly(5, 0), new TimeOnly(7, 0))
        ]);

        Assert.Contains("chồng lấn", error!);
    }

    [Fact]
    public void BlockedHoursTakePriorityOverBootAndOrdinarySchedule()
    {
        var clock = new FakeClock(Monday, TimeZoneInfo.Utc);
        var engine = new DeviceTimePolicyEngine(clock);
        var policy = Policy() with
        {
            StartupLimitMinutes = 60,
            BlockedWindows =
            [
                new BlockedUsageWindow(new TimeOnly(0, 0), new TimeOnly(6, 0))
            ]
        };

        var decision = engine.Evaluate(policy, TimeSpan.Zero, [], TimeSpan.FromMinutes(90));

        Assert.False(decision.Allowed);
        Assert.Equal(AccessDenyReason.BlockedTime, decision.Reason);
    }

    [Fact]
    public void StartupLimitLocksAfterConfiguredMinutes()
    {
        var clock = new FakeClock(Monday.AddHours(3), TimeZoneInfo.Utc);
        var engine = new DeviceTimePolicyEngine(clock);
        var policy = Policy() with { StartupLimitMinutes = 60 };

        Assert.True(engine.Evaluate(policy, TimeSpan.Zero, [], TimeSpan.FromMinutes(59)).Allowed);
        Assert.Equal(
            AccessDenyReason.StartupLimitExceeded,
            engine.Evaluate(policy, TimeSpan.Zero, [], TimeSpan.FromMinutes(60)).Reason);
    }

    [Fact]
    public void ParentOverrideBypassesBlockedHoursAndStartupLimit()
    {
        var clock = new FakeClock(Monday, TimeZoneInfo.Utc);
        var engine = new DeviceTimePolicyEngine(clock);
        var policy = Policy() with
        {
            ParentOverride = true,
            StartupLimitMinutes = 1,
            BlockedWindows =
            [
                new BlockedUsageWindow(new TimeOnly(0, 0), new TimeOnly(6, 0))
            ]
        };

        Assert.True(engine.Evaluate(policy, TimeSpan.FromHours(24), [], TimeSpan.FromHours(24)).Allowed);
    }

    private static DeviceTimePolicy Policy() =>
        new(
            "child",
            7,
            180,
            [new AllowedUsageWindow(DayOfWeek.Monday, TimeOnly.MinValue, new TimeOnly(23, 59))],
            DeviceTimePolicy.DefaultWarnings,
            false,
            false,
            false)
        {
            ManagedUserSid = "child"
        };
}
