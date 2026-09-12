using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using TuoiTho.Core.Time;
using TuoiTho.Service;

namespace TuoiTho.Tests;

public sealed class WindowsSessionEventSourceLifecycleTests
{
    [Fact]
    public async Task StopAsyncEndsPendingReaderAndDetachesMonitor()
    {
        var notifications = new RecordingNotificationSource();
        using var source = CreateSource(new FixedActivityProvider(SessionActivityState.Active), notifications);
        await source.StartAsync();
        await source.GetInitialSnapshotAsync();
        await using var reader = source.ReadEventsAsync().GetAsyncEnumerator();
        var pendingRead = reader.MoveNextAsync().AsTask();

        await source.StopAsync();

        Assert.False(await pendingRead.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(1, notifications.StartCount);
        Assert.True(notifications.Disposed);
        Assert.Empty(notifications.Subscribers);
    }

    [Fact]
    public async Task EventThenStopAsyncCompletesCleanlyAndPreventsFurtherEvents()
    {
        var notifications = new RecordingNotificationSource();
        using var source = CreateSource(new FixedActivityProvider(SessionActivityState.Active), notifications);
        await source.StartAsync();
        await source.GetInitialSnapshotAsync();
        await using var reader = source.ReadEventsAsync().GetAsyncEnumerator();
        var read = reader.MoveNextAsync().AsTask();

        notifications.Emit(SessionSwitchReason.SessionLock, 7);
        Assert.True(await read.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(SessionActivityState.Locked, reader.Current.State);

        await source.StopAsync();
        notifications.Emit(SessionSwitchReason.SessionUnlock, 7);
        Assert.False(await reader.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task StartAndStopAreIdempotent()
    {
        var notifications = new RecordingNotificationSource();
        using var source = CreateSource(new FixedActivityProvider(SessionActivityState.Active), notifications);

        await source.StartAsync();
        await source.StartAsync();
        await source.GetInitialSnapshotAsync();
        await source.StopAsync();
        await source.StopAsync();

        Assert.Equal(1, notifications.StartCount);
        Assert.True(notifications.Disposed);
    }

    [Fact]
    public async Task UnexpectedMonitorFailureIsSurfacedByStopAsync()
    {
        var notifications = new RecordingNotificationSource();
        using var source = CreateSource(new ThrowOnSecondReadActivityProvider(), notifications);
        await source.StartAsync();
        await source.GetInitialSnapshotAsync();
        await using var reader = source.ReadEventsAsync().GetAsyncEnumerator();
        var pendingRead = reader.MoveNextAsync().AsTask();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await pendingRead.WaitAsync(TimeSpan.FromSeconds(3)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.StopAsync());
    }

    [Fact]
    public void WtsProviderTreatsAnInvalidSessionAsLoggedOutWithoutQueryingProcessInput()
    {
        var provider = new WtsSessionActivityProvider(Options.Create(new WindowsTimeTrackingOptions
        {
            IdleThresholdMinutes = 5,
            IdlePollIntervalSeconds = 1
        }));

        var result = provider.GetActivity(0);

        Assert.Equal(SessionActivityState.LoggedOut, result.State);
        Assert.Null(result.IdleDuration);
        Assert.Null(result.Win32Error);
    }

    private static WindowsSessionEventSource CreateSource(
        IWindowsSessionActivityProvider activityProvider,
        RecordingNotificationSource notifications) => new(
        new FakeClock(new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc),
        Options.Create(new WindowsTimeTrackingOptions
        {
            SessionId = 7,
            IdleThresholdMinutes = 5,
            IdlePollIntervalSeconds = 1
        }),
        activityProvider,
        new FixedBootTimeProvider(),
        notifications,
        NullLogger<WindowsSessionEventSource>.Instance);

    private sealed class FixedActivityProvider(SessionActivityState state) : IWindowsSessionActivityProvider
    {
        public WindowsSessionActivity GetActivity(int sessionId) => new(state, TimeSpan.Zero, null, null, null, null);
    }

    private sealed class ThrowOnSecondReadActivityProvider : IWindowsSessionActivityProvider
    {
        private int callCount;

        public WindowsSessionActivity GetActivity(int sessionId)
        {
            if (Interlocked.Increment(ref callCount) > 1)
            {
                throw new InvalidOperationException("Simulated WTS monitor failure.");
            }

            return new WindowsSessionActivity(SessionActivityState.Active, TimeSpan.Zero, null, null, null, null);
        }
    }

    private sealed class FixedBootTimeProvider : IWindowsBootTimeProvider
    {
        public DateTimeOffset GetBootStartedAtUtc(DateTimeOffset utcNow) => utcNow.AddHours(-1);
    }

    private sealed class RecordingNotificationSource : IWindowsSessionNotificationSource
    {
        private EventHandler<WindowsSessionNotification>? sessionChanged;

        public event EventHandler<WindowsSessionNotification>? SessionChanged
        {
            add => sessionChanged += value;
            remove => sessionChanged -= value;
        }

        public int StartCount { get; private set; }

        public bool Disposed { get; private set; }

        public Delegate[] Subscribers => sessionChanged?.GetInvocationList() ?? [];

        public void Start() => StartCount++;

        public void Emit(SessionSwitchReason reason, int sessionId) =>
            sessionChanged?.Invoke(this, new WindowsSessionNotification(reason, sessionId));

        public void Dispose() => Disposed = true;
    }
}
