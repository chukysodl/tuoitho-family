namespace TuoiTho.Core.Time;

public readonly record struct DailyUsageSlice
{
    public DailyUsageSlice(DateOnly date, TimeSpan activeDuration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(activeDuration, TimeSpan.Zero);
        Date = date;
        ActiveDuration = activeDuration;
    }

    public DateOnly Date { get; }

    public TimeSpan ActiveDuration { get; }
}