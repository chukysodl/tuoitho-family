namespace TuoiTho.Core.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }

    TimeZoneInfo LocalTimeZone { get; }
}