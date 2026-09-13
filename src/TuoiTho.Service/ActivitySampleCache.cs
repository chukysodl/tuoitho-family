using TuoiTho.Core.Time;

namespace TuoiTho.Service;

public sealed class ActivitySampleCache(IClock clock)
{
    private readonly object gate = new();
    private SessionActivitySample? sample;
    private DateTimeOffset receivedAtUtc;

    public bool TryAccept(SessionActivitySample candidate, string profileId, int sessionId)
    {
        if (candidate.ProfileId != profileId || candidate.SessionId != sessionId ||
            candidate.IdleSeconds < 0 || double.IsNaN(candidate.IdleSeconds) || double.IsInfinity(candidate.IdleSeconds) ||
            candidate.WindowsIdleSeconds is < 0 || double.IsNaN(candidate.WindowsIdleSeconds ?? 0) || double.IsInfinity(candidate.WindowsIdleSeconds ?? 0))
        {
            return false;
        }

        lock (gate)
        {
            sample = candidate;
            receivedAtUtc = clock.UtcNow;
            return true;
        }
    }

    public SessionActivitySample? GetFresh(string profileId, int sessionId, TimeSpan maximumAge)
    {
        lock (gate)
        {
            if (sample is null || sample.ProfileId != profileId || sample.SessionId != sessionId || clock.UtcNow - receivedAtUtc > maximumAge)
            {
                return null;
            }

            return sample;
        }
    }

    public DateTimeOffset? LastReceivedAtUtc
    {
        get { lock (gate) return sample is null ? null : receivedAtUtc; }
    }
}