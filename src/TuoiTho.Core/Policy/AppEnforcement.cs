namespace TuoiTho.Core.Policy;

public enum AppEnforcementMode
{
    Simulation,
    ExplicitBlockOnly,
    AllowlistProduction
}

public enum AppEnforcementAuditAction
{
    EnforcementOn,
    EnforcementOff,
    RealBlock,
    GracefulClose,
    Terminate,
    SkippedSafe
}

public sealed record AppEnforcementAuditEvent(
    DateTimeOffset TimestampUtc,
    string ProfileId,
    int SessionId,
    string ExecutablePath,
    string Decision,
    AppEnforcementAuditAction Action);

public sealed record ParentAppEnforcementStatus(
    AppEnforcementMode Mode,
    bool Armed,
    AppEnforcementAuditEvent? LastEvent = null,
    DateTimeOffset? LeaseExpiresAtUtc = null,
    int? LeaseRemainingSeconds = null);