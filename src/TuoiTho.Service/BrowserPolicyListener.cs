using System.IO.Pipes;
using System.Text.Json;
using TuoiTho.Core.Policy;

namespace TuoiTho.Service;

public sealed class BrowserPolicyListener(
    IDeviceTimePolicyStore policies,
    IWebPolicyStore webPolicies,
    ILogger<BrowserPolicyListener> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        BrowserControlConfiguration settings;
        try
        {
            settings = BrowserControlConfiguration.Load();
        }
        catch (Exception exception)
        {
            BrowserPolicyLog.Failed(logger, exception);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var pipe = ParentControlPipeSecurity.CreateServer("TuoiTho.BrowserPolicy", [settings.ManagedUserSid]);
                await pipe.WaitForConnectionAsync(stoppingToken);
                using var reader = new StreamReader(pipe, leaveOpen: true);
                var line = await reader.ReadLineAsync(stoppingToken);
                BrowserNavigationRequest? request;
                try
                {
                    request = string.IsNullOrWhiteSpace(line) ? null : JsonSerializer.Deserialize<BrowserNavigationRequest>(line);
                }
                catch (JsonException)
                {
                    request = null;
                }

                // A client must first write before Windows permits impersonation. The ACL blocks
                // unrelated accounts and the actual impersonated SID is checked again below.
                var sid = request is null ? null : ParentControlListener.GetAuthenticatedSid(pipe);
                var result = await EvaluateAsync(request, sid, settings, stoppingToken);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                await writer.WriteLineAsync(JsonSerializer.Serialize(result));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                BrowserPolicyLog.Failed(logger, exception);
            }
        }
    }

    private async Task<BrowserNavigationResponse> EvaluateAsync(
        BrowserNavigationRequest? request,
        string? sid,
        BrowserControlConfiguration settings,
        CancellationToken token)
    {
        if (request is null ||
            sid is null ||
            !string.Equals(sid, settings.ManagedUserSid, StringComparison.OrdinalIgnoreCase) ||
            !BrowserNavigationValidator.IsValid(request, settings.ExtensionId))
        {
            return new(false, "REJECTED_BROWSER_REQUEST");
        }

        var policy = await policies.LoadAsync(request.ProfileId, token);
        if (policy is null ||
            policy.ManagedSessionId != request.ManagedSessionId ||
            !string.Equals(policy.ManagedUserSid, sid, StringComparison.OrdinalIgnoreCase))
        {
            return new(false, "PROFILE_OR_SESSION_MISMATCH");
        }

        var rules = await webPolicies.GetRulesAsync(policy.ProfileId, token);
        var navigation = new BrowserNavigation(
            request.Provider,
            request.Host,
            request.Path,
            request.ContentType,
            request.ChannelId,
            request.ChannelHandle,
            request.TikTokCreator);
        var decision = WebPolicyEngine.Evaluate(navigation, rules);
        return new(decision.Allowed, decision.Reason, decision.MatchedRule?.DisplayLabel);
    }
}

internal static partial class BrowserPolicyLog
{
    [LoggerMessage(EventId = 2600, Level = LogLevel.Warning, Message = "Browser policy pipe disabled until browser-control.json is installed.")]
    public static partial void Disabled(ILogger logger);

    [LoggerMessage(EventId = 2601, Level = LogLevel.Warning, Message = "Browser policy pipe request failed.")]
    public static partial void Failed(ILogger logger, Exception exception);
}
