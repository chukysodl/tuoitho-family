using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TuoiTho.Core.Policy;
using TuoiTho.Storage;

namespace TuoiTho.Service;

public sealed class ParentControlListener(
    SqliteDatabase database,
    ParentControlService controls,
    IOptions<ParentControlOptions> options,
    ILogger<ParentControlListener> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (settings.AllowedParentSids.Length == 0)
        {
            ParentControlLog.Disabled(logger);
            return;
        }

        await database.InitializeAsync(stoppingToken);
        var allowed = new HashSet<string>(settings.AllowedParentSids, StringComparer.OrdinalIgnoreCase);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var pipe = ParentControlPipeSecurity.CreateServer(settings.PipeName, allowed);
                await pipe.WaitForConnectionAsync(stoppingToken);
                var actualSid = GetAuthenticatedSid(pipe);
                var command = await ReadCommandAsync(pipe, stoppingToken);
                if (actualSid is null || command is null)
                {
                    ParentControlLog.Rejected(logger);
                    continue;
                }
                var result = await controls.ExecuteAsync(command, actualSid, allowed, stoppingToken);
                if (!result.Accepted) ParentControlLog.Rejected(logger);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception) { ParentControlLog.Failed(logger, exception); }
        }
    }

    public static string? GetAuthenticatedSid(NamedPipeServerStream pipe)
    {
        string? sid = null;
        pipe.RunAsClient(() => sid = WindowsIdentity.GetCurrent().User?.Value);
        return sid;
    }

    public static async Task<ParentControlCommand?> ReadCommandAsync(Stream stream, CancellationToken cancellationToken)
    {
        try { return await JsonSerializer.DeserializeAsync<ParentControlCommand>(stream, cancellationToken: cancellationToken); }
        catch (JsonException) { return null; }
    }
}

internal static partial class ParentControlLog
{
    [LoggerMessage(EventId = 2300, Level = LogLevel.Warning, Message = "Parent control pipe disabled because no parent SIDs are configured.")]
    public static partial void Disabled(ILogger logger);
    [LoggerMessage(EventId = 2301, Level = LogLevel.Warning, Message = "Parent control command rejected.")]
    public static partial void Rejected(ILogger logger);
    [LoggerMessage(EventId = 2302, Level = LogLevel.Error, Message = "Parent control listener failure.")]
    public static partial void Failed(ILogger logger, Exception exception);
}