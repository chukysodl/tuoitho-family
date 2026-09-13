using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TuoiTho.Core.Policy;
using TuoiTho.Core.Time;
using TuoiTho.Storage;

namespace TuoiTho.Service;

public sealed class ActivitySampleListener(
    SqliteDatabase database,
    IDeviceTimePolicyStore policies,
    ITimeUsageStore usage,
    IClock clock,
    IWindowsSessionActivityProvider activityProvider,
    ActivitySampleCache cache,
    IOptions<WindowsTimeTrackingOptions> options,
    ILogger<ActivitySampleListener> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await database.InitializeAsync(stoppingToken);
        var profileId = options.Value.ProfileId;
        while (!stoppingToken.IsCancellationRequested)
        {
            var policy = await policies.LoadAsync(profileId, stoppingToken);
            if (policy?.ManagedUserSid is not { Length: > 0 } managedSidText)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                continue;
            }

            SecurityIdentifier managedSid;
            try { managedSid = new SecurityIdentifier(managedSidText); }
            catch (ArgumentException) { ActivitySampleLog.InvalidPolicySid(logger); await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); continue; }

            try
            {
                using var pipe = ActivityPipeSecurity.CreateServer("TuoiTho.Activity", managedSid);
                await pipe.WaitForConnectionAsync(stoppingToken);
                using var reader = new StreamReader(pipe, leaveOpen: true);
                var sample = await ReadSampleAsync(reader, stoppingToken);
                var callerSid = sample is null ? null : GetAuthenticatedSid(pipe);
                if (sample is null || callerSid != managedSid.Value || !cache.TryAccept(sample, policy.ProfileId, policy.ManagedSessionId))
                {
                    ActivitySampleLog.Rejected(logger);
                    continue;
                }

                var local = TimeZoneInfo.ConvertTime(clock.UtcNow, clock.LocalTimeZone);
                var recordedToday = await usage.GetUsageAsync(policy.ProfileId, DateOnly.FromDateTime(local.DateTime), stoppingToken);
                await M1ActivityTraceWriter.AppendAsync(policy, sample, activityProvider.GetActivity(policy.ManagedSessionId).State, recordedToday, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception) { ActivitySampleLog.Failed(logger, exception); }
        }
    }

    public static string? GetAuthenticatedSid(NamedPipeServerStream pipe)
    {
        string? sid = null;
        pipe.RunAsClient(() => sid = WindowsIdentity.GetCurrent().User?.Value);
        return sid;
    }

    public static async Task<SessionActivitySample?> ReadSampleAsync(TextReader reader, CancellationToken token)
    {
        try { var line = await reader.ReadLineAsync(token); return string.IsNullOrWhiteSpace(line) ? null : JsonSerializer.Deserialize<SessionActivitySample>(line); }
        catch (JsonException) { return null; }
    }
}

internal static partial class ActivitySampleLog
{
    [LoggerMessage(EventId = 2500, Level = LogLevel.Warning, Message = "Activity pipe rejected a malformed, unauthorized, or mismatched sample.")]
    public static partial void Rejected(ILogger logger);
    [LoggerMessage(EventId = 2501, Level = LogLevel.Warning, Message = "Activity pipe policy has an invalid managed SID.")]
    public static partial void InvalidPolicySid(ILogger logger);
    [LoggerMessage(EventId = 2502, Level = LogLevel.Error, Message = "Activity pipe listener failure.")]
    public static partial void Failed(ILogger logger, Exception exception);
}