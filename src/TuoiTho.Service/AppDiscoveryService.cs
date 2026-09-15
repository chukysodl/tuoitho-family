using System.ComponentModel;
using System.Diagnostics;
using TuoiTho.Core.Policy;
using TuoiTho.Core.Time;
namespace TuoiTho.Service;
public sealed record AppDiscoveryResult(DateTimeOffset LastScanAtUtc,int ProcessesExamined,int AppsDiscovered,int AppsSkippedInaccessible,string? DiscoveryError,IReadOnlyList<AppIdentity> Applications);
public interface IManagedSessionAppDiscovery { AppDiscoveryResult Discover(int sessionId); }
public sealed class WindowsManagedSessionAppDiscovery(IClock clock) : IManagedSessionAppDiscovery
{
 public static bool IsManagedSession(int processSessionId,int managedSessionId)=>processSessionId==managedSessionId;
 public AppDiscoveryResult Discover(int sessionId)
 {
  var apps=new Dictionary<string,AppIdentity>(StringComparer.OrdinalIgnoreCase);var examined=0;var skipped=0;string? error=null;
  try { foreach(var process in Process.GetProcesses()) try { if(!IsManagedSession(process.SessionId,sessionId))continue;examined++;var path=process.MainModule?.FileName;if(string.IsNullOrWhiteSpace(path)){skipped++;continue;}var info=FileVersionInfo.GetVersionInfo(path);var identity=AppIdentity.FromExecutablePath(path,info.FileDescription,info.CompanyName,info.ProductName);apps.TryAdd(identity.NormalizedExecutablePath,identity); } catch(Exception){skipped++;}finally{process.Dispose();} }
  catch(Exception e) when(e is InvalidOperationException or Win32Exception){error=e.Message;}
  return new(clock.UtcNow,examined,apps.Count,skipped,error,apps.Values.ToArray());
 }
}
public sealed class AppDiscoveryService(IDeviceTimePolicyStore timePolicies,IAppPolicyStore appPolicies,IManagedSessionAppDiscovery discovery,ILogger<AppDiscoveryService> logger):BackgroundService
{
 protected override async Task ExecuteAsync(CancellationToken stoppingToken){while(!stoppingToken.IsCancellationRequested){try{var profile=Environment.GetEnvironmentVariable("TuoiTho__ProfileId")??"m1-child";var policy=await timePolicies.LoadAsync(profile,stoppingToken);if(policy is not null){var result=discovery.Discover(policy.ManagedSessionId);foreach(var app in result.Applications)await appPolicies.RecordObservationAsync(new(policy.ProfileId,policy.ManagedSessionId,app,result.LastScanAtUtc,result.LastScanAtUtc),stoppingToken);}}catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){}catch(Exception e){AppDiscoveryLog.Failed(logger,e);}await Task.Delay(TimeSpan.FromSeconds(5),stoppingToken);}}
}
internal static partial class AppDiscoveryLog{[LoggerMessage(EventId=2400,Level=LogLevel.Warning,Message="M2 app discovery failed; no process is controlled.")]public static partial void Failed(ILogger logger,Exception exception);}