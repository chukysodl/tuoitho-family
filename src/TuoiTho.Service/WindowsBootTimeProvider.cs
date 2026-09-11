using TuoiTho.Core.Time;

namespace TuoiTho.Service;

public interface IWindowsBootTimeProvider
{
    DateTimeOffset GetBootStartedAtUtc(DateTimeOffset utcNow);
}

public sealed class WindowsBootTimeProvider : IWindowsBootTimeProvider
{
    public DateTimeOffset GetBootStartedAtUtc(DateTimeOffset utcNow) =>
        utcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
}