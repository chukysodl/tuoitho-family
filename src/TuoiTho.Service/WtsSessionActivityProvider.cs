using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;
using TuoiTho.Core.Time;
namespace TuoiTho.Service;
public sealed record WindowsSessionActivity(SessionActivityState State,TimeSpan? IdleDuration,DateTimeOffset? LastInputTimeUtc,DateTimeOffset? CurrentTimeUtc,int? Win32Error,string? Diagnostic);
public interface IWindowsSessionActivityProvider{WindowsSessionActivity GetActivity(int sessionId);}
public sealed class WtsSessionActivityProvider(IOptions<WindowsTimeTrackingOptions> options):IWindowsSessionActivityProvider
{
 private const int WtsInfoExClass=25;private readonly TimeSpan idleThreshold=TimeSpan.FromMinutes(options.Value.IdleThresholdMinutes);
 public WindowsSessionActivity GetActivity(int sessionId)
 {
  if(sessionId<=0)return new(SessionActivityState.LoggedOut,null,null,null,null,"No managed Windows session.");
  if(!WTSQuerySessionInformation(IntPtr.Zero,sessionId,WtsInfoExClass,out var buffer,out _)){var error=Marshal.GetLastWin32Error();return new(SessionActivityState.Unknown,null,null,null,error,$"WTSQuerySessionInformation(WTSInfoEx) failed: {error}.");}
  try{var info=Marshal.PtrToStructure<WtsInfoEx>(buffer);if(info.Level!=1)return new(SessionActivityState.Unknown,null,null,null,null,$"Unsupported WTSInfoEx level {info.Level}.");var data=info.Data;var now=ToUtc(data.CurrentTime);var last=ToUtc(data.LastInputTime);if(data.SessionState is not (0 or 1))return new(SessionActivityState.LoggedOut,null,last,now,null,$"WTS connect state {data.SessionState}.");if(data.SessionFlags==0)return new(SessionActivityState.Locked,null,last,now,null,null);if(now is null||last is null)return new(SessionActivityState.Unknown,null,last,now,null,"WTS did not provide LastInputTime/CurrentTime.");var idle=now.Value-last.Value;if(idle<TimeSpan.Zero)idle=TimeSpan.Zero;return new(idle>=idleThreshold?SessionActivityState.Idle:SessionActivityState.Active,idle,last,now,null,null);}
  catch(Exception e)when(e is ArgumentException or TypeLoadException){return new(SessionActivityState.Unknown,null,null,null,null,$"Unable to marshal WTSInfoEx: {e.Message}");}
  finally{WTSFreeMemory(buffer);}
 }
 private static DateTimeOffset? ToUtc(long fileTime)=>fileTime<=0?null:DateTimeOffset.FromFileTime(fileTime).ToUniversalTime();
 [DllImport("wtsapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool WTSQuerySessionInformation(IntPtr server,int sessionId,int infoClass,out IntPtr buffer,out int bytes);
 [DllImport("wtsapi32.dll")]private static extern void WTSFreeMemory(IntPtr buffer);
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct WtsInfoEx{public int Level;public WtsInfoExLevel1 Data;}
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct WtsInfoExLevel1{public int SessionId;public int SessionState;public int SessionFlags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=33)]public string WinStationName;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=21)]public string UserName;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=18)]public string DomainName;public long LogonTime;public long ConnectTime;public long DisconnectTime;public long LastInputTime;public long CurrentTime;public int IncomingBytes;public int OutgoingBytes;public int IncomingFrames;public int OutgoingFrames;public int IncomingCompressedBytes;public int OutgoingCompressedBytes;}
}