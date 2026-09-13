using System.Runtime.InteropServices;

namespace TuoiTho.SessionAgent;

public interface IInteractiveActivitySampler
{
    double GetIdleSeconds();
}

/// <summary>Reads only elapsed idle time from the interactive SessionAgent desktop.</summary>
public sealed class InteractiveActivitySampler : IInteractiveActivitySampler
{
    public double GetIdleSeconds()
    {
        var input = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref input))
        {
            throw new InvalidOperationException($"GetLastInputInfo failed: {Marshal.GetLastWin32Error()}.");
        }

        var elapsedMilliseconds = unchecked((uint)Environment.TickCount) - input.TickCount;
        return TimeSpan.FromMilliseconds(elapsedMilliseconds).TotalSeconds;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetLastInputInfo(ref LastInputInfo lastInputInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint TickCount;
    }
}