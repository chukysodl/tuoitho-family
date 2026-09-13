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
    private readonly Dictionary<(string ProfileId, DateOnly Date), TimeSpan> usageByDate = [];
    private readonly Dictionary<string, TimeTrackingCheckpoint> checkpoints = [];

    public Task<TimeTrackingCheckpoint?> LoadCheckpointAsync(string profileId, CancellationToken cancellationToken = default) =>
        Task.FromResult(checkpoints.TryGetValue(profileId, out var checkpoint) ? checkpoint : null);

    public Task SaveAsync(
        string profileId,
        TimeTrackingCheckpoint checkpoint,
        IReadOnlyList<DailyUsageSlice> usageSlices,
        CancellationToken cancellationToken = default)
    {
        foreach (var usageSlice in usageSlices)
        {
            var key = (profileId, usageSlice.Date);
            usageByDate.TryGetValue(key, out var current);
            usageByDate[key] = current + usageSlice.ActiveDuration;
        }

        checkpoints[profileId] = checkpoint;
        return Task.CompletedTask;
    }

    public Task ResetUsageAsync(string profileId, DateOnly date, CancellationToken cancellationToken = default)
    {
        usageByDate.Remove((profileId, date));
        checkpoints.Remove(profileId);
        return Task.CompletedTask;
    }

    public Task<TimeSpan> GetUsageAsync(string profileId, DateOnly date, CancellationToken cancellationToken = default)
    {
        usageByDate.TryGetValue((profileId, date), out var usage);
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