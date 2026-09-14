namespace TuoiTho.SessionAgent;

public sealed class SessionAgentOptions { public const string SectionName = "SessionAgent"; public string ProfileId { get; init; } = "local-child"; public bool M1TestMode { get; init; } public string? M1WarningPublisherSid { get; init; } }

public sealed class Worker(LocalWarningListener listener, LocalActivityReporter reporter, IChildSoftLockController softLock, ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        SessionAgentLog.Started(logger);
        try
        {
            var warnings = ListenWarningsAsync(stoppingToken);
            var activity = reporter.RunAsync(stoppingToken);
            await Task.WhenAll(warnings, activity);
        }
        finally
        {
            softLock.Dispose();
            SessionAgentLog.Stopped(logger);
        }
    }

    private async Task ListenWarningsAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try { await listener.ListenOnceAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception e)
            {
                SessionAgentLog.ListenerFailed(logger, e);
                await Task.Delay(TimeSpan.FromSeconds(1), token);
            }
        }
    }
}

internal static partial class SessionAgentLog
{
    [LoggerMessage(EventId = 2000, Level = LogLevel.Information, Message = "TuoiTho session agent started. {Component}")] public static partial void Started(ILogger logger, string component = "SessionAgent");
    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "TuoiTho session agent stopped. {Component}")] public static partial void Stopped(ILogger logger, string component = "SessionAgent");
    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning, Message = "Warning listener recovered from a failed client/message.")] public static partial void ListenerFailed(ILogger logger, Exception exception);
}