using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Microsoft.Win32;

using TuoiTho.Core.Time;
using TuoiTho.Service;
using TuoiTho.Storage;

using Xunit.Sdk;

namespace TuoiTho.Tests;

public sealed class WindowsSessionEventSourceTests
{
    [Fact]
    public async Task ForeignSessionNotificationsDoNotChangeTrackedChildState()
    {
        var clock = new FakeClock(Utc(2026, 9, 12, 9, 0), TimeZoneInfo.Utc);
        var states = new FakeWindowsSessionStateProvider { ActiveConsoleSessionId = 7 };
        states.States[7] = WindowsSessionState.Active;
        states.States[8] = WindowsSessionState.Active;
        var notifications = new FakeWindowsSessionNotificationSource();
        using var source = CreateSource(clock, 7, states, notifications);

        Assert.Equal(SessionActivityState.Active, (await source.GetInitialSnapshotAsync()).State);
        await using var enumerator = source.ReadEventsAsync().GetAsyncEnumerator();
        var next = enumerator.MoveNextAsync().AsTask();
        notifications.Emit(SessionSwitchReason.SessionLock, 8);
        notifications.Emit(SessionSwitchReason.SessionLogoff, 8);
        await Task.Delay(50);
        Assert.False(next.IsCompleted);

        notifications.Emit(SessionSwitchReason.SessionLock, 7);
        Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(SessionActivityState.Locked, enumerator.Current.State);
        Assert.Equal(7, enumerator.Current.SessionId);
    }

    [Fact]
    public async Task InitialLockedSessionIsNotClassifiedAsIdleOrActive()
    {
        var clock = new FakeClock(Utc(2026, 9, 12, 9, 0), TimeZoneInfo.Utc);
        var states = new FakeWindowsSessionStateProvider { ActiveConsoleSessionId = 7 };
        states.States[7] = WindowsSessionState.Locked;
        using var source = CreateSource(clock, 7, states, new FakeWindowsSessionNotificationSource(), TimeSpan.Zero);

        var snapshot = await source.GetInitialSnapshotAsync();

        Assert.Equal(7, snapshot.SessionId);
        Assert.Equal(SessionActivityState.Locked, snapshot.State);
    }

    [Fact]
    public async Task UnlockAfterLockedRestartNeverAccumulatesLockedTime()
    {
        var start = Utc(2026, 9, 12, 9, 0);
        var clock = new FakeClock(start, TimeZoneInfo.Utc);
        var states = new FakeWindowsSessionStateProvider { ActiveConsoleSessionId = 7 };
        states.States[7] = WindowsSessionState.Locked;
        var notifications = new FakeWindowsSessionNotificationSource();
        using var source = CreateSource(clock, 7, states, notifications);
        var store = new FakeTimeUsageStore();
        using var engine = new SessionTimeEngine(store, clock, "child-1");

        await engine.InitializeAsync(await source.GetInitialSnapshotAsync());
        await using var enumerator = source.ReadEventsAsync().GetAsyncEnumerator();
        var unlock = enumerator.MoveNextAsync().AsTask();
        clock.UtcNow = start.AddMinutes(10);
        states.States[7] = WindowsSessionState.Active;
        notifications.Emit(SessionSwitchReason.SessionUnlock, 7);
        Assert.True(await unlock.WaitAsync(TimeSpan.FromSeconds(1)));
        await engine.ApplyObservationAsync(enumerator.Current);

        var locked = enumerator.MoveNextAsync().AsTask();
        clock.UtcNow = start.AddMinutes(20);
        notifications.Emit(SessionSwitchReason.SessionLock, 7);
        Assert.True(await locked.WaitAsync(TimeSpan.FromSeconds(1)));
        await engine.ApplyObservationAsync(enumerator.Current);
        await engine.StopAsync();

        Assert.Equal(TimeSpan.FromMinutes(10), await store.GetUsageAsync("child-1", new DateOnly(2026, 9, 12)));
    }

    [Fact]
    public async Task AutomaticTrackingReplacesOnlyTheSessionThatLoggedOff()
    {
        var clock = new FakeClock(Utc(2026, 9, 12, 9, 0), TimeZoneInfo.Utc);
        var states = new FakeWindowsSessionStateProvider { ActiveConsoleSessionId = 7 };
        states.States[7] = WindowsSessionState.Active;
        states.States[8] = WindowsSessionState.Active;
        var notifications = new FakeWindowsSessionNotificationSource();
        using var source = CreateSource(clock, null, states, notifications);

        Assert.Equal(7, (await source.GetInitialSnapshotAsync()).SessionId);
        await using var enumerator = source.ReadEventsAsync().GetAsyncEnumerator();
        var logoff = enumerator.MoveNextAsync().AsTask();
        notifications.Emit(SessionSwitchReason.SessionLogoff, 7);
        Assert.True(await logoff.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(SessionActivityState.LoggedOut, enumerator.Current.State);
        Assert.Equal(7, enumerator.Current.SessionId);

        states.ActiveConsoleSessionId = 8;
        var logon = enumerator.MoveNextAsync().AsTask();
        notifications.Emit(SessionSwitchReason.SessionLogon, 8);
        Assert.True(await logon.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(SessionActivityState.Active, enumerator.Current.State);
        Assert.Equal(8, enumerator.Current.SessionId);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public async Task SystemCheckWindowsAdapterPersistsRealInitialSnapshot()
    {
        var stateProvider = new WindowsSessionStateProvider();
        if (stateProvider.GetActiveConsoleSessionId() is null)
        {
            Console.Error.WriteLine("WINDOWS INTEGRATION CHECK SKIPPED: no interactive Windows console session is available for the real adapter check.");
            return;
        }

        var databasePath = Path.Combine(Path.GetTempPath(), $"tuoitho-windows-adapter-{Guid.NewGuid():N}.db");
        try
        {
            var clock = new SystemClock();
            using var source = new WindowsSessionEventSource(
                clock,
                Options.Create(new WindowsTimeTrackingOptions { IdleThresholdMinutes = 5, IdlePollIntervalSeconds = 60 }),
                new WindowsIdleTimeProvider(),
                new WindowsBootTimeProvider(),
                stateProvider,
                new WindowsSessionNotificationPump());
            var snapshot = await source.GetInitialSnapshotAsync();
            Assert.True(snapshot.SessionId > 0, "An interactive session must produce a non-zero Windows session ID.");
            Assert.NotEqual(SessionActivityState.LoggedOut, snapshot.State);

            var database = new SqliteDatabase(databasePath);
            await database.InitializeAsync();
            var store = new SqliteTimeUsageStore(database);
            using var engine = new SessionTimeEngine(store, clock, "windows-adapter-system-check");
            await engine.InitializeAsync(snapshot);
            await engine.StopAsync();

            var checkpoint = await store.LoadCheckpointAsync("windows-adapter-system-check");
            Assert.NotNull(checkpoint);
            Assert.Equal(snapshot.SessionId, checkpoint.SessionId);
            Assert.Equal(snapshot.State, checkpoint.State);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    private static WindowsSessionEventSource CreateSource(
        FakeClock clock,
        int? sessionId,
        FakeWindowsSessionStateProvider states,
        FakeWindowsSessionNotificationSource notifications,
        TimeSpan? idle = null) => new(
        clock,
        Options.Create(new WindowsTimeTrackingOptions { SessionId = sessionId, IdleThresholdMinutes = 5, IdlePollIntervalSeconds = 60 }),
        new FakeWindowsIdleTimeProvider(idle ?? TimeSpan.Zero),
        new FixedWindowsBootTimeProvider(clock.UtcNow.AddHours(-1)),
        states,
        notifications);

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute) => new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    private sealed class FakeWindowsIdleTimeProvider(TimeSpan idle) : IWindowsIdleTimeProvider
    {
        public TimeSpan GetIdleDuration() => idle;
    }

    private sealed class FixedWindowsBootTimeProvider(DateTimeOffset bootStartedAtUtc) : IWindowsBootTimeProvider
    {
        public DateTimeOffset GetBootStartedAtUtc(DateTimeOffset utcNow) => bootStartedAtUtc;
    }
    private sealed class FakeWindowsSessionStateProvider : IWindowsSessionStateProvider
    {
        public int? ActiveConsoleSessionId { get; set; }

        public Dictionary<int, WindowsSessionState> States { get; } = [];

        public int? GetActiveConsoleSessionId() => ActiveConsoleSessionId;

        public WindowsSessionState GetSessionState(int sessionId) => States.TryGetValue(sessionId, out var state) ? state : WindowsSessionState.LoggedOut;
    }

    private sealed class FakeWindowsSessionNotificationSource : IWindowsSessionNotificationSource
    {
        public event EventHandler<WindowsSessionNotification>? SessionChanged;

        public void Dispose()
        {
        }

        public void Emit(SessionSwitchReason reason, int sessionId) => SessionChanged?.Invoke(this, new WindowsSessionNotification(reason, sessionId));

        public void Start()
        {
        }
    }
}