using Microsoft.Extensions.Options;
using TuoiTho.Core.Time;

namespace TuoiTho.Service;

public sealed class AgentReportedSessionActivityProvider : IWindowsSessionActivityProvider
{
    private static readonly TimeSpan MaximumSampleAge = TimeSpan.FromSeconds(15);
    private readonly Func<int, WindowsSessionActivity> getWtsActivity;
    private readonly ActivitySampleCache cache;
    private readonly IClock clock;
    private readonly string profileId;
    private readonly TimeSpan idleThreshold;

    public AgentReportedSessionActivityProvider(
        WtsSessionActivityProvider wts,
        ActivitySampleCache cache,
        IClock clock,
        IOptions<WindowsTimeTrackingOptions> options)
        : this(wts.GetActivity, cache, clock, options.Value)
    {
    }

    public AgentReportedSessionActivityProvider(
        Func<int, WindowsSessionActivity> getWtsActivity,
        ActivitySampleCache cache,
        IClock clock,
        WindowsTimeTrackingOptions options)
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
        if (session.State is SessionActivityState.Locked or SessionActivityState.LoggedOut)
        {
            return session;
        }

        if (!IsConnectedAndUnlocked(session) || cache.GetFresh(profileId, sessionId, MaximumSampleAge) is not { } sample)
        {
            return new(SessionActivityState.Unknown, null, null, null, session.Win32Error, "No fresh SessionAgent activity sample.", session.QuerySucceeded, session.Raw);
        }

        var idle = TimeSpan.FromSeconds(sample.IdleSeconds);
        return new(
            idle >= idleThreshold ? SessionActivityState.Idle : SessionActivityState.Active,
            idle,
            sample.ObservedAtUtc - idle,
            clock.UtcNow,
            null,
            null,
            true,
            session.Raw);
    }

    private static bool IsConnectedAndUnlocked(WindowsSessionActivity activity) =>
        activity.State == SessionActivityState.Active ||
        activity.Raw is { SessionState: 0 or 1, SessionFlags: not 0 };
}