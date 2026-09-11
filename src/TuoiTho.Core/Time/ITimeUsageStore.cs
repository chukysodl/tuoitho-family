namespace TuoiTho.Core.Time;

public interface ITimeUsageStore
{
    Task<TimeTrackingCheckpoint?> LoadCheckpointAsync(
        string profileId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        string profileId,
        TimeTrackingCheckpoint checkpoint,
        IReadOnlyList<DailyUsageSlice> usageSlices,
        CancellationToken cancellationToken = default);

    Task<TimeSpan> GetUsageAsync(
        string profileId,
        DateOnly usageDate,
        CancellationToken cancellationToken = default);
}