namespace TuoiTho.Core.Time;

public sealed record TimeTrackingCheckpoint(
    int SessionId,
    SessionActivityState State,
    DateTimeOffset LastObservedAtUtc,
    DateTimeOffset BootStartedAtUtc)
{
    public TimeTrackingCheckpoint NormalizeToUtc() => this with
    {
        LastObservedAtUtc = LastObservedAtUtc.ToUniversalTime(),
        BootStartedAtUtc = BootStartedAtUtc.ToUniversalTime()
    };
}