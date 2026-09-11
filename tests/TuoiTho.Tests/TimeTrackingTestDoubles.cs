using System.Runtime.CompilerServices;

using TuoiTho.Core.Time;

namespace TuoiTho.Tests;

internal sealed class FakeClock(DateTimeOffset utcNow, TimeZoneInfo localTimeZone) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow.ToUniversalTime();

    public TimeZoneInfo LocalTimeZone { get; } = localTimeZone;
}

internal sealed class FakeTimeUsageStore : ITimeUsageStore
{
    private readonly Dictionary<DateOnly, TimeSpan> usageByDate = [];

    public TimeTrackingCheckpoint? Checkpoint { get; private set; }

    public Task<TimeTrackingCheckpoint?> LoadCheckpointAsync(string profileId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Checkpoint);

    public Task SaveAsync(
        string profileId,
        TimeTrackingCheckpoint checkpoint,
        IReadOnlyList<DailyUsageSlice> usageSlices,
        CancellationToken cancellationToken = default)
    {
        foreach (var usageSlice in usageSlices)
        {
            usageByDate.TryGetValue(usageSlice.Date, out var current);
            usageByDate[usageSlice.Date] = current + usageSlice.ActiveDuration;
        }

        Checkpoint = checkpoint;
        return Task.CompletedTask;
    }

    public Task<TimeSpan> GetUsageAsync(string profileId, DateOnly date, CancellationToken cancellationToken = default)
    {
        usageByDate.TryGetValue(date, out var usage);
        return Task.FromResult(usage);
    }
}

internal sealed class FakeSessionEventSource(
    SessionSnapshot initialSnapshot,
    IReadOnlyList<SessionSnapshot> snapshots) : ISessionEventSource
{
    public Task<SessionSnapshot> GetInitialSnapshotAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(initialSnapshot);

    public async IAsyncEnumerable<SessionSnapshot> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var snapshot in snapshots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return snapshot;
            await Task.Yield();
        }
    }
}