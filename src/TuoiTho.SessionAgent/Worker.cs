namespace TuoiTho.SessionAgent;

public sealed class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        SessionAgentLog.Started(logger);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal session shutdown.
        }
        finally
        {
            SessionAgentLog.Stopped(logger);
        }
    }
}

internal static partial class SessionAgentLog
{
    [LoggerMessage(EventId = 2000, Level = LogLevel.Information, Message = "TuoiTho session agent started. {Component}")]
    public static partial void Started(ILogger logger, string component = "SessionAgent");

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "TuoiTho session agent stopped. {Component}")]
    public static partial void Stopped(ILogger logger, string component = "SessionAgent");
}
