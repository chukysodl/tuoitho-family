using Microsoft.Extensions.Options;
using TuoiTho.Core.Time;

namespace TuoiTho.Service;

// WTS proves that the configured session is present/connected. Input activity is
// authoritative only when supplied by the authenticated SessionAgent in that session.
public sealed class AgentReportedSessionActivityProvider : IWindowsSessionActivityProvider
{
    private static readonly TimeSpan MaximumSampleAge = TimeSpan.FromSeconds(15);
    private readonly Func<int, WindowsSessionActivity> getWtsActivity;
    private readonly ActivitySampleCache cache;
    private readonly IClock clock;
    private readonly string profileId;
    private readonly TimeSpan idleThreshold;

    public AgentReportedSessionActivityProvider(WtsSessionActivityProvider wts, ActivitySampleCache cache, IClock clock, IOptions<WindowsTimeTrackingOptions> options)
        : this(wts.GetActivity, cache, clock, options.Value) { }

    public AgentReportedSessionActivityProvider(Func<int, WindowsSessionActivity> getWtsActivity, ActivitySampleCache cache, IClock clock, WindowsTimeTrackingOptions options)
    {
        this.getWtsActivity = getWtsActivity;
        this.cache = cache;
        this.clock = clock;
        profileId = options.ProfileId;
        idleThreshold = TimeSpan.FromMinutes(options.IdleThresholdMinutes);
    }

    public WindowsSessionActivity GetActivity(int sessionId)
    {
        var session = getWtsActivity(sessionId);
        if (!IsConnected(session))
        {
            return session.State == SessionActivityState.LoggedOut
                ? session
                : new(SessionActivityState.Unknown, null, null, null, session.Win32Error, "Managed Windows session is unavailable.", session.QuerySucceeded, session.Raw);
        }

        if (cache.GetFresh(profileId, sessionId, MaximumSampleAge) is not { } sample)
        {
            return new(SessionActivityState.Unknown, null, null, null, session.Win32Error, "No fresh authenticated SessionAgent activity sample.", session.QuerySucceeded, session.Raw);
        }

        var idle = TimeSpan.FromSeconds(sample.IdleSeconds);
        return new(idle >= idleThreshold ? SessionActivityState.Idle : SessionActivityState.Active, idle, sample.ObservedAtUtc - idle, clock.UtcNow, null, null, true, session.Raw);
    }

    private static bool IsConnected(WindowsSessionActivity activity) =>
        activity.Raw is { SessionState: 0 or 1 } ||
        activity.State is SessionActivityState.Active or SessionActivityState.Idle;
}