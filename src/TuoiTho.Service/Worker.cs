using Microsoft.Extensions.Options;
using TuoiTho.Core.Time;
using TuoiTho.Storage;

namespace TuoiTho.Service;
public sealed class Worker(SqliteDatabase database, SessionTimeEngine engine, WindowsSessionEventSource sessionEventSource, DevicePolicyCoordinator coordinator, IOptions<WindowsTimeTrackingOptions> options, ILogger<Worker> logger) : BackgroundService
{
 protected override async Task ExecuteAsync(CancellationToken stoppingToken)
 {
  TimeTrackingLog.Starting(logger); await database.InitializeAsync(stoppingToken);
  try { var tracking=new SessionTimeTrackingHost(engine,sessionEventSource); await Task.WhenAll(tracking.RunAsync(stoppingToken),coordinator.RunAsync(options.Value.ProfileId,stoppingToken)); }
  catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){} catch(Exception e){TimeTrackingLog.Failed(logger,e);throw;} finally {TimeTrackingLog.Stopped(logger);}
 }
}
internal static partial class TimeTrackingLog { [LoggerMessage(EventId=1100,Level=LogLevel.Information,Message="TuoiTho time tracking service started.")] public static partial void Starting(ILogger logger); [LoggerMessage(EventId=1101,Level=LogLevel.Information,Message="TuoiTho time tracking service stopped.")] public static partial void Stopped(ILogger logger); [LoggerMessage(EventId=1102,Level=LogLevel.Error,Message="TuoiTho time tracking service failed.")] public static partial void Failed(ILogger logger,Exception exception); }