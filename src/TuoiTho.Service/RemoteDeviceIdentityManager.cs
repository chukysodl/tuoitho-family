using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TuoiTho.Core.Remote;
using TuoiTho.Core.Time;

namespace TuoiTho.Service;

public sealed class RemoteDeviceIdentityManager(
    IRemoteCommandStateStore state,
    IDeviceCredentialProtector protector,
    IRemoteTransport transport,
    IClock clock,
    IOptions<RemoteControlOptions> options) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private RemoteDeviceCredential? cached;

    public async Task<RemoteDeviceCredential> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        if (cached is not null) return cached;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cached is not null) return cached;
            var stored = await state.LoadDeviceAsync(cancellationToken);
            if (stored is null)
            {
                var deviceId = Guid.NewGuid().ToString("D");
                var tokenBytes = RandomNumberGenerator.GetBytes(32);
                var token = Convert.ToBase64String(tokenBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                var tokenUtf8 = Encoding.UTF8.GetBytes(token);
                var protectedToken = protector.Protect(tokenUtf8);
                try
                {
                    await state.SaveDeviceAsync(new(deviceId, options.Value.EffectiveDeviceName, protectedToken, clock.UtcNow), cancellationToken);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(tokenBytes);
                    CryptographicOperations.ZeroMemory(tokenUtf8);
                    CryptographicOperations.ZeroMemory(protectedToken);
                }
                cached = new(deviceId, token);
            }
            else
            {
                var tokenBytes = protector.Unprotect(stored.ProtectedCredential);
                try
                {
                    // New records persist the ASCII Base64URL token itself. Legacy records stored
                    // the original 32 random bytes, while the server hash was computed from the
                    // Base64URL representation. Reconstruct that original token exactly.
                    var asciiToken = tokenBytes.Length == 43 && tokenBytes.All(static value =>
                        value is >= (byte)'0' and <= (byte)'9' or >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z' or (byte)'-' or (byte)'_');
                    var restored = asciiToken
                        ? Encoding.ASCII.GetString(tokenBytes)
                        : Convert.ToBase64String(tokenBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

                    // Re-protect the canonical ASCII token using the current protector. This migrates
                    // legacy per-user DPAPI records to the machine-scoped format after one successful
                    // read under the original Windows security context.
                    var canonicalBytes = Encoding.ASCII.GetBytes(restored);
                    var reprotected = protector.Protect(canonicalBytes);
                    try
                    {
                        await state.SaveDeviceAsync(new(stored.DeviceId, stored.DeviceName, reprotected, stored.CreatedAtUtc), cancellationToken);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(canonicalBytes);
                        CryptographicOperations.ZeroMemory(reprotected);
                    }
                    cached = new(stored.DeviceId, restored);
                }
                finally { CryptographicOperations.ZeroMemory(tokenBytes); }
            }
            return cached;
        }
        finally { gate.Release(); }
    }

    public async Task<string> CreatePairingCodeAsync(CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled) throw new InvalidOperationException("Remote control is not configured.");
        var credential = await GetOrCreateAsync(cancellationToken);
        var code = RemotePairingCode.Create();
        var codeHash = RemotePairingCode.Sha256(code);
        var expires = clock.UtcNow.AddMinutes(5);
        await state.SavePendingPairingAsync(new(credential.DeviceId, codeHash, expires, false), cancellationToken);
        await transport.RegisterPairingAsync(new(credential.DeviceId, options.Value.EffectiveDeviceName, codeHash,
            RemotePairingCode.HashCredential(credential.BearerToken), expires), cancellationToken);
        return code;
    }

    public void Dispose() => gate.Dispose();
}
