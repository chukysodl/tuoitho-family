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
                var protectedToken = protector.Protect(tokenBytes);
                try
                {
                    await state.SaveDeviceAsync(new(deviceId, options.Value.EffectiveDeviceName, protectedToken, clock.UtcNow), cancellationToken);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(tokenBytes);
                    CryptographicOperations.ZeroMemory(protectedToken);
                }
                cached = new(deviceId, token);
            }
            else
            {
                var tokenBytes = protector.Unprotect(stored.ProtectedCredential);
                try { cached = new(stored.DeviceId, Encoding.UTF8.GetString(tokenBytes)); }
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
