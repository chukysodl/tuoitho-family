using System.IO.Pipes;
using System.Text.Json;
using TuoiTho.Core.Policy;

namespace TuoiTho.Service;

public sealed class BrowserPolicyListener(
    IDeviceTimePolicyStore policies,
    IWebPolicyStore webPolicies,
    BrowserRuntimeStatusCache runtimeStatus,
    ILogger<BrowserPolicyListener> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            BrowserControlConfiguration settings;
            try
            {
                runtimeStatus.SetBrowserPolicyReadiness("STARTING");
                settings = BrowserControlConfiguration.Load();
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                var category = CategorizeConfigurationFailure(exception);
                runtimeStatus.SetBrowserPolicyReadiness("DEGRADED", category);
                BrowserPolicyLog.Degraded(logger, category, exception);
                await DelayForRetryAsync(stoppingToken);
                continue;
            }

            NamedPipeServerStream pipe;
            try
            {
                pipe = ParentControlPipeSecurity.CreateServer("TuoiTho.BrowserPolicy", [settings.ManagedUserSid]);
                runtimeStatus.SetBrowserPolicyReadiness("READY");
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                runtimeStatus.SetBrowserPolicyReadiness("DEGRADED", "PIPE_CREATE_FAILED");
                BrowserPolicyLog.Degraded(logger, "PIPE_CREATE_FAILED", exception);
                await DelayForRetryAsync(stoppingToken);
                continue;
            }

            using (pipe)
            {
                try
                {
                    await pipe.WaitForConnectionAsync(stoppingToken);
                    using var reader = new StreamReader(pipe, leaveOpen: true);
                    var line = await reader.ReadLineAsync(stoppingToken);
                    BrowserNavigationRequest? request;
                    try { request = string.IsNullOrWhiteSpace(line) ? null : JsonSerializer.Deserialize<BrowserNavigationRequest>(line); }
                    catch (JsonException) { request = null; }

                    // A client must write first before Windows permits impersonation. The ACL plus the
                    // actual impersonated SID check prevent an extension payload from choosing an identity.
                    var sid = request is null ? null : ParentControlListener.GetAuthenticatedSid(pipe);
                    var result = await EvaluateAsync(request, sid, settings, stoppingToken);
                    if (request is { IsDiagnosticProbe: false } && result.Reason is not "REJECTED_BROWSER_REQUEST" and not "PROFILE_OR_SESSION_MISMATCH") runtimeStatus.Record(result, settings.TestMode && request.ContentType == BrowserContentType.ShortForm ? request.OwnerState : null, settings.TestMode && request.ContentType == BrowserContentType.ShortForm ? request.ShortContainer : null, settings.TestMode && request.ContentType == BrowserContentType.ShortForm ? request.OwnerCandidateCount : null, settings.TestMode && request.ContentType == BrowserContentType.ShortForm ? request.OwnerSource : null);
                    using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                    await writer.WriteLineAsync(JsonSerializer.Serialize(result));
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
                catch (Exception exception)
                {
                    // A malformed/disconnected client must never take down the listener.
                    BrowserPolicyLog.RequestFailed(logger, exception);
                }
            }
        }
    }

    private static async Task DelayForRetryAsync(CancellationToken token)
    {
        try { await Task.Delay(RetryDelay, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private static string CategorizeConfigurationFailure(Exception exception) => exception switch
    {
        FileNotFoundException => "CONFIG_MISSING",
        UnauthorizedAccessException => "CONFIG_INACCESSIBLE",
        _ => "CONFIG_INVALID"
    };

    private async Task<BrowserNavigationResponse> EvaluateAsync(BrowserNavigationRequest? request, string? sid, BrowserControlConfiguration settings, CancellationToken token)
    {
        if (request is null || sid is null || !string.Equals(sid, settings.ManagedUserSid, StringComparison.OrdinalIgnoreCase) || !BrowserNavigationValidator.IsValid(request, settings.ExtensionId)) return new(false, "REJECTED_BROWSER_REQUEST");
        var policy = await policies.LoadAsync(request.ProfileId, token);
        if (policy is null || policy.ManagedSessionId != request.ManagedSessionId || !string.Equals(policy.ManagedUserSid, sid, StringComparison.OrdinalIgnoreCase)) return new(false, "PROFILE_OR_SESSION_MISMATCH");
        if (request.IsPolicySync)
        {
            var snapshot = await webPolicies.GetSnapshotAsync(policy.ProfileId, token);
            return new(true, "POLICY_SNAPSHOT", PolicyRevision: snapshot.Revision, CustomRules: snapshot.Rules.Where(rule => rule.Provider == BrowserProvider.GenericWeb).ToArray());
        }        var rules = await webPolicies.GetRulesAsync(policy.ProfileId, token);
        var navigation = new BrowserNavigation(request.Provider, request.Host, request.Path, request.ContentType, request.ChannelId, request.ChannelHandle, request.TikTokCreator);
        var decision = WebPolicyEngine.Evaluate(navigation, rules);
        return new(decision.Allowed, decision.Reason, decision.MatchedRule?.DisplayLabel);
    }
}

internal static partial class BrowserPolicyLog
{
    [LoggerMessage(EventId = 2602, Level = LogLevel.Warning, Message = "Browser policy listener is degraded: {Category}.")]
    public static partial void Degraded(ILogger logger, string category, Exception exception);
    [LoggerMessage(EventId = 2603, Level = LogLevel.Warning, Message = "Browser policy pipe request failed.")]
    public static partial void RequestFailed(ILogger logger, Exception exception);
}
