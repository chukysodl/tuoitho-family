using System.Runtime.InteropServices;

namespace TuoiTho.Service;

public interface IWindowsIdleTimeProvider
{
    TimeSpan GetIdleDuration();
}

public sealed class WindowsIdleTimeProvider : IWindowsIdleTimeProvider
{
    public TimeSpan GetIdleDuration()
    {
        var lastInputInfo = new LastInputInfo
        {
            Size = (uint)Marshal.SizeOf<LastInputInfo>()
        };
        if (!GetLastInputInfo(ref lastInputInfo))
        {
            return TimeSpan.Zero;
        }

        var elapsedMilliseconds = unchecked(GetTickCount() - lastInputInfo.Time);
        return TimeSpan.FromMilliseconds(elapsedMilliseconds);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetLastInputInfo(ref LastInputInfo lastInputInfo);

    [DllImport("kernel32.dll")]
    private static extern uint GetTickCount();

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }
}