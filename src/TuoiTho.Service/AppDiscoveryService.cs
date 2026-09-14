using System.Diagnostics;
using TuoiTho.Core.Policy;
using TuoiTho.Core.Time;
namespace TuoiTho.Service;
public interface IManagedSessionAppDiscovery { IReadOnlyList<AppIdentity> Discover(int sessionId); }
public sealed class WindowsManagedSessionAppDiscovery : IManagedSessionAppDiscovery
{
 public IReadOnlyList<AppIdentity> Discover(int sessionId)
 {
  var apps=new Dictionary<string,AppIdentity>(StringComparer.OrdinalIgnoreCase);
  foreach(var process in Process.GetProcesses()) try { if(process.SessionId!=sessionId) continue; var path=process.MainModule?.FileName; if(string.IsNullOrWhiteSpace(path)) continue; var info=FileVersionInfo.GetVersionInfo(path); var identity=AppIdentity.FromExecutablePath(path,info.FileDescription,info.CompanyName,info.ProductName); apps.TryAdd(identity.NormalizedExecutablePath,identity); } catch(InvalidOperationException){} catch(System.ComponentModel.Win32Exception){} finally { process.Dispose(); }
  return apps.Values.ToArray();
 }
}
public sealed class AppDiscoveryService(IDeviceTimePolicyStore timePolicies,IAppPolicyStore appPolicies,IManagedSessionAppDiscovery discovery,IClock clock,ILogger<AppDiscoveryService> logger):BackgroundService
{
 protected override async Task ExecuteAsync(CancellationToken stoppingToken){while(!stoppingToken.IsCancellationRequested){try{var profile=Environment.GetEnvironmentVariable("TuoiTho__ProfileId")??"m1-child";var policy=await timePolicies.LoadAsync(profile,stoppingToken);if(policy is not null){var now=clock.UtcNow;foreach(var app in discovery.Discover(policy.ManagedSessionId))await appPolicies.RecordObservationAsync(new(policy.ProfileId,policy.ManagedSessionId,app,now,now),stoppingToken);}}catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){}catch(Exception e){AppDiscoveryLog.Failed(logger,e);}await Task.Delay(TimeSpan.FromSeconds(5),stoppingToken);}}
}
internal static partial class AppDiscoveryLog
{
 [LoggerMessage(EventId=2400,Level=LogLevel.Warning,Message="M2 app discovery failed; no process is controlled.")]
 public static partial void Failed(ILogger logger,Exception exception);
}