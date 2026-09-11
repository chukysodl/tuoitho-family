using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;

using Microsoft.Extensions.Options;
using Microsoft.Win32;

using TuoiTho.Core.Time;

namespace TuoiTho.Service;

public sealed class WindowsSessionEventSource : ISessionEventSource
{
    private const uint NoActiveConsoleSession = uint.MaxValue;
    private readonly IClock clock;
    private readonly Channel<SessionSnapshot> events = Channel.CreateUnbounded<SessionSnapshot>();
    private readonly IWindowsIdleTimeProvider idleTimeProvider;
    private readonly TimeSpan idlePollInterval;
    private readonly TimeSpan idleThreshold;
    private int trackedSessionId;
    private SessionActivityState currentState;
    private bool initialized;

    public WindowsSessionEventSource(
        IClock clock,
        IOptions<WindowsTimeTrackingOptions> options,
        IWindowsIdleTimeProvider idleTimeProvider)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        ArgumentNullException.ThrowIfNull(options);
        this.idleTimeProvider = idleTimeProvider ?? throw new ArgumentNullException(nameof(idleTimeProvider));

        var value = options.Value;
        value.Validate();
        idleThreshold = TimeSpan.FromMinutes(value.IdleThresholdMinutes);
        idlePollInterval = TimeSpan.FromSeconds(value.IdlePollIntervalSeconds);
        trackedSessionId = value.SessionId ?? -1;
    }

    public Task<SessionSnapshot> GetInitialSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (initialized)
        {
            throw new InvalidOperationException("The Windows session event source has already been initialized.");
        }

        if (trackedSessionId < 0)
        {
            var activeConsoleSession = WTSGetActiveConsoleSessionId();
            trackedSessionId = activeConsoleSession == NoActiveConsoleSession ? 0 : checked((int)activeConsoleSession);
        }

        currentState = trackedSessionId == 0
            ? SessionActivityState.LoggedOut
            : idleTimeProvider.GetIdleDuration() >= idleThreshold
                ? SessionActivityState.Idle
                : SessionActivityState.Active;
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
        SystemEvents.SessionSwitch += OnSessionSwitch;
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
            SystemEvents.SessionSwitch -= OnSessionSwitch;
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

    private async Task MonitorIdleAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(idlePollInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            if (currentState is not (SessionActivityState.Active or SessionActivityState.Idle))
            {
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

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs eventArgs)
    {
        if (trackedSessionId == 0 && currentState == SessionActivityState.LoggedOut
            && eventArgs.Reason == SessionSwitchReason.SessionLogon)
        {
            var activeConsoleSession = WTSGetActiveConsoleSessionId();
            if (activeConsoleSession != NoActiveConsoleSession)
            {
                trackedSessionId = checked((int)activeConsoleSession);
            }
        }

        var nextState = eventArgs.Reason switch
        {
            SessionSwitchReason.SessionLock => SessionActivityState.Locked,
            SessionSwitchReason.SessionLogoff => SessionActivityState.LoggedOut,
            SessionSwitchReason.SessionUnlock => SessionActivityState.Active,
            SessionSwitchReason.SessionLogon => SessionActivityState.Active,
            SessionSwitchReason.ConsoleConnect => SessionActivityState.Active,
            SessionSwitchReason.RemoteConnect => SessionActivityState.Active,
            SessionSwitchReason.ConsoleDisconnect => SessionActivityState.Locked,
            SessionSwitchReason.RemoteDisconnect => SessionActivityState.Locked,
            _ => currentState
        };

        Publish(nextState, clock.UtcNow);
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

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();
}