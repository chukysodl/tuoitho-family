namespace TuoiTho.Core.Time;

public sealed class SessionTimeEngine : IDisposable
{
    private readonly IClock clock;
    private readonly string profileId;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ITimeUsageStore usageStore;
    private TimeTrackingCheckpoint? checkpoint;

    public SessionTimeEngine(ITimeUsageStore usageStore, IClock clock, string profileId)
    {
        this.usageStore = usageStore ?? throw new ArgumentNullException(nameof(usageStore));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.profileId = string.IsNullOrWhiteSpace(profileId)
            ? throw new ArgumentException("A profile ID is required.", nameof(profileId))
            : profileId.Trim();
    }

    public async Task InitializeAsync(SessionSnapshot currentSnapshot, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var persistedCheckpoint = await usageStore.LoadCheckpointAsync(profileId, cancellationToken);
            if (persistedCheckpoint is null)
            {
                checkpoint = CreateCheckpoint(currentSnapshot);
                await usageStore.SaveAsync(profileId, checkpoint, [], cancellationToken);
                return;
            }

            checkpoint = persistedCheckpoint.NormalizeToUtc();
            await ApplySnapshotAsync(currentSnapshot, isInitialReconciliation: true, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<TimeEngineApplyResult> ApplyObservationAsync(
        SessionSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await ApplySnapshotAsync(snapshot, isInitialReconciliation: false, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    // Holds the same gate as heartbeats so a pre-reset interval can never be persisted after this returns.
    public async Task<bool> ResetForM1Async(
        DateOnly usageDate,
        Func<SessionSnapshot?>? currentSnapshotProvider = null,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var currentCheckpoint = checkpoint;
            if (currentCheckpoint is null)
            {
                return false;
            }

            var resetSnapshot = currentSnapshotProvider?.Invoke()
                ?? new SessionSnapshot(
                    currentCheckpoint.SessionId,
                    currentCheckpoint.State,
                    clock.UtcNow,
                    currentCheckpoint.BootStartedAtUtc);

            if (resetSnapshot.SessionId != currentCheckpoint.SessionId)
            {
                throw new InvalidOperationException("The M1 reset snapshot does not match the managed session.");
            }

            var resetCheckpoint = CreateCheckpoint(resetSnapshot);
            await usageStore.ResetUsageAndSaveCheckpointAsync(profileId, usageDate, resetCheckpoint, cancellationToken);
            checkpoint = resetCheckpoint;
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<TimeEngineApplyResult> StopAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var currentCheckpoint = checkpoint ?? throw new InvalidOperationException("The time engine has not been initialized.");
            var snapshot = new SessionSnapshot(
                currentCheckpoint.SessionId,
                currentCheckpoint.State,
                clock.UtcNow,
                currentCheckpoint.BootStartedAtUtc);
            return await ApplySnapshotAsync(snapshot, isInitialReconciliation: false, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose() => gate.Dispose();

    private async Task<TimeEngineApplyResult> ApplySnapshotAsync(
        SessionSnapshot snapshot,
        bool isInitialReconciliation,
        CancellationToken cancellationToken)
    {
        var currentCheckpoint = checkpoint ?? throw new InvalidOperationException("The time engine has not been initialized.");
        if (snapshot.SessionId != currentCheckpoint.SessionId)
        {
            return TimeEngineApplyResult.IgnoredDifferentSession;
        }

        if (snapshot.OccurredAtUtc < currentCheckpoint.LastObservedAtUtc)
        {
            return TimeEngineApplyResult.IgnoredOutOfOrder;
        }

        if (snapshot.OccurredAtUtc == currentCheckpoint.LastObservedAtUtc && snapshot.State == currentCheckpoint.State)
        {
            return TimeEngineApplyResult.IgnoredDuplicate;
        }

        var usageSlices = Array.Empty<DailyUsageSlice>();
        var shouldAccumulate = currentCheckpoint.State == SessionActivityState.Active
            && snapshot.OccurredAtUtc > currentCheckpoint.LastObservedAtUtc
            && (!isInitialReconciliation || snapshot.State == SessionActivityState.Active);

        if (shouldAccumulate)
        {
            var intervalStart = currentCheckpoint.LastObservedAtUtc;
            if (snapshot.BootStartedAtUtc > intervalStart)
            {
                intervalStart = snapshot.BootStartedAtUtc;
            }

            if (snapshot.OccurredAtUtc > intervalStart)
            {
                usageSlices = SplitAcrossLocalDates(intervalStart, snapshot.OccurredAtUtc).ToArray();
            }
        }

        checkpoint = new TimeTrackingCheckpoint(
            currentCheckpoint.SessionId,
            snapshot.State,
            snapshot.OccurredAtUtc,
            snapshot.BootStartedAtUtc).NormalizeToUtc();
        await usageStore.SaveAsync(profileId, checkpoint, usageSlices, cancellationToken);
        return TimeEngineApplyResult.Applied;
    }

    private static TimeTrackingCheckpoint CreateCheckpoint(SessionSnapshot snapshot) => new TimeTrackingCheckpoint(
        snapshot.SessionId,
        snapshot.State,
        snapshot.OccurredAtUtc,
        snapshot.BootStartedAtUtc).NormalizeToUtc();

    private IEnumerable<DailyUsageSlice> SplitAcrossLocalDates(DateTimeOffset intervalStartUtc, DateTimeOffset intervalEndUtc)
    {
        var cursor = intervalStartUtc;
        while (cursor < intervalEndUtc)
        {
            var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(cursor, clock.LocalTimeZone).DateTime);
            var nextBoundaryUtc = GetStartOfLocalDateUtc(localDate.AddDays(1), clock.LocalTimeZone);
            var segmentEndUtc = nextBoundaryUtc > cursor && nextBoundaryUtc < intervalEndUtc
                ? nextBoundaryUtc
                : intervalEndUtc;

            yield return new DailyUsageSlice(localDate, segmentEndUtc - cursor);
            cursor = segmentEndUtc;
        }
    }

    private static DateTimeOffset GetStartOfLocalDateUtc(DateOnly date, TimeZoneInfo timeZone)
    {
        var localMidnight = date.ToDateTime(TimeOnly.MinValue);
        while (timeZone.IsInvalidTime(localMidnight))
        {
            localMidnight = localMidnight.AddMinutes(1);
        }

        var offset = timeZone.IsAmbiguousTime(localMidnight)
            ? timeZone.GetAmbiguousTimeOffsets(localMidnight).Max()
            : timeZone.GetUtcOffset(localMidnight);
        return new DateTimeOffset(localMidnight, offset).ToUniversalTime();
    }
}