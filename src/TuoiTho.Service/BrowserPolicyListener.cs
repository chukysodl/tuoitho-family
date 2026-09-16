using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TuoiTho.Core.Policy;

namespace TuoiTho.Service;

public sealed class BrowserPolicyListener(
    IDeviceTimePolicyStore policies,
    IWebPolicyStore webPolicies,
    IOptions<BrowserControlOptions> options,
    ILogger<BrowserPolicyListener> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ManagedUserSid) || string.IsNullOrWhiteSpace(settings.AllowedExtensionId))
        {
            BrowserPolicyLog.Disabled(logger);
            return;
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var pipe = ParentControlPipeSecurity.CreateServer(settings.PipeName, [settings.ManagedUserSid]);
                await pipe.WaitForConnectionAsync(stoppingToken);
                using var reader = new StreamReader(pipe, leaveOpen: true);
                var line = await reader.ReadLineAsync(stoppingToken);
                BrowserNavigationRequest? request;
                try { request = string.IsNullOrWhiteSpace(line) ? null : JsonSerializer.Deserialize<BrowserNavigationRequest>(line); }
                catch (JsonException) { request = null; }
                var sid = request is null ? null : ParentControlListener.GetAuthenticatedSid(pipe);
                var result = await EvaluateAsync(request, sid, settings, stoppingToken);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                await writer.WriteLineAsync(JsonSerializer.Serialize(result));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception) { BrowserPolicyLog.Failed(logger, exception); }
        }
    }

    private async Task<BrowserNavigationResponse> EvaluateAsync(BrowserNavigationRequest? request, string? sid, BrowserControlOptions settings, CancellationToken token)
    {
        if (request is null || sid is null || !string.Equals(sid, settings.ManagedUserSid, StringComparison.OrdinalIgnoreCase) || !BrowserNavigationValidator.IsValid(request, settings.AllowedExtensionId)) return new(false, "REJECTED_BROWSER_REQUEST");
        var policy = await policies.LoadAsync(request.ProfileId, token);
        if (policy is null || policy.ManagedSessionId != request.ManagedSessionId || !string.Equals(policy.ManagedUserSid, sid, StringComparison.OrdinalIgnoreCase)) return new(false, "PROFILE_OR_SESSION_MISMATCH");
        var decision = WebPolicyEngine.Evaluate(new BrowserNavigation(request.Provider, request.Host, request.Path, request.ContentType, request.ChannelId, request.ChannelHandle, request.TikTokCreator), await webPolicies.GetRulesAsync(policy.ProfileId, token));
        return new(decision.Allowed, decision.Reason, decision.MatchedRule?.DisplayLabel);
    }
}

internal static partial class BrowserPolicyLog
{
    [LoggerMessage(EventId = 2600, Level = LogLevel.Warning, Message = "Browser policy pipe disabled until managed SID and extension ID are configured.")]
    public static partial void Disabled(ILogger logger);
    [LoggerMessage(EventId = 2601, Level = LogLevel.Warning, Message = "Browser policy pipe request failed.")]
    public static partial void Failed(ILogger logger, Exception exception);
}