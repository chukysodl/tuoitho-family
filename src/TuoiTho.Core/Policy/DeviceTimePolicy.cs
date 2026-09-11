namespace TuoiTho.Core.Policy;

public enum AccessDenyReason { None, OutsideSchedule, QuotaExhausted, ParentLock }
public sealed record AllowedUsageWindow(DayOfWeek Day, TimeOnly Start, TimeOnly End)
{
    public bool Contains(TimeOnly value) => Start <= End ? value >= Start && value < End : value >= Start || value < End;
}
public sealed record TemporaryGrant(int Minutes, DateTimeOffset ExpiresAtUtc);
public sealed record DeviceTimePolicy(
    string ProfileId, int ManagedSessionId, int DailyQuotaMinutes, IReadOnlyList<AllowedUsageWindow> Windows,
    IReadOnlyList<int> WarningThresholdMinutes, bool ParentLock, bool ParentOverride, bool TestMode)
{
    public string? ManagedUserSid { get; init; }
    public static readonly int[] DefaultWarnings = [15, 5, 1];
}
public sealed record PolicyDecision(bool Allowed, AccessDenyReason Reason, int RemainingMinutes, IReadOnlyList<int> Warnings);