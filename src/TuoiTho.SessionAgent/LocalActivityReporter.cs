using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TuoiTho.Core.Time;

namespace TuoiTho.SessionAgent;

public sealed class LocalActivityReporter(
    IOptions<SessionAgentOptions> options,
    IInteractiveActivitySampler sampler,
    ILogger<LocalActivityReporter> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var sessionId = System.Diagnostics.Process.GetCurrentProcess().SessionId;
        sampler.Start();
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    var activity = sampler.GetActivity();
                    var sample = new SessionActivitySample(
                        settings.ProfileId,
                        sessionId,
                        DateTimeOffset.UtcNow,
                        activity.DeviceIdleSeconds ?? 0,
                        activity.RawInputAvailable,
                        activity.WindowsIdleSeconds);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(1));
                    using var pipe = new NamedPipeClientStream(".", "TuoiTho.Activity", PipeDirection.Out, PipeOptions.Asynchronous);
                    await pipe.ConnectAsync(timeout.Token);
                    using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                    await writer.WriteLineAsync(JsonSerializer.Serialize(sample));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException or InvalidOperationException)
                {
                    ActivityReporterLog.Unavailable(logger, exception);
                }
            }
        }
        finally
        {
            sampler.Dispose();
        }
    }
}

internal static partial class ActivityReporterLog
{
    [LoggerMessage(EventId = 2200, Level = LogLevel.Debug, Message = "Activity pipe unavailable; the next sample will retry.")]
    public static partial void Unavailable(ILogger logger, Exception exception);
}