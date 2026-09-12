using System.IO.Pipes;using System.Text.Json;using TuoiTho.Core.Policy;
namespace TuoiTho.Parent;
public interface IParentControlClient{Task<ParentControlResult> SendAsync(ParentControlCommand command,CancellationToken token=default);}
public sealed class LocalParentControlClient(string pipeName="TuoiTho.ParentControl") : IParentControlClient
{
 public async Task<ParentControlResult> SendAsync(ParentControlCommand command,CancellationToken token=default){using var pipe=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut,PipeOptions.Asynchronous);await pipe.ConnectAsync(1000,token);using var writer=new StreamWriter(pipe,leaveOpen:true){AutoFlush=true};await writer.WriteLineAsync(JsonSerializer.Serialize(command));using var reader=new StreamReader(pipe,leaveOpen:true);var line=await reader.ReadLineAsync(token);return string.IsNullOrWhiteSpace(line)?new(false,"EMPTY_RESPONSE"):JsonSerializer.Deserialize<ParentControlResult>(line)??new(false,"MALFORMED_RESPONSE");}
}