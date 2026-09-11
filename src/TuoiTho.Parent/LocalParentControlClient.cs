using System.IO.Pipes;
using System.Text.Json;
using TuoiTho.Core.Policy;

namespace TuoiTho.Parent;

public sealed class LocalParentControlClient(string pipeName = "TuoiTho.ParentControl")
{
    public async Task SendAsync(ParentControlCommand command, CancellationToken cancellationToken = default)
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(1000, cancellationToken);
        await JsonSerializer.SerializeAsync(pipe, command, cancellationToken: cancellationToken);
        await pipe.FlushAsync(cancellationToken);
    }
}