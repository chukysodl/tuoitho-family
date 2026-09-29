using System.Text.Json;
using Microsoft.Extensions.Options;
using TuoiTho.Core.Policy;

namespace TuoiTho.Service;

public sealed class ProductionProtectionOptions
{
    public const string SectionName = "ProductionProtection";
    public bool Enabled { get; init; }
    public string ProfileId { get; init; } = "m1-child";
    public string SessionAgentPath { get; init; } = string.Empty;
    public string ParentExecutablePath { get; init; } = string.Empty;
    public int PollIntervalSeconds { get; init; } = 2;

    public void Validate()
    {
        if (!Enabled) return;
        if (string.IsNullOrWhiteSpace(ProfileId)) throw new InvalidOperationException("ProductionProtection:ProfileId is required.");
        if (string.IsNullOrWhiteSpace(SessionAgentPath)) throw new InvalidOperationException("ProductionProtection:SessionAgentPath is required.");
        if (PollIntervalSeconds is < 1 or > 60) throw new InvalidOperationException("ProductionProtection:PollIntervalSeconds must be between 1 and 60.");
    }
}

public sealed record ActiveInteractiveSession(int SessionId, string UserSid);

public interface IInteractiveSessionAgentRuntime
{
    ActiveInteractiveSession? GetActiveSession();
    bool IsAgentRunning(int sessionId, string executablePath);
    void LaunchAgent(ActiveInteractiveSession session, string executablePath, string profileId, string? parentExecutablePath);
}

public sealed class ProductionSessionAgentWatchdog(
    IOptions<ProductionProtectionOptions> options,
    IDeviceTimePolicyStore policies,
    PolicyChangeSignal changes,
    IInteractiveSessionAgentRuntime runtime,
    ILogger<ProductionSessionAgentWatchdog> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var value = options.Value;
        value.Validate();
        if (!value.Enabled) return;

        ProtectionLog.Enabled(logger, value.ProfileId, value.PollIntervalSeconds);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(value.PollIntervalSeconds));
        do
        {
            await TickAsync(value, stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task TickAsync(ProductionProtectionOptions value, CancellationToken token)
    {
        var session = runtime.GetActiveSession();
        if (session is null) return;

        var policy = await policies.LoadAsync(value.ProfileId, token);
        if (policy is not null &&
            (policy.ManagedSessionId != session.SessionId ||
             !string.Equals(policy.ManagedUserSid, session.UserSid, StringComparison.OrdinalIgnoreCase) ||
             policy.TestMode))
        {
            await policies.SaveAsync(policy with
            {
                ManagedSessionId = session.SessionId,
                ManagedUserSid = session.UserSid,
                TestMode = false
            }, token);
            changes.Notify();
            ProtectionLog.SessionBound(logger, session.SessionId, session.UserSid);
        }

        RebindBrowserConfiguration(value.ProfileId, session);

        if (runtime.IsAgentRunning(session.SessionId, value.SessionAgentPath)) return;

        runtime.LaunchAgent(session, value.SessionAgentPath, value.ProfileId,
            string.IsNullOrWhiteSpace(value.ParentExecutablePath) ? null : value.ParentExecutablePath);
        changes.Notify();
        ProtectionLog.AgentRestarted(logger, session.SessionId);
    }

    private void RebindBrowserConfiguration(string profileId, ActiveInteractiveSession session)
    {
        var path = BrowserControlConfiguration.DefaultPath;
        if (!File.Exists(path)) return;

        try
        {
            var configuration = BrowserControlConfiguration.Load(path);
            if (configuration.ManagedSessionId == session.SessionId &&
                string.Equals(configuration.ManagedUserSid, session.UserSid, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(configuration.ProfileId, profileId, StringComparison.Ordinal) &&
                !configuration.TestMode)
            {
                return;
            }

            var updated = configuration with
            {
                ProfileId = profileId,
                ManagedSessionId = session.SessionId,
                ManagedUserSid = session.UserSid,
                TestMode = false
            };
            File.WriteAllText(path, JsonSerializer.Serialize(updated, new JsonSerializerOptions { WriteIndented = true }));
            ProtectionLog.BrowserBound(logger, session.SessionId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            ProtectionLog.BrowserBindFailed(logger, exception);
        }
    }
}

internal static partial class ProtectionLog
{
    [LoggerMessage(EventId = 2800, Level = LogLevel.Information, Message = "Production protection enabled for profile {ProfileId}; watchdog interval {IntervalSeconds}s.")]
    public static partial void Enabled(ILogger logger, string profileId, int intervalSeconds);

    [LoggerMessage(EventId = 2801, Level = LogLevel.Information, Message = "Managed policy rebound to interactive session {SessionId} SID {UserSid}.")]
    public static partial void SessionBound(ILogger logger, int sessionId, string userSid);

    [LoggerMessage(EventId = 2802, Level = LogLevel.Warning, Message = "SessionAgent was missing and has been restarted in session {SessionId}.")]
    public static partial void AgentRestarted(ILogger logger, int sessionId);

    [LoggerMessage(EventId = 2803, Level = LogLevel.Information, Message = "Browser-control identity rebound to interactive session {SessionId}.")]
    public static partial void BrowserBound(ILogger logger, int sessionId);

    [LoggerMessage(EventId = 2804, Level = LogLevel.Warning, Message = "Browser-control identity could not be rebound.")]
    public static partial void BrowserBindFailed(ILogger logger, Exception exception);
}
