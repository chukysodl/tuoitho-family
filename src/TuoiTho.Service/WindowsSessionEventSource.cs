using System.Runtime.CompilerServices;
using System.Threading.Channels;

using Microsoft.Extensions.Options;
using Microsoft.Win32;

using TuoiTho.Core.Time;

namespace TuoiTho.Service;

public sealed class WindowsSessionEventSource : ISessionEventSource, IDisposable
{
    private readonly IClock clock;
    private readonly Channel<SessionSnapshot> events = Channel.CreateUnbounded<SessionSnapshot>();
    private readonly IWindowsIdleTimeProvider idleTimeProvider;
    private readonly TimeSpan idlePollInterval;
    private readonly TimeSpan idleThreshold;
    private readonly IWindowsSessionNotificationSource notificationSource;
    private readonly bool sessionIsConfigured;
    private readonly IWindowsSessionStateProvider sessionStateProvider;
    private int trackedSessionId;
    private SessionActivityState currentState;
    private bool initialized;
    private bool notificationsStarted;

    public WindowsSessionEventSource(
        IClock clock,
        IOptions<WindowsTimeTrackingOptions> options,
        IWindowsIdleTimeProvider idleTimeProvider,
        IWindowsSessionStateProvider sessionStateProvider,
        IWindowsSessionNotificationSource notificationSource)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        ArgumentNullException.ThrowIfNull(options);
        this.idleTimeProvider = idleTimeProvider ?? throw new ArgumentNullException(nameof(idleTimeProvider));
        this.sessionStateProvider = sessionStateProvider ?? throw new ArgumentNullException(nameof(sessionStateProvider));
        this.notificationSource = notificationSource ?? throw new ArgumentNullException(nameof(notificationSource));

        var value = options.Value;
        value.Validate();
        idleThreshold = TimeSpan.FromMinutes(value.IdleThresholdMinutes);
        idlePollInterval = TimeSpan.FromSeconds(value.IdlePollIntervalSeconds);
        sessionIsConfigured = value.SessionId.HasValue;
        trackedSessionId = value.SessionId ?? 0;
    }

    public Task<SessionSnapshot> GetInitialSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (initialized)
        {
            throw new InvalidOperationException("The Windows session event source has already been initialized.");
        }

        if (!sessionIsConfigured)
        {
            trackedSessionId = sessionStateProvider.GetActiveConsoleSessionId() ?? 0;
        }

        currentState = GetCurrentState();
        initialized = true;
        return Task.FromResult(CreateSnapshot(currentState, clock.UtcNow));
    }

    public async IAsyncEnumerable<SessionSnapshot> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!initialized)
        {
            throw new InvalidOperationException("GetInitialSnapshotAsync must be called before reading session events.");
        }

        using var monitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        StartNotifications();
        var idleMonitor = MonitorIdleAsync(monitorCancellation.Token);
        try
        {
            await foreach (var snapshot in events.Reader.ReadAllAsync(cancellationToken))
            {
                yield return snapshot;
            }
        }
        finally
        {
            monitorCancellation.Cancel();
            try
            {
                await idleMonitor;
            }
            catch (OperationCanceledException) when (monitorCancellation.IsCancellationRequested)
            {
                // Expected during service shutdown.
            }
        }
    }

    public void Dispose()
    {
        notificationSource.SessionChanged -= OnSessionSwitch;
        notificationSource.Dispose();
    }

    private void StartNotifications()
    {
        if (notificationsStarted)
        {
            return;
        }

        notificationSource.SessionChanged += OnSessionSwitch;
        try
        {
            notificationSource.Start();
            notificationsStarted = true;
        }
        catch
        {
            notificationSource.SessionChanged -= OnSessionSwitch;
            throw;
        }
    }

    private async Task MonitorIdleAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(idlePollInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            if (currentState is not (SessionActivityState.Active or SessionActivityState.Idle))
            {
                continue;
            }

            if (GetCurrentState() == SessionActivityState.Locked)
            {
                Publish(SessionActivityState.Locked, clock.UtcNow);
                continue;
            }

            var idleDuration = idleTimeProvider.GetIdleDuration();
            if (currentState == SessionActivityState.Active && idleDuration >= idleThreshold)
            {
                var idleStartedAt = clock.UtcNow - (idleDuration - idleThreshold);
                Publish(SessionActivityState.Idle, idleStartedAt);
            }
            else if (currentState == SessionActivityState.Idle && idleDuration < idleThreshold)
            {
                Publish(SessionActivityState.Active, clock.UtcNow);
            }
        }
    }

    private void OnSessionSwitch(object? sender, WindowsSessionNotification notification)
    {
        if (trackedSessionId == 0)
        {
            if (!sessionIsConfigured
                && notification.Reason is SessionSwitchReason.SessionLogon or SessionSwitchReason.ConsoleConnect
                && sessionStateProvider.GetActiveConsoleSessionId() == notification.SessionId)
            {
                trackedSessionId = notification.SessionId;
                Publish(GetCurrentState(), clock.UtcNow);
            }

            return;
        }

        if (notification.SessionId != trackedSessionId)
        {
            return;
        }

        var occurredAtUtc = clock.UtcNow;
        switch (notification.Reason)
        {
            case SessionSwitchReason.SessionLogoff:
                Publish(SessionActivityState.LoggedOut, occurredAtUtc);
                if (!sessionIsConfigured)
                {
                    trackedSessionId = 0;
                }

                break;
            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.ConsoleDisconnect:
            case SessionSwitchReason.RemoteDisconnect:
                Publish(SessionActivityState.Locked, occurredAtUtc);
                break;
            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.SessionLogon:
            case SessionSwitchReason.ConsoleConnect:
            case SessionSwitchReason.RemoteConnect:
                Publish(GetCurrentState(), occurredAtUtc);
                break;
        }
    }

    private SessionActivityState GetCurrentState()
    {
        if (trackedSessionId == 0)
        {
            return SessionActivityState.LoggedOut;
        }

        return sessionStateProvider.GetSessionState(trackedSessionId) switch
        {
            WindowsSessionState.LoggedOut => SessionActivityState.LoggedOut,
            WindowsSessionState.Locked => SessionActivityState.Locked,
            _ => idleTimeProvider.GetIdleDuration() >= idleThreshold
                ? SessionActivityState.Idle
                : SessionActivityState.Active
        };
    }

    private void Publish(SessionActivityState nextState, DateTimeOffset occurredAtUtc)
    {
        if (nextState == currentState)
        {
            return;
        }

        currentState = nextState;
        events.Writer.TryWrite(CreateSnapshot(nextState, occurredAtUtc));
    }

    private SessionSnapshot CreateSnapshot(SessionActivityState state, DateTimeOffset occurredAtUtc) => new(
        trackedSessionId,
        state,
        occurredAtUtc,
        GetBootStartedAtUtc());

    private DateTimeOffset GetBootStartedAtUtc() => clock.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
}