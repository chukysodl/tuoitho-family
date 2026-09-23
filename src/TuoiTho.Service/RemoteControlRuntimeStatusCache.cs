using System.Net;
using System.Security.Cryptography;
using TuoiTho.Core.Remote;

namespace TuoiTho.Service;

/// <summary>Ephemeral, credential-free diagnostics for the local M5 preflight and Parent UI.</summary>
public sealed class RemoteControlRuntimeStatusCache
{
    private readonly object gate = new();
    private RemoteControlRuntimeDiagnostics current = new(false, false, false, null, null, null, null, null, null, null);

    public RemoteControlRuntimeDiagnostics Snapshot()
    {
        lock (gate) return current;
    }

    public void Configure(RemoteControlOptions options)
    {
        var uriValid = Uri.TryCreate(options.SupabaseUrl, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
        var keyValid = SupabasePublicKeyValidator.IsValid(options.SupabaseAnonKey);
        lock (gate) current = current with { Enabled = options.Enabled, ConfigurationValid = options.Enabled && uriValid && keyValid,
            LastErrorCode = options.Enabled && (!uriValid || !keyValid) ? "REMOTE_CONFIGURATION_INVALID" : null };
    }

    public void IdentityReady(string deviceId)
    {
        lock (gate) current = current with { DeviceIdentityReady = true, DeviceId = deviceId, LastErrorCode = null };
    }

    public void CommandPollSucceeded(DateTimeOffset atUtc)
    {
        lock (gate) current = current with { LastCommandPollAtUtc = atUtc, LastErrorCode = null };
    }

    public void StatusPublished(DateTimeOffset atUtc)
    {
        lock (gate) current = current with { LastStatusPublishedAtUtc = atUtc, LastErrorCode = null };
    }

    public void PolicyObserved(string profileId, int sessionId, bool testMode)
    {
        lock (gate) current = current with { ProfileId = profileId, ManagedSessionId = sessionId, TestMode = testMode };
    }

    public void Failed(Exception exception)
    {
        var code = exception switch
        {
            HttpRequestException { StatusCode: { } status } => $"REMOTE_HTTP_{(int)status}",
            HttpRequestException => "REMOTE_NETWORK_UNAVAILABLE",
            CryptographicException => "DEVICE_IDENTITY_UNAVAILABLE",
            InvalidOperationException => "REMOTE_CONFIGURATION_INVALID",
            _ => "REMOTE_OPERATION_FAILED"
        };
        lock (gate) current = current with { LastErrorCode = code };
    }
}
