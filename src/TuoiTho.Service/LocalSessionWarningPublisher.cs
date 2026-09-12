using System.IO.Pipes;
using System.Text.Json;
using TuoiTho.Core.Policy;
namespace TuoiTho.Service;
public sealed class LocalSessionWarningPublisher(ILogger<LocalSessionWarningPublisher> logger,Func<int,string>? pipeName=null)
{
 public async Task PublishAsync(SessionWarning warning,CancellationToken cancellationToken=default){await TryPublishAsync(warning,cancellationToken);}
 public async Task<bool> TryPublishAsync(SessionWarning warning,CancellationToken cancellationToken=default)
 {
  try{using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);timeout.CancelAfter(TimeSpan.FromSeconds(1));using var pipe=new NamedPipeClientStream(".",(pipeName??(id=>$"TuoiTho.Warning.{id}"))(warning.SessionId),PipeDirection.Out,PipeOptions.Asynchronous);await pipe.ConnectAsync(timeout.Token);await JsonSerializer.SerializeAsync(pipe,warning,cancellationToken:timeout.Token);await pipe.FlushAsync(timeout.Token);return true;}
  catch(OperationCanceledException) when(!cancellationToken.IsCancellationRequested){WarningPublishLog.Unavailable(logger,warning.SessionId);return false;}catch(IOException){WarningPublishLog.Unavailable(logger,warning.SessionId);return false;}catch(UnauthorizedAccessException){WarningPublishLog.Unavailable(logger,warning.SessionId);return false;}
 }
}
internal static partial class WarningPublishLog{[LoggerMessage(EventId=2400,Level=LogLevel.Debug,Message="SessionAgent warning pipe unavailable for session {SessionId}; will retry on next policy evaluation.")]public static partial void Unavailable(ILogger logger,int sessionId);}