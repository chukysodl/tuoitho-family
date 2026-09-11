using TuoiTho.Core.Time;

namespace TuoiTho.Tests;

public sealed class SessionTimeEngineTests
{
    [Fact]
    public async Task ActiveTimeAccumulatesUntilSafeShutdown()
    {
        var start = Utc(2026, 9, 11, 9, 0);
        var clock = new FakeClock(start, TimeZoneInfo.Utc);
        var store = new FakeTimeUsageStore();
        using var engine = new SessionTimeEngine(store, clock, "child-1");

        await engine.InitializeAsync(Snapshot(SessionActivityState.Active, start));
        clock.UtcNow = start.AddMinutes(45);
        await engine.StopAsync();

        Assert.Equal(TimeSpan.FromMinutes(45), await store.GetUsageAsync("child-1", new DateOnly(2026, 9, 11)));
    }

    [Fact]
    public async Task LockAndUnlockExcludeLockedTime()
    {
        var start = Utc(2026, 9, 11, 9, 0);
        var clock = new FakeClock(start, TimeZoneInfo.Utc);
        var store = new FakeTimeUsageStore();
        using var engine = new SessionTimeEngine(store, clock, "child-1");

        await engine.InitializeAsync(Snapshot(SessionActivityState.Active, start));
        await engine.ApplyObservationAsync(Snapshot(SessionActivityState.Locked, start.AddMinutes(10)));
        await engine.ApplyObservationAsync(Snapshot(SessionActivityState.Active, start.AddMinutes(20)));
        clock.UtcNow = start.AddMinutes(30);
        await engine.StopAsync();

        Assert.Equal(TimeSpan.FromMinutes(20), await store.GetUsageAsync("child-1", new DateOnly(2026, 9, 11)));
    }

    [Fact]
    public async Task IdleAndResumeExcludeIdleTime()
    {
        var start = Utc(2026, 9, 11, 9, 0);
        var clock = new FakeClock(start, TimeZoneInfo.Utc);
        var store = new FakeTimeUsageStore();
        using var engine = new SessionTimeEngine(store, clock, "child-1");

        await engine.InitializeAsync(Snapshot(SessionActivityState.Active, start));
        await engine.ApplyObservationAsync(Snapshot(SessionActivityState.Idle, start.AddMinutes(5)));
        await engine.ApplyObservationAsync(Snapshot(SessionActivityState.Active, start.AddMinutes(25)));
        clock.UtcNow = start.AddMinutes(30);
        await engine.StopAsync();

        Assert.Equal(TimeSpan.FromMinutes(10), await store.GetUsageAsync("child-1", new DateOnly(2026, 9, 11)));
    }

    [Fact]
    public async Task ServiceRestartContinuesFromPersistedActiveCheckpoint()
    {
        var start = Utc(2026, 9, 11, 9, 0);
        var clock = new FakeClock(start, TimeZoneInfo.Utc);
        var store = new FakeTimeUsageStore();

        using (var firstEngine = new SessionTimeEngine(store, clock, "child-1"))
        {
            await firstEngine.InitializeAsync(Snapshot(SessionActivityState.Active, start));
            clock.UtcNow = start.AddMinutes(10);
            await firstEngine.StopAsync();
        }

        using (var restartedEngine = new SessionTimeEngine(store, clock, "child-1"))
        {
            clock.UtcNow = start.AddMinutes(15);
            await restartedEngine.InitializeAsync(Snapshot(SessionActivityState.Active, clock.UtcNow));
            clock.UtcNow = start.AddMinutes(20);
            await restartedEngine.StopAsync();
        }

        Assert.Equal(TimeSpan.FromMinutes(20), await store.GetUsageAsync("child-1", new DateOnly(2026, 9, 11)));
    }

    [Fact]
    public async Task RebootCapsRecoveredActiveIntervalAtNewBootTime()
    {
        var start = Utc(2026, 9, 11, 9, 0);
        var clock = new FakeClock(start, TimeZoneInfo.Utc);
        var store = new FakeTimeUsageStore();

        using (var firstEngine = new SessionTimeEngine(store, clock, "child-1"))
        {
            await firstEngine.InitializeAsync(Snapshot(SessionActivityState.Active, start, start.AddHours(-1)));
            clock.UtcNow = start.AddMinutes(10);
            await firstEngine.StopAsync();
        }

        using (var restartedEngine = new SessionTimeEngine(store, clock, "child-1"))
        {
            var rebootStartedAt = start.AddMinutes(50);
            clock.UtcNow = start.AddHours(1);
            await restartedEngine.InitializeAsync(Snapshot(SessionActivityState.Active, clock.UtcNow, rebootStartedAt));
            clock.UtcNow = start.AddHours(1).AddMinutes(10);
            await restartedEngine.StopAsync();
        }

        Assert.Equal(TimeSpan.FromMinutes(30), await store.GetUsageAsync("child-1", new DateOnly(2026, 9, 11)));
    }

    [Fact]
    public async Task MidnightRolloverSplitsUsageAcrossLocalDates()
    {
        var start = Utc(2026, 9, 11, 23, 50);
        var clock = new FakeClock(start, TimeZoneInfo.Utc);
        var store = new FakeTimeUsageStore();
        using var engine = new SessionTimeEngine(store, clock, "child-1");

        await engine.InitializeAsync(Snapshot(SessionActivityState.Active, start));
        clock.UtcNow = start.AddMinutes(20);
        await engine.StopAsync();

        Assert.Equal(TimeSpan.FromMinutes(10), await store.GetUsageAsync("child-1", new DateOnly(2026, 9, 11)));
        Assert.Equal(TimeSpan.FromMinutes(10), await store.GetUsageAsync("child-1", new DateOnly(2026, 9, 12)));
    }

    [Fact]
    public async Task DstTransitionsUseElapsedTimeRatherThanWallClockDifference()
    {
        var easternTime = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var springStart = Utc(2026, 3, 8, 6, 30);
        var springClock = new FakeClock(springStart, easternTime);
        var springStore = new FakeTimeUsageStore();
        using (var springEngine = new SessionTimeEngine(springStore, springClock, "child-1"))
        {
            await springEngine.InitializeAsync(Snapshot(SessionActivityState.Active, springStart));
            springClock.UtcNow = Utc(2026, 3, 8, 8, 30);
            await springEngine.StopAsync();
        }

        var fallStart = Utc(2026, 11, 1, 5, 30);
        var fallClock = new FakeClock(fallStart, easternTime);
        var fallStore = new FakeTimeUsageStore();
        using (var fallEngine = new SessionTimeEngine(fallStore, fallClock, "child-1"))
        {
            await fallEngine.InitializeAsync(Snapshot(SessionActivityState.Active, fallStart));
            fallClock.UtcNow = Utc(2026, 11, 1, 7, 30);
            await fallEngine.StopAsync();
        }

        Assert.Equal(TimeSpan.FromHours(2), await springStore.GetUsageAsync("child-1", new DateOnly(2026, 3, 8)));
        Assert.Equal(TimeSpan.FromHours(2), await fallStore.GetUsageAsync("child-1", new DateOnly(2026, 11, 1)));
    }

    [Fact]
    public async Task DuplicateAndOutOfOrderEventsDoNotDoubleCount()
    {
        var start = Utc(2026, 9, 11, 9, 0);
        var clock = new FakeClock(start, TimeZoneInfo.Utc);
        var store = new FakeTimeUsageStore();
        using var engine = new SessionTimeEngine(store, clock, "child-1");

        await engine.InitializeAsync(Snapshot(SessionActivityState.Active, start));
        Assert.Equal(TimeEngineApplyResult.Applied, await engine.ApplyObservationAsync(Snapshot(SessionActivityState.Locked, start.AddMinutes(10))));
        Assert.Equal(TimeEngineApplyResult.IgnoredDuplicate, await engine.ApplyObservationAsync(Snapshot(SessionActivityState.Locked, start.AddMinutes(10))));
        Assert.Equal(TimeEngineApplyResult.IgnoredOutOfOrder, await engine.ApplyObservationAsync(Snapshot(SessionActivityState.Active, start.AddMinutes(5))));
        await engine.ApplyObservationAsync(Snapshot(SessionActivityState.Active, start.AddMinutes(20)));
        clock.UtcNow = start.AddMinutes(30);
        await engine.StopAsync();

        Assert.Equal(TimeSpan.FromMinutes(20), await store.GetUsageAsync("child-1", new DateOnly(2026, 9, 11)));
    }

    [Fact]
    public async Task SystemCheckRecordsOneLocalSession()
    {
        var start = Utc(2026, 9, 11, 14, 0);
        var clock = new FakeClock(start.AddMinutes(30), TimeZoneInfo.Utc);
        var store = new FakeTimeUsageStore();
        using var engine = new SessionTimeEngine(store, clock, "child-1");
        var eventSource = new FakeSessionEventSource(
            Snapshot(SessionActivityState.Active, start),
            [Snapshot(SessionActivityState.Locked, start.AddMinutes(30))]);
        var trackingHost = new SessionTimeTrackingHost(engine, eventSource);

        await trackingHost.RunAsync();

        Assert.Equal(TimeSpan.FromMinutes(30), await store.GetUsageAsync("child-1", new DateOnly(2026, 9, 11)));
    }

    private static SessionSnapshot Snapshot(
        SessionActivityState state,
        DateTimeOffset observedAtUtc,
        DateTimeOffset? bootStartedAtUtc = null) => new(
        sessionId: 1,
        state,
        observedAtUtc,
        bootStartedAtUtc ?? observedAtUtc.AddHours(-1));

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute) => new(
        year,
        month,
        day,
        hour,
        minute,
        0,
        TimeSpan.Zero);
}