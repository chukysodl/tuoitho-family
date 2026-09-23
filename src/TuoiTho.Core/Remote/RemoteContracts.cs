using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TuoiTho.Core.Policy;

namespace TuoiTho.Core.Remote;

public enum RemoteCommandKind { LockNow, Unlock, GrantTime, SyncPolicy }
public enum RemoteCommandState { Accepted, Rejected, Expired, Duplicate, Applied }

/// <summary>Provider-neutral command envelope. Payload is validated by the device before any local action.</summary>
public sealed record RemoteCommandEnvelope(
    string CommandId,
    string DeviceId,
    string Nonce,
    DateTimeOffset TimestampUtc,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    RemoteCommandKind Kind,
    JsonElement Payload);

public sealed record RemoteCommandAcknowledgement(
    string CommandId,
    string DeviceId,
    RemoteCommandState State,
    DateTimeOffset AcknowledgedAtUtc,
    string? ErrorCode,
    string? PolicyState,
    double? RemainingSeconds,
    bool TestMode);

/// <summary>Only management telemetry is allowed here; never add browsing, app-use, or page-content history.</summary>
public sealed record RemoteDeviceStatus(
    string DeviceId,
    string DeviceName,
    bool Online,
    DateTimeOffset LastSeenAtUtc,
    double UsedSecondsToday,
    double RemainingSeconds,
    string PolicyState,
    bool Locked,
    bool TestMode,
    long TimePolicyRevision,
    long AppPolicyRevision,
    long WebPolicyRevision,
    string ServiceHealth,
    RemotePolicySnapshot? Policy = null);

public sealed record RemotePairingRegistration(
    string DeviceId,
    string DeviceName,
    string PairCodeSha256,
    string CredentialSha256,
    DateTimeOffset ExpiresAtUtc);

public sealed record RemoteDeviceCredential(string DeviceId, string BearerToken);

/// <summary>Secret-free, local-only status used by the Parent preflight. Never includes a URL or credential.</summary>
public sealed record RemoteControlRuntimeDiagnostics(
    bool Enabled,
    bool ConfigurationValid,
    bool DeviceIdentityReady,
    string? DeviceId,
    DateTimeOffset? LastCommandPollAtUtc,
    DateTimeOffset? LastStatusPublishedAtUtc,
    string? LastErrorCode,
    string? ProfileId,
    int? ManagedSessionId,
    bool? TestMode);

public sealed record RemotePolicySnapshot(
    string DeviceId,
    string ProfileId,
    int ManagedSessionId,
    long Revision,
    DeviceTimePolicy TimePolicy,
    DefaultAppPolicy DefaultAppPolicy,
    IReadOnlyList<AppRule> AppRules,
    IReadOnlyList<WebRule> WebRules);

public sealed record RemoteCommandReceipt(string CommandId, string DeviceId, string Nonce, DateTimeOffset ReceivedAtUtc, RemoteCommandAcknowledgement? Acknowledgement);
public sealed record RemoteStoredDevice(string DeviceId, string DeviceName, byte[] ProtectedCredential, DateTimeOffset CreatedAtUtc);
public sealed record PendingRemotePairing(string DeviceId, string CodeSha256, DateTimeOffset ExpiresAtUtc, bool Consumed);

public sealed record RemoteCommandValidation(bool Valid, string? ErrorCode)
{
    public static RemoteCommandValidation Ok { get; } = new(true, null);
    public static RemoteCommandValidation Reject(string code) => new(false, code);
}

public static class RemoteCommandValidator
{
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(2);

    public static RemoteCommandValidation Validate(RemoteCommandEnvelope? command, string expectedDeviceId, DateTimeOffset now)
    {
        if (command is null || !Guid.TryParse(command.CommandId, out _) || !Guid.TryParse(command.DeviceId, out _) || !string.Equals(command.DeviceId, expectedDeviceId, StringComparison.OrdinalIgnoreCase)) return RemoteCommandValidation.Reject("WRONG_DEVICE");
        if (string.IsNullOrWhiteSpace(command.Nonce) || command.Nonce.Length is < 16 or > 128) return RemoteCommandValidation.Reject("INVALID_NONCE");
        if (command.TimestampUtc.Offset != TimeSpan.Zero || command.IssuedAtUtc.Offset != TimeSpan.Zero || command.ExpiresAtUtc.Offset != TimeSpan.Zero) return RemoteCommandValidation.Reject("INVALID_TIMESTAMP");
        if (command.IssuedAtUtc > now + AllowedClockSkew || command.TimestampUtc > now + AllowedClockSkew) return RemoteCommandValidation.Reject("FUTURE_COMMAND");
        if (command.ExpiresAtUtc <= now || command.ExpiresAtUtc <= command.IssuedAtUtc) return RemoteCommandValidation.Reject("COMMAND_EXPIRED");
        if (command.ExpiresAtUtc - command.IssuedAtUtc > MaximumLifetime) return RemoteCommandValidation.Reject("COMMAND_TTL_TOO_LONG");
        if (!Enum.IsDefined(command.Kind) || command.Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return RemoteCommandValidation.Reject("MALFORMED_COMMAND");
        return RemoteCommandValidation.Ok;
    }
}

public static class RemotePairingCode
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string Create(int randomBytes = 10)
    {
        if (randomBytes is < 8 or > 32) throw new ArgumentOutOfRangeException(nameof(randomBytes));
        var bytes = RandomNumberGenerator.GetBytes(randomBytes);
        var output = new StringBuilder((bytes.Length * 8 + 4) / 5);
        var accumulator = 0;
        var bits = 0;
        foreach (var value in bytes)
        {
            accumulator = (accumulator << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                output.Append(Alphabet[(accumulator >> bits) & 31]);
            }
        }
        if (bits > 0) output.Append(Alphabet[(accumulator << (5 - bits)) & 31]);
        CryptographicOperations.ZeroMemory(bytes);
        return string.Join('-', Enumerable.Range(0, (output.Length + 3) / 4).Select(i => output.ToString().Substring(i * 4, Math.Min(4, output.Length - i * 4))));
    }

    public static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant()))).ToLowerInvariant();
    public static string HashCredential(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public interface IRemoteTransport
{
    Task RegisterPairingAsync(RemotePairingRegistration pairing, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RemoteCommandEnvelope>> ReceiveCommandsAsync(RemoteDeviceCredential credential, CancellationToken cancellationToken = default);
    Task PublishStatusAsync(RemoteDeviceCredential credential, RemoteDeviceStatus status, CancellationToken cancellationToken = default);
    Task AcknowledgeAsync(RemoteDeviceCredential credential, RemoteCommandAcknowledgement acknowledgement, CancellationToken cancellationToken = default);
}

public interface IRemoteCommandStateStore
{
    Task<RemoteStoredDevice?> LoadDeviceAsync(CancellationToken cancellationToken = default);
    Task SaveDeviceAsync(RemoteStoredDevice device, CancellationToken cancellationToken = default);
    Task SavePendingPairingAsync(PendingRemotePairing pairing, CancellationToken cancellationToken = default);
    Task<bool> TryConsumePairingAsync(string codeSha256, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
    Task<bool> TryRecordCommandAsync(RemoteCommandEnvelope command, DateTimeOffset receivedAtUtc, CancellationToken cancellationToken = default);
    Task CompleteCommandAsync(RemoteCommandAcknowledgement acknowledgement, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RemoteCommandAcknowledgement>> GetUnacknowledgedResultsAsync(CancellationToken cancellationToken = default);
    Task MarkAcknowledgementSentAsync(string commandId, CancellationToken cancellationToken = default);
}

public interface IRemotePolicyStore
{
    Task<long> GetAppliedRevisionAsync(string deviceId, CancellationToken cancellationToken = default);
    Task<bool> ApplyAsync(RemotePolicySnapshot snapshot, CancellationToken cancellationToken = default);
}

public interface IDeviceCredentialProtector
{
    byte[] Protect(ReadOnlySpan<byte> secret);
    byte[] Unprotect(ReadOnlySpan<byte> protectedSecret);
}
