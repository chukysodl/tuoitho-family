namespace TuoiTho.Service;

public sealed class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ServiceLog.Started(logger);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal service shutdown.
        }
        finally
        {
            ServiceLog.Stopped(logger);
        }
    }
}

internal static partial class ServiceLog
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "TuoiTho service started.")]
    public static partial void Started(ILogger logger);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "TuoiTho service stopped.")]
    public static partial void Stopped(ILogger logger);
}
