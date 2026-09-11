using System.IO.Pipes;using System.Text.Json;using TuoiTho.Core.Policy;
namespace TuoiTho.Service;
public sealed class LocalSessionWarningPublisher
{
 public static async Task PublishAsync(SessionWarning warning,CancellationToken cancellationToken=default)
 { using var pipe=new NamedPipeClientStream(".",$"TuoiTho.Warning.{warning.SessionId}",PipeDirection.Out,PipeOptions.Asynchronous);await pipe.ConnectAsync(1000,cancellationToken);await JsonSerializer.SerializeAsync(pipe,warning,cancellationToken:cancellationToken);await pipe.FlushAsync(cancellationToken); }
}