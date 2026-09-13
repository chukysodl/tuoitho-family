using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;
using TuoiTho.Core.Time;

namespace TuoiTho.Service;

public sealed record WtsRawSessionActivity(
    int InfoExBytesReturned,
    int InfoExLevel,
    int SessionId,
    int SessionState,
    int SessionFlags,
    long InfoExLastInputFileTime,
    long InfoExCurrentFileTime,
    int? SessionInfoBytesReturned,
    long? SessionInfoLastInputFileTime,
    long? SessionInfoCurrentFileTime);

public sealed record WindowsSessionActivity(
    SessionActivityState State,
    TimeSpan? IdleDuration,
    DateTimeOffset? LastInputTimeUtc,
    DateTimeOffset? CurrentTimeUtc,
    int? Win32Error,
    string? Diagnostic,
    bool QuerySucceeded = true,
    WtsRawSessionActivity? Raw = null);

public interface IWindowsSessionActivityProvider
{
    WindowsSessionActivity GetActivity(int sessionId);
}

public sealed class WtsSessionActivityProvider(IOptions<WindowsTimeTrackingOptions> options) : IWindowsSessionActivityProvider
{
    private const int WtsSessionInfo = 24;
    private const int WtsSessionInfoEx = 25;
    private readonly TimeSpan idleThreshold = TimeSpan.FromMinutes(options.Value.IdleThresholdMinutes);

    public WindowsSessionActivity GetActivity(int sessionId)
    {
        if (sessionId <= 0)
        {
            return new(SessionActivityState.LoggedOut, null, null, null, null, "No managed Windows session.", false);
        }

        if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, WtsSessionInfoEx, out var buffer, out var bytesReturned))
        {
            var error = Marshal.GetLastWin32Error();
            return new(SessionActivityState.Unknown, null, null, null, error, $"WTSQuerySessionInformation(WTSSessionInfoEx) failed: {error}.", false);
        }

        try
        {
            if (bytesReturned < Marshal.SizeOf<WtsInfoEx>())
            {
                return new(SessionActivityState.Unknown, null, null, null, null, $"WTSSessionInfoEx returned {bytesReturned} bytes; expected at least {Marshal.SizeOf<WtsInfoEx>()}.");
            }

            var info = Marshal.PtrToStructure<WtsInfoEx>(buffer);
            if (info.Level != 1)
            {
                return new(SessionActivityState.Unknown, null, null, null, null, $"Unsupported WTSInfoEx level {info.Level}.");
            }

            var data = info.Data;
            var lastInputFileTime = data.LastInputTime;
            var currentFileTime = data.CurrentTime;
            int? sessionInfoBytesReturned = null;
            long? sessionInfoLastInputFileTime = null;
            long? sessionInfoCurrentFileTime = null;

            // Some local Windows configurations report zero timestamps in WTSSessionInfoEx.
            // WTSSessionInfo is the documented compatible fallback and is queried only then.
            if (lastInputFileTime <= 0 || currentFileTime <= 0)
            {
                var fallback = QuerySessionInfo(sessionId);
                if (fallback is not null)
                {
                    sessionInfoBytesReturned = fallback.Value.BytesReturned;
                    sessionInfoLastInputFileTime = fallback.Value.Info.LastInputTime;
                    sessionInfoCurrentFileTime = fallback.Value.Info.CurrentTime;
                    if (lastInputFileTime <= 0)
                    {
                        lastInputFileTime = fallback.Value.Info.LastInputTime;
                    }

                    if (currentFileTime <= 0)
                    {
                        currentFileTime = fallback.Value.Info.CurrentTime;
                    }
                }
            }

            var raw = new WtsRawSessionActivity(
                bytesReturned,
                info.Level,
                data.SessionId,
                data.SessionState,
                data.SessionFlags,
                data.LastInputTime,
                data.CurrentTime,
                sessionInfoBytesReturned,
                sessionInfoLastInputFileTime,
                sessionInfoCurrentFileTime);
            var currentTime = ToUtc(currentFileTime);
            var lastInputTime = ToUtc(lastInputFileTime);

            if (data.SessionState is not (0 or 1))
            {
                return new(SessionActivityState.LoggedOut, null, lastInputTime, currentTime, null, $"WTS connect state {data.SessionState}.", true, raw);
            }

            if (data.SessionFlags == 0)
            {
                return new(SessionActivityState.Locked, null, lastInputTime, currentTime, null, null, true, raw);
            }

            if (currentTime is null || lastInputTime is null)
            {
                return new(
                    SessionActivityState.Unknown,
                    null,
                    lastInputTime,
                    currentTime,
                    null,
                    "WTS returned zero LastInputTime/CurrentTime in both WTSSessionInfoEx and the WTSSessionInfo fallback.",
                    true,
                    raw);
            }

            var idleDuration = currentTime.Value - lastInputTime.Value;
            if (idleDuration < TimeSpan.Zero)
            {
                idleDuration = TimeSpan.Zero;
            }

            return new(
                idleDuration >= idleThreshold ? SessionActivityState.Idle : SessionActivityState.Active,
                idleDuration,
                lastInputTime,
                currentTime,
                null,
                null,
                true,
                raw);
        }
        catch (Exception exception) when (exception is ArgumentException or TypeLoadException)
        {
            return new(SessionActivityState.Unknown, null, null, null, null, $"Unable to marshal WTS session information: {exception.Message}");
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    private static (WtsInfo Info, int BytesReturned)? QuerySessionInfo(int sessionId)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, WtsSessionInfo, out var buffer, out var bytesReturned))
        {
            return null;
        }

        try
        {
            return bytesReturned < Marshal.SizeOf<WtsInfo>()
                ? null
                : (Marshal.PtrToStructure<WtsInfo>(buffer), bytesReturned);
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    private static DateTimeOffset? ToUtc(long fileTime) =>
        fileTime <= 0 ? null : DateTimeOffset.FromFileTime(fileTime).ToUniversalTime();

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSQuerySessionInformation(
        IntPtr server,
        int sessionId,
        int infoClass,
        out IntPtr buffer,
        out int bytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WtsInfoEx
    {
        public int Level;
        public WtsInfoExLevel1 Data;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WtsInfoExLevel1
    {
        public int SessionId;
        public int SessionState;
        public int SessionFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)] public string WinStationName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)] public string UserName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 18)] public string DomainName;
        public long LogonTime;
        public long ConnectTime;
        public long DisconnectTime;
        public long LastInputTime;
        public long CurrentTime;
        public int IncomingBytes;
        public int OutgoingBytes;
        public int IncomingFrames;
        public int OutgoingFrames;
        public int IncomingCompressedBytes;
        public int OutgoingCompressedBytes;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WtsInfo
    {
        public int State;
        public int SessionId;
        public int IncomingBytes;
        public int OutgoingBytes;
        public int IncomingFrames;
        public int OutgoingFrames;
        public int IncomingCompressedBytes;
        public int OutgoingCompressedBytes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string WinStationName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 17)] public string DomainName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)] public string UserName;
        public long ConnectTime;
        public long DisconnectTime;
        public long LastInputTime;
        public long LogonTime;
        public long CurrentTime;
    }
}