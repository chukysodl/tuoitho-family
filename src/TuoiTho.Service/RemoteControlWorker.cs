using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TuoiTho.Core.Policy;
using TuoiTho.Core.Remote;
using TuoiTho.Core.Time;

namespace TuoiTho.Service;

public sealed class RemoteControlWorker(
    IRemoteTransport transport,
    IRemoteCommandStateStore state,
    IRemotePolicyStore remotePolicies,
    RemoteDeviceIdentityManager identity,
    ParentControlService parent,
    IDeviceTimePolicyStore timePolicies,
    IAppPolicyStore appPolicies,
    IWebPolicyStore webPolicies,
    IClock clock,
    IOptions<WindowsTimeTrackingOptions> timeOptions,
    IOptions<RemoteControlOptions> remoteOptions,
    RemoteControlRuntimeStatusCache runtimeStatus,
    ILogger<RemoteControlWorker> logger) : BackgroundService
{
    private readonly JsonSerializerOptions json = CreateJsonOptions();
    private DateTimeOffset lastPublished = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = remoteOptions.Value;
        runtimeStatus.Configure(settings);
        if (!settings.Enabled)
        {
            RemoteWorkerLog.Disabled(logger);
            return;
        }
        try
        {
            await identity.GetOrCreateAsync(stoppingToken);
            RemoteWorkerLog.Initialized(logger);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            runtimeStatus.Failed(exception);
            RemoteWorkerLog.InitializeFailed(logger, exception);
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Clamp(settings.PollIntervalSeconds, 2, 60)));
        do
        {
            try { await RunCycleAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { runtimeStatus.Failed(exception); RemoteWorkerLog.TransportUnavailable(logger, exception); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunCycleAsync(CancellationToken token = default)
    {
        var credential = await identity.GetOrCreateAsync(token);
        runtimeStatus.IdentityReady(credential.DeviceId);
        foreach (var ack in await state.GetUnacknowledgedResultsAsync(token))
        {
            await transport.AcknowledgeAsync(credential, ack, token);
            await state.MarkAcknowledgementSentAsync(ack.CommandId, token);
        }

        var commands = await transport.ReceiveCommandsAsync(credential, token);
        runtimeStatus.CommandPollSucceeded(clock.UtcNow);
        foreach (var command in commands.Take(100))
        {
            try { await ProcessCommandAsync(credential, command, token); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                RemoteWorkerLog.CommandFailed(logger, exception, command.CommandId);
            }
        }

        if (clock.UtcNow - lastPublished >= TimeSpan.FromSeconds(Math.Clamp(remoteOptions.Value.StatusIntervalSeconds, 5, 300)))
        {
            var profile = await timePolicies.LoadAsync(timeOptions.Value.ProfileId, token);
            var status = profile is null ? null : await parent.GetRemoteStatusAsync(profile.ProfileId, token);
            if (profile is not null && status is not null)
            {
                runtimeStatus.PolicyObserved(profile.ProfileId, profile.ManagedSessionId, status.TestMode);
                var policyRevision = await remotePolicies.GetAppliedRevisionAsync(credential.DeviceId, token);
                var appDefault = await appPolicies.GetDefaultPolicyAsync(profile.ProfileId, token);
                var appRules = await appPolicies.GetRulesAsync(profile.ProfileId, token);
                var webRules = await webPolicies.GetRulesAsync(profile.ProfileId, token);
                var publishedRevision = Math.Max(clock.UtcNow.ToUnixTimeMilliseconds(), policyRevision + 1);
                var policySnapshot = new RemotePolicySnapshot(credential.DeviceId, profile.ProfileId, profile.ManagedSessionId,
                    publishedRevision, profile with { ManagedUserSid = null }, appDefault, appRules, webRules);
                var webRevision = status.Web?.PolicyRevision ?? 0;
                var remoteStatus = new RemoteDeviceStatus(credential.DeviceId, remoteOptions.Value.EffectiveDeviceName, true, clock.UtcNow,
                    status.Diagnostics?.RecordedTodaySeconds ?? status.UsedMinutes * 60d, status.RemainingSeconds,
                    status.State, status.State is "PARENT_LOCK" or "QUOTA_EXHAUSTED" or "OUTSIDE_SCHEDULE", status.TestMode,
                    publishedRevision, publishedRevision, webRevision, "HEALTHY", policySnapshot);
                await transport.PublishStatusAsync(credential, remoteStatus, token);
                lastPublished = clock.UtcNow;
                runtimeStatus.StatusPublished(clock.UtcNow);
            }
        }
    }

    private async Task ProcessCommandAsync(RemoteDeviceCredential credential, RemoteCommandEnvelope command, CancellationToken token)
    {
        var validation = RemoteCommandValidator.Validate(command, credential.DeviceId, clock.UtcNow);
        var allowedDevice = string.Equals(command.DeviceId, credential.DeviceId, StringComparison.OrdinalIgnoreCase);
        if (!allowedDevice || !Guid.TryParse(command.CommandId, out _)) return;
        if (validation.ErrorCode == "INVALID_NONCE")
        {
            await transport.AcknowledgeAsync(credential, new(command.CommandId, credential.DeviceId, RemoteCommandState.Rejected, clock.UtcNow, "INVALID_NONCE", null, null, (await CurrentStatusAsync(token))?.TestMode ?? true), token);
            return;
        }
        if (!await state.TryRecordCommandAsync(command, clock.UtcNow, token))
        {
            await transport.AcknowledgeAsync(credential, new(command.CommandId, credential.DeviceId, RemoteCommandState.Duplicate, clock.UtcNow, "DUPLICATE_COMMAND", null, null, (await CurrentStatusAsync(token))?.TestMode ?? true), token);
            return;
        }

        ParentControlResult? result = null;
        var acknowledgementState = RemoteCommandState.Rejected;
        var error = validation.ErrorCode ?? "COMMAND_REJECTED";
        var localPolicy = await timePolicies.LoadAsync(timeOptions.Value.ProfileId, token);
        if (localPolicy is null) error = "MANAGED_POLICY_NOT_FOUND";
        if (validation.Valid && localPolicy is not null)
        {
            switch (command.Kind)
            {
                case RemoteCommandKind.LockNow:
                    result = await parent.ExecuteRemoteAsync(new(ParentControlAction.SetParentLock, localPolicy.ProfileId, localPolicy.ManagedSessionId), token);
                    break;
                case RemoteCommandKind.Unlock:
                    // UNLOCK removes only the parent lock. Schedule/quota denial remains authoritative; it is not an override.
                    result = await parent.ExecuteRemoteAsync(new(ParentControlAction.ClearParentLock, localPolicy.ProfileId, localPolicy.ManagedSessionId), token);
                    break;
                case RemoteCommandKind.GrantTime:
                    var minutes = command.Payload.ValueKind == System.Text.Json.JsonValueKind.Object && command.Payload.TryGetProperty("minutes", out var value) && value.TryGetInt32(out var parsed) ? parsed : 0;
                    if (minutes is not (15 or 30 or 60)) error = "INVALID_GRANT";
                    else result = await parent.ExecuteRemoteAsync(new(ParentControlAction.GrantMinutes, localPolicy.ProfileId, localPolicy.ManagedSessionId, minutes), token);
                    break;
                case RemoteCommandKind.SyncPolicy:
                    RemotePolicySnapshot? snapshot;
                    try { snapshot = command.Payload.Deserialize<RemotePolicySnapshot>(json); }
                    catch (JsonException) { snapshot = null; }
                    var applied = snapshot is not null && snapshot.ProfileId == localPolicy.ProfileId && snapshot.ManagedSessionId == localPolicy.ManagedSessionId && await parent.ApplyRemotePolicyAsync(snapshot, credential.DeviceId, token);
                    if (applied)
                    {
                        acknowledgementState = RemoteCommandState.Applied;
                        error = string.Empty;
                    }
                    else error = "POLICY_SNAPSHOT_REJECTED";
                    break;
                default: error = "UNSUPPORTED_COMMAND"; break;
            }
            if (result is not null)
            {
                acknowledgementState = result.Accepted ? RemoteCommandState.Applied : RemoteCommandState.Rejected;
                error = result.Error;
            }
            else if (command.Kind == RemoteCommandKind.SyncPolicy && acknowledgementState == RemoteCommandState.Applied) error = string.Empty;
        }

        var currentStatus = result?.Status ?? await CurrentStatusAsync(token);
        if (validation.ErrorCode == "COMMAND_EXPIRED") acknowledgementState = RemoteCommandState.Expired;
        var acknowledgement = new RemoteCommandAcknowledgement(command.CommandId, credential.DeviceId, acknowledgementState, clock.UtcNow,
            string.IsNullOrEmpty(error) ? null : error, currentStatus?.State, currentStatus?.RemainingSeconds, currentStatus?.TestMode ?? true);
        await state.CompleteCommandAsync(acknowledgement, token);
        await transport.AcknowledgeAsync(credential, acknowledgement, token);
        await state.MarkAcknowledgementSentAsync(command.CommandId, token);
    }

    private async Task<ParentControlStatus?> CurrentStatusAsync(CancellationToken token)
        => await parent.GetRemoteStatusAsync(timeOptions.Value.ProfileId, token);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var result = new JsonSerializerOptions(JsonSerializerDefaults.Web) { MaxDepth = 32 };
        result.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return result;
    }
}

internal static partial class RemoteWorkerLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Remote control is disabled; local time/app/web enforcement remains independent.")]
    public static partial void Disabled(ILogger logger);
    [LoggerMessage(Level = LogLevel.Information, Message = "Remote control transport initialized for this device.")]
    public static partial void Initialized(ILogger logger);
    [LoggerMessage(Level = LogLevel.Error, Message = "Remote control initialization failed; local policy remains active.")]
    public static partial void InitializeFailed(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Remote transport unavailable; local enforcement continues and will retry.")]
    public static partial void TransportUnavailable(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Error, Message = "Remote command processing failed safely. CommandId={CommandId}")]
    public static partial void CommandFailed(ILogger logger, Exception exception, string commandId);
}
