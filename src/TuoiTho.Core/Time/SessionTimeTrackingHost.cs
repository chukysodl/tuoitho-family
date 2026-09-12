namespace TuoiTho.Core.Time;

public sealed class SessionTimeTrackingHost
{
    private readonly SessionTimeEngine engine;
    private readonly ISessionEventSource eventSource;
    private readonly Action? observationApplied;

    public SessionTimeTrackingHost(SessionTimeEngine engine, ISessionEventSource eventSource, Action? observationApplied = null)
    {
        this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
        this.eventSource = eventSource ?? throw new ArgumentNullException(nameof(eventSource));
        this.observationApplied = observationApplied;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var initialSnapshot = await eventSource.GetInitialSnapshotAsync(cancellationToken);
        await engine.InitializeAsync(initialSnapshot, cancellationToken);
        try
        {
            await foreach (var snapshot in eventSource.ReadEventsAsync(cancellationToken))
            {
                if (await engine.ApplyObservationAsync(snapshot, cancellationToken) == TimeEngineApplyResult.Applied)
                {
                    observationApplied?.Invoke();
                }
            }
        }
        finally
        {
            await engine.StopAsync(CancellationToken.None);
        }
    }
}