using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using TuoiTho.Core.Time;

namespace TuoiTho.Service;

public sealed class WindowsSessionEventSource : ISessionEventSource, ISessionEventSourceLifecycle, IDisposable
{
    private readonly IWindowsBootTimeProvider bootTimeProvider;
    private readonly IClock clock;
    private readonly IWindowsSessionActivityProvider activityProvider;
    private readonly IWindowsSessionNotificationSource notificationSource;
    private readonly Channel<SessionSnapshot> events = Channel.CreateUnbounded<SessionSnapshot>();
    private readonly TimeSpan pollInterval;
    private readonly bool sessionIsConfigured;
    private readonly ILogger<WindowsSessionEventSource> logger;
    private Func<int?> getActiveConsoleSessionId;

    private int trackedSessionId;
    private SessionActivityState currentState;
    private DateTimeOffset lastPublishedAtUtc;
    private bool initialized;
    private bool started;
    private bool stopped;
    private CancellationTokenSource? monitorCancellation;
    private Task? monitorTask;

    public WindowsSessionEventSource(
        IClock clock,
        IOptions<WindowsTimeTrackingOptions> options,
        IWindowsSessionActivityProvider activityProvider,
        IWindowsBootTimeProvider bootTimeProvider,
        IWindowsSessionNotificationSource notificationSource,
        ILogger<WindowsSessionEventSource> logger)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        ArgumentNullException.ThrowIfNull(options);
        this.activityProvider = activityProvider ?? throw new ArgumentNullException(nameof(activityProvider));
        this.bootTimeProvider = bootTimeProvider ?? throw new ArgumentNullException(nameof(bootTimeProvider));
        this.notificationSource = notificationSource ?? throw new ArgumentNullException(nameof(notificationSource));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var value = options.Value;
        value.Validate();
        pollInterval = TimeSpan.FromSeconds(value.IdlePollIntervalSeconds);
        sessionIsConfigured = value.SessionId.HasValue;
        trackedSessionId = value.SessionId ?? 0;
        getActiveConsoleSessionId = GetActiveConsoleSessionId;
    }

    // Retained only for existing deterministic tests while production is wired to WTS activity.
    public WindowsSessionEventSource(
        IClock clock,
        IOptions<WindowsTimeTrackingOptions> options,
        IWindowsIdleTimeProvider idleTimeProvider,
        IWindowsBootTimeProvider bootTimeProvider,
        IWindowsSessionStateProvider sessionStateProvider,
        IWindowsSessionNotificationSource notificationSource)
        : this(
            clock,
            options,
            new LegacyActivityProvider(idleTimeProvider, sessionStateProvider),
            bootTimeProvider,
            notificationSource,
            NullLogger<WindowsSessionEventSource>.Instance)
    {
        getActiveConsoleSessionId = sessionStateProvider.GetActiveConsoleSessionId;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (stopped)
        {
            throw new InvalidOperationException("The Windows session event source has been stopped.");
        }

        if (started)
        {
            return Task.CompletedTask;
        }

        notificationSource.SessionChanged += OnSessionSwitch;
        try
        {
            notificationSource.Start();
            monitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            monitorTask = MonitorAsync(monitorCancellation.Token);
            started = true;
            return Task.CompletedTask;
        }
        catch
        {
            notificationSource.SessionChanged -= OnSessionSwitch;
            monitorCancellation?.Dispose();
            monitorCancellation = null;
            notificationSource.Dispose();
            throw;
        }
    }

    public Task<SessionSnapshot> GetInitialSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (initialized)
        {
            throw new InvalidOperationException("The Windows session event source has already been initialized.");
        }

        if (!started)
        {
            StartAsync(cancellationToken).GetAwaiter().GetResult();
        }

        if (!sessionIsConfigured)
        {
            trackedSessionId = getActiveConsoleSessionId() ?? 0;
        }

        currentState = GetActivity().State;
        lastPublishedAtUtc = clock.UtcNow;
        initialized = true;
        return Task.FromResult(CreateSnapshot(currentState, lastPublishedAtUtc));
    }

    public async IAsyncEnumerable<SessionSnapshot> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!initialized)
        {
            throw new InvalidOperationException("GetInitialSnapshotAsync must be called before reading session events.");
        }

        await foreach (var snapshot in events.Reader.ReadAllAsync(cancellationToken))
        {
            yield return snapshot;
        }
    }

    public async Task StopAsync()
    {
        if (stopped)
        {
            return;
        }

        stopped = true;
        notificationSource.SessionChanged -= OnSessionSwitch;
        monitorCancellation?.Cancel();

        try
        {
            if (monitorTask is not null)
            {
                await monitorTask;
            }
        }
        catch (OperationCanceledException) when (monitorCancellation?.IsCancellationRequested == true)
        {
            // Normal monitor shutdown requested by this source.
        }
        finally
        {
            events.Writer.TryComplete();
            monitorCancellation?.Dispose();
            monitorCancellation = null;
            notificationSource.Dispose();
        }
    }

    public void Dispose() => StopAsync().GetAwaiter().GetResult();

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(pollInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var activity = GetActivity();
                Publish(activity.State, clock.UtcNow, allowSameState: activity.State == SessionActivityState.Active);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Requested lifecycle shutdown is expected.
        }
        catch (Exception exception)
        {
            events.Writer.TryComplete(exception);
            throw;
        }
    }

    private WindowsSessionActivity GetActivity()
    {
        var activity = activityProvider.GetActivity(trackedSessionId);
        if (activity.State == SessionActivityState.Unknown)
        {
            SessionActivityLog.Unknown(logger, trackedSessionId, activity.Diagnostic, activity.Win32Error);
        }

        return activity;
    }

    private void OnSessionSwitch(object? sender, WindowsSessionNotification notification)
    {
        if (trackedSessionId == 0)
        {
            if (!sessionIsConfigured
                && notification.Reason is SessionSwitchReason.SessionLogon or SessionSwitchReason.ConsoleConnect
                && getActiveConsoleSessionId() == notification.SessionId)
            {
                trackedSessionId = notification.SessionId;
                Publish(GetActivity().State, clock.UtcNow);
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
                Publish(GetActivity().State, occurredAtUtc);
                break;
        }
    }

    private void Publish(SessionActivityState nextState, DateTimeOffset occurredAtUtc, bool allowSameState = false)
    {
        if (nextState == currentState && !allowSameState)
        {
            return;
        }

        var monotonicOccurredAtUtc = occurredAtUtc < lastPublishedAtUtc ? lastPublishedAtUtc : occurredAtUtc;
        if (nextState == currentState && monotonicOccurredAtUtc == lastPublishedAtUtc)
        {
            return;
        }

        currentState = nextState;
        lastPublishedAtUtc = monotonicOccurredAtUtc;
        events.Writer.TryWrite(CreateSnapshot(nextState, monotonicOccurredAtUtc));
    }

    private SessionSnapshot CreateSnapshot(SessionActivityState state, DateTimeOffset occurredAtUtc) =>
        new(trackedSessionId, state, occurredAtUtc, bootTimeProvider.GetBootStartedAtUtc(clock.UtcNow));

    private static int? GetActiveConsoleSessionId()
    {
        var sessionId = WTSGetActiveConsoleSessionId();
        return sessionId == uint.MaxValue ? null : unchecked((int)sessionId);
    }

    private sealed class LegacyActivityProvider(
        IWindowsIdleTimeProvider idleTimeProvider,
        IWindowsSessionStateProvider sessionStateProvider) : IWindowsSessionActivityProvider
    {
        public WindowsSessionActivity GetActivity(int sessionId) => sessionStateProvider.GetSessionState(sessionId) switch
        {
            WindowsSessionState.LoggedOut => new(SessionActivityState.LoggedOut, null, null, null, null, null),
            WindowsSessionState.Locked => new(SessionActivityState.Locked, null, null, null, null, null),
            _ => new(SessionActivityState.Active, idleTimeProvider.GetIdleDuration(), null, null, null, null)
        };
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();
}

internal static partial class SessionActivityLog
{
    [LoggerMessage(
        EventId = 1200,
        Level = LogLevel.Warning,
        Message = "Managed session {SessionId} activity is UNKNOWN: {Diagnostic} (Win32 {Win32Error}).")]
    public static partial void Unknown(ILogger logger, int sessionId, string? diagnostic, int? win32Error);
}
