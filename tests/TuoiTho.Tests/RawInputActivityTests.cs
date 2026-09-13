using TuoiTho.Core.Time;
using TuoiTho.Service;
using TuoiTho.SessionAgent;

namespace TuoiTho.Tests;

public sealed class RawInputActivityTests
{
    [Fact]
    public void StartupThenNoRawInputLetsDeviceIdleIncrease()
    {
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 9, 15, 9, 0, 0, TimeSpan.Zero));
        var tracker = new ManualTracker(time.GetUtcNow());
        using var sampler = new InteractiveActivitySampler(tracker, new MutableWindowsIdleDiagnostics(0.2), time);
        sampler.Start();
        time.Advance(TimeSpan.FromSeconds(60));

        var activity = sampler.GetActivity();
        Assert.True(activity.RawInputAvailable);
        Assert.Equal(60d, activity.DeviceIdleSeconds.GetValueOrDefault(), 3);
        Assert.Equal(0.2d, activity.WindowsIdleSeconds.GetValueOrDefault(), 3);
    }

    [Fact]
    public void RawDeviceActivityResetsIdleButWindowsDiagnosticCannot()
    {
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 9, 15, 9, 0, 0, TimeSpan.Zero));
        var tracker = new ManualTracker(time.GetUtcNow());
        var windows = new MutableWindowsIdleDiagnostics(0.1);
        using var sampler = new InteractiveActivitySampler(tracker, windows, time);
        sampler.Start();
        time.Advance(TimeSpan.FromSeconds(61));
        windows.IdleSeconds = 0;
        Assert.Equal(61d, sampler.GetActivity().DeviceIdleSeconds.GetValueOrDefault(), 3);

        tracker.Record(time.GetUtcNow());
        Assert.Equal(0d, sampler.GetActivity().DeviceIdleSeconds.GetValueOrDefault(), 3);
    }

    [Fact]
    public void RawInputUnavailableIsUnknownRatherThanFalseActive()
    {
        var now = new DateTimeOffset(2026, 9, 15, 9, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(now, TimeZoneInfo.Utc);
        var cache = new ActivitySampleCache(clock);
        cache.TryAccept(new SessionActivitySample("child", 7, now, 0, RawInputAvailable: false, WindowsIdleSeconds: 0), "child", 7);
        var connected = new WindowsSessionActivity(SessionActivityState.Unknown, null, null, null, null, null, true, new(232, 1, 7, 0, 1, 0, 0, null, null, null));
        var provider = new AgentReportedSessionActivityProvider(_ => connected, cache, clock, new WindowsTimeTrackingOptions { ProfileId = "child", IdleThresholdMinutes = 1, IdlePollIntervalSeconds = 5 });

        Assert.Equal(SessionActivityState.Unknown, provider.GetActivity(7).State);
    }

    [Fact]
    public async Task IdleAndUnknownSamplesDoNotAccumulateAndRawInputResumesAccounting()
    {
        var start = new DateTimeOffset(2026, 9, 15, 9, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(start, TimeZoneInfo.Utc);
        var cache = new ActivitySampleCache(clock);
        var connected = new WindowsSessionActivity(SessionActivityState.Unknown, null, null, null, null, null, true, new(232, 1, 7, 0, 1, 0, 0, null, null, null));
        var provider = new AgentReportedSessionActivityProvider(_ => connected, cache, clock, new WindowsTimeTrackingOptions { ProfileId = "child", IdleThresholdMinutes = 1, IdlePollIntervalSeconds = 5 });
        var usage = new FakeTimeUsageStore();
        using var engine = new SessionTimeEngine(usage, clock, "child");

        cache.TryAccept(new("child", 7, clock.UtcNow, 0), "child", 7);
        await engine.InitializeAsync(new SessionSnapshot(7, provider.GetActivity(7).State, clock.UtcNow, start.AddHours(-1)));
        clock.UtcNow = start.AddSeconds(5);
        cache.TryAccept(new("child", 7, clock.UtcNow, 61), "child", 7);
        await engine.ApplyObservationAsync(new SessionSnapshot(7, provider.GetActivity(7).State, clock.UtcNow, start.AddHours(-1)));
        var usedAtIdle = await usage.GetUsageAsync("child", DateOnly.FromDateTime(start.UtcDateTime));

        clock.UtcNow = start.AddSeconds(65);
        cache.TryAccept(new("child", 7, clock.UtcNow, 121), "child", 7);
        await engine.ApplyObservationAsync(new SessionSnapshot(7, provider.GetActivity(7).State, clock.UtcNow, start.AddHours(-1)));
        Assert.Equal(usedAtIdle, await usage.GetUsageAsync("child", DateOnly.FromDateTime(start.UtcDateTime)));

        clock.UtcNow = start.AddSeconds(70);
        cache.TryAccept(new("child", 7, clock.UtcNow, 0, RawInputAvailable: false), "child", 7);
        await engine.ApplyObservationAsync(new SessionSnapshot(7, provider.GetActivity(7).State, clock.UtcNow, start.AddHours(-1)));
        clock.UtcNow = start.AddSeconds(80);
        cache.TryAccept(new("child", 7, clock.UtcNow, 0, RawInputAvailable: false), "child", 7);
        await engine.ApplyObservationAsync(new SessionSnapshot(7, provider.GetActivity(7).State, clock.UtcNow, start.AddHours(-1)));
        Assert.Equal(usedAtIdle, await usage.GetUsageAsync("child", DateOnly.FromDateTime(start.UtcDateTime)));

        cache.TryAccept(new("child", 7, clock.UtcNow, 0), "child", 7);
        await engine.ApplyObservationAsync(new SessionSnapshot(7, provider.GetActivity(7).State, clock.UtcNow, start.AddHours(-1)));
        clock.UtcNow = start.AddSeconds(85);
        cache.TryAccept(new("child", 7, clock.UtcNow, 0), "child", 7);
        await engine.ApplyObservationAsync(new SessionSnapshot(7, provider.GetActivity(7).State, clock.UtcNow, start.AddHours(-1)));
        Assert.Equal(usedAtIdle.Add(TimeSpan.FromSeconds(5)), await usage.GetUsageAsync("child", DateOnly.FromDateTime(start.UtcDateTime)));
    }

    [Fact]
    public void StoredActivityModelContainsOnlyAggregateMetadata()
    {
        var names = typeof(SessionActivitySample).GetProperties().Select(property => property.Name).ToArray();
        Assert.Equal(["ProfileId", "SessionId", "ObservedAtUtc", "IdleSeconds", "RawInputAvailable", "WindowsIdleSeconds"], names);
        Assert.DoesNotContain(names, name => name.Contains("Key", StringComparison.OrdinalIgnoreCase) || name.Contains("Coordinate", StringComparison.OrdinalIgnoreCase) || name.Contains("Button", StringComparison.OrdinalIgnoreCase) || name.Contains("DeviceName", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset utcNow = now;
        public override DateTimeOffset GetUtcNow() => utcNow;
        public void Advance(TimeSpan duration) => utcNow += duration;
    }

    private sealed class ManualTracker(DateTimeOffset initial) : IRawInputActivityTracker
    {
        private DateTimeOffset last = initial;
        public void Start() { }
        public RawInputActivityStatus GetStatus() => new(true, last, null);
        public void Record(DateTimeOffset value) => last = value;
        public void Dispose() { }
    }

    private sealed class MutableWindowsIdleDiagnostics(double value) : IWindowsIdleTimeDiagnostics
    {
        public double? IdleSeconds { get; set; } = value;
        public double? GetIdleSeconds() => IdleSeconds;
    }
}