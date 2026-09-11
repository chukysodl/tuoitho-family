namespace TuoiTho.Core.Time;

public interface ISessionEventSource
{
    Task<SessionSnapshot> GetInitialSnapshotAsync(CancellationToken cancellationToken = default);

    IAsyncEnumerable<SessionSnapshot> ReadEventsAsync(CancellationToken cancellationToken = default);
}