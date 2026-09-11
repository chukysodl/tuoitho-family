using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using TuoiTho.Core.Policy;

namespace TuoiTho.Service;

public interface IManagedSessionNativeApi
{
    Task<bool> IsManagedChildSessionAsync(int sessionId, string managedUserSid, CancellationToken cancellationToken = default);
    Task DisconnectSessionAsync(int sessionId, CancellationToken cancellationToken = default);
}

public sealed record SessionEnforcementResult(bool WouldLock, bool RealLockIssued, string Outcome);

public sealed class SafeChildSessionEnforcer(IManagedSessionNativeApi nativeApi, ILogger<SafeChildSessionEnforcer> logger)
{
    private readonly HashSet<string> enforced = [];

    public async Task<SessionEnforcementResult> EnforceAsync(DeviceTimePolicy policy, int observedSessionId, PolicyDecision decision, CancellationToken cancellationToken = default)
    {
        if (decision.Allowed || observedSessionId != policy.ManagedSessionId)
        {
            return new(false, false, "NOT_TARGETED");
        }

        var key = $"{policy.ProfileId}:{policy.ManagedSessionId}:{decision.Reason}";
        if (!enforced.Add(key))
        {
            return new(true, false, "ALREADY_ENFORCED");
        }

        if (policy.TestMode)
        {
            SessionEnforcementLog.Simulated(logger, policy.ProfileId, policy.ManagedSessionId, decision.Reason);
            return new(true, false, "SIMULATED_LOCK");
        }

        if (string.IsNullOrWhiteSpace(policy.ManagedUserSid) || !await nativeApi.IsManagedChildSessionAsync(policy.ManagedSessionId, policy.ManagedUserSid, cancellationToken))
        {
            SessionEnforcementLog.Rejected(logger, policy.ProfileId, policy.ManagedSessionId);
            return new(false, false, "SESSION_IDENTITY_REJECTED");
        }

        await nativeApi.DisconnectSessionAsync(policy.ManagedSessionId, cancellationToken);
        SessionEnforcementLog.Real(logger, policy.ProfileId, policy.ManagedSessionId, decision.Reason);
        return new(true, true, "REAL_SESSION_LOCK");
    }
}

public sealed class WindowsManagedSessionNativeApi : IManagedSessionNativeApi
{
    public Task<bool> IsManagedChildSessionAsync(int sessionId, string managedUserSid, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!WTSQueryUserToken((uint)sessionId, out var token)) return Task.FromResult(false);
        try { using var identity = new WindowsIdentity(token); return Task.FromResult(string.Equals(identity.User?.Value, managedUserSid, StringComparison.OrdinalIgnoreCase)); }
        finally { CloseHandle(token); }
    }

    public Task DisconnectSessionAsync(int sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!WTSDisconnectSession(IntPtr.Zero, (uint)sessionId, false)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return Task.CompletedTask;
    }

    [DllImport("wtsapi32.dll", SetLastError = true)] private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);
    [DllImport("wtsapi32.dll", SetLastError = true)] private static extern bool WTSDisconnectSession(IntPtr server, uint sessionId, bool wait);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
}

internal static partial class SessionEnforcementLog
{
    [LoggerMessage(EventId = 2200, Level = LogLevel.Warning, Message = "SIMULATED_LOCK profile {ProfileId} session {SessionId} reason {Reason}")]
    public static partial void Simulated(ILogger logger, string profileId, int sessionId, AccessDenyReason reason);
    [LoggerMessage(EventId = 2201, Level = LogLevel.Warning, Message = "REAL_SESSION_LOCK profile {ProfileId} session {SessionId} reason {Reason}")]
    public static partial void Real(ILogger logger, string profileId, int sessionId, AccessDenyReason reason);
    [LoggerMessage(EventId = 2202, Level = LogLevel.Warning, Message = "Managed session identity rejected for profile {ProfileId} session {SessionId}")]
    public static partial void Rejected(ILogger logger, string profileId, int sessionId);
}