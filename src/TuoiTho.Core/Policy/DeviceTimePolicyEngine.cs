using TuoiTho.Core.Time;
namespace TuoiTho.Core.Policy;
public sealed class DeviceTimePolicyEngine(IClock clock)
{
    public PolicyDecision Evaluate(DeviceTimePolicy policy, TimeSpan usedToday, IEnumerable<TemporaryGrant> grants)
    {
        ArgumentNullException.ThrowIfNull(policy); ArgumentNullException.ThrowIfNull(grants);
        var now = TimeZoneInfo.ConvertTime(clock.UtcNow, clock.LocalTimeZone);
        if (policy.ParentLock) return Deny(AccessDenyReason.ParentLock);
        if (policy.ParentOverride) return new(true, AccessDenyReason.None, int.MaxValue, []);
        if (!policy.Windows.Any(window => window.Day == now.DayOfWeek && window.Contains(TimeOnly.FromDateTime(now.DateTime)))) return Deny(AccessDenyReason.OutsideSchedule);
        var grantMinutes = grants.Where(grant => grant.ExpiresAtUtc > clock.UtcNow).Sum(grant => grant.Minutes);
        var remaining = Math.Max(0, policy.DailyQuotaMinutes + grantMinutes - (int)Math.Ceiling(usedToday.TotalMinutes));
        if (remaining == 0) return Deny(AccessDenyReason.QuotaExhausted);
        return new(true, AccessDenyReason.None, remaining, policy.WarningThresholdMinutes.Where(threshold => threshold >= remaining).OrderByDescending(x => x).ToArray());
    }
    private static PolicyDecision Deny(AccessDenyReason reason) => new(false, reason, 0, []);
}