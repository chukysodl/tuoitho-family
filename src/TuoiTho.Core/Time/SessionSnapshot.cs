namespace TuoiTho.Core.Time;

public sealed record SessionSnapshot
{
    public SessionSnapshot(
        int sessionId,
        SessionActivityState state,
        DateTimeOffset occurredAtUtc,
        DateTimeOffset bootStartedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sessionId);
        SessionId = sessionId;
        State = state;
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
        BootStartedAtUtc = bootStartedAtUtc.ToUniversalTime();
    }

    public int SessionId { get; }

    public SessionActivityState State { get; }

    public DateTimeOffset OccurredAtUtc { get; }

    public DateTimeOffset BootStartedAtUtc { get; }
}