namespace TuoiTho.Core.Time;

public sealed class SessionTimeTrackingHost
{
    private readonly SessionTimeEngine engine;
    private readonly ISessionEventSource eventSource;

    public SessionTimeTrackingHost(SessionTimeEngine engine, ISessionEventSource eventSource)
    {
        this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
        this.eventSource = eventSource ?? throw new ArgumentNullException(nameof(eventSource));
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var initialSnapshot = await eventSource.GetInitialSnapshotAsync(cancellationToken);
        await engine.InitializeAsync(initialSnapshot, cancellationToken);

        try
        {
            await foreach (var snapshot in eventSource.ReadEventsAsync(cancellationToken))
            {
                await engine.ApplyObservationAsync(snapshot, cancellationToken);
            }
        }
        finally
        {
            await engine.StopAsync(CancellationToken.None);
        }
    }
}