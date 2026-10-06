using TuoiTho.Core.Time;

namespace TuoiTho.Core.Policy;

public sealed class DeviceTimePolicyEngine(IClock clock)
{
    public PolicyDecision Evaluate(
        DeviceTimePolicy policy,
        TimeSpan usedToday,
        IEnumerable<TemporaryGrant> grants,
        TimeSpan? elapsedSinceBoot = null)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(grants);

        var now = TimeZoneInfo.ConvertTime(clock.UtcNow, clock.LocalTimeZone);

        if (policy.ParentLock)
            return Deny(AccessDenyReason.ParentLock);

        if (policy.ParentOverride)
            return new(true, AccessDenyReason.None, int.MaxValue, []);

        var localTime = TimeOnly.FromDateTime(now.DateTime);

        if (policy.BlockedWindows.Any(window => window.Contains(localTime)))
            return Deny(AccessDenyReason.BlockedTime);

        if (policy.StartupLimitMinutes > 0 &&
            elapsedSinceBoot is { } bootElapsed &&
            bootElapsed >= TimeSpan.FromMinutes(policy.StartupLimitMinutes))
        {
            return Deny(AccessDenyReason.StartupLimitExceeded);
        }

        if (!policy.Windows.Any(window =>
                window.Day == now.DayOfWeek &&
                window.Contains(localTime)))
        {
            return Deny(AccessDenyReason.OutsideSchedule);
        }

        var grantMinutes = grants
            .Where(grant => grant.ExpiresAtUtc > clock.UtcNow)
            .Sum(grant => grant.Minutes);

        var remainingDuration =
            TimeSpan.FromMinutes(policy.DailyQuotaMinutes + grantMinutes) - usedToday;

        if (remainingDuration <= TimeSpan.Zero)
            return Deny(AccessDenyReason.QuotaExhausted);

        var remaining = (int)Math.Ceiling(remainingDuration.TotalMinutes);
        var warnings = policy.WarningThresholdMinutes
            .Where(threshold => threshold >= remaining)
            .OrderByDescending(threshold => threshold)
            .ToArray();

        if (policy.StartupLimitMinutes > 0 && elapsedSinceBoot is { } elapsed)
        {
            var bootRemaining = Math.Max(
                0,
                (int)Math.Ceiling(
                    (TimeSpan.FromMinutes(policy.StartupLimitMinutes) - elapsed).TotalMinutes));

            remaining = Math.Min(remaining, bootRemaining);
            warnings = policy.WarningThresholdMinutes
                .Where(threshold => threshold >= remaining)
                .OrderByDescending(threshold => threshold)
                .ToArray();
        }

        return new(true, AccessDenyReason.None, remaining, warnings);
    }

    private static PolicyDecision Deny(AccessDenyReason reason) =>
        new(false, reason, 0, []);
}
