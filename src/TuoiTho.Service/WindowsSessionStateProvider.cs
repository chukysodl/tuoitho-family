using System.Runtime.InteropServices;

namespace TuoiTho.Service;

public enum WindowsSessionState
{
    LoggedOut,
    Active,
    Locked
}

public interface IWindowsSessionStateProvider
{
    int? GetActiveConsoleSessionId();

    WindowsSessionState GetSessionState(int sessionId);
}

public sealed class WindowsSessionStateProvider : IWindowsSessionStateProvider
{
    private const uint NoActiveConsoleSession = uint.MaxValue;
    private const int WtsInfoEx = 25;
    private const int WtsActive = 0;
    private const int WtsConnected = 1;
    private const int WtsDisconnected = 4;
    private const int WtsSessionStateLock = 0;
    private const int WtsSessionStateUnlock = 1;

    public int? GetActiveConsoleSessionId()
    {
        var sessionId = WTSGetActiveConsoleSessionId();
        return sessionId == NoActiveConsoleSession ? null : checked((int)sessionId);
    }

    public WindowsSessionState GetSessionState(int sessionId)
    {
        if (!WTSQuerySessionInformation(
                IntPtr.Zero,
                sessionId,
                WtsInfoEx,
                out var buffer,
                out var bytesReturned))
        {
            return WindowsSessionState.LoggedOut;
        }

        try
        {
            if (bytesReturned < sizeof(int) * 4 || Marshal.ReadInt32(buffer) != 1)
            {
                return WindowsSessionState.LoggedOut;
            }

            var connectState = Marshal.ReadInt32(buffer, sizeof(int) * 2);
            var sessionFlags = Marshal.ReadInt32(buffer, sizeof(int) * 3);
            if (connectState == WtsDisconnected)
            {
                return WindowsSessionState.Locked;
            }

            if (connectState is not (WtsActive or WtsConnected))
            {
                return WindowsSessionState.LoggedOut;
            }

            return sessionFlags == WtsSessionStateLock
                ? WindowsSessionState.Locked
                : WindowsSessionState.Active;
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSQuerySessionInformation(
        IntPtr serverHandle,
        int sessionId,
        int wtsInfoClass,
        out IntPtr buffer,
        out int bytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);
}