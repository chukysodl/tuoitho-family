namespace TuoiTho.Core.Policy;
public sealed record SessionWarning(string ProfileId,int SessionId,int RemainingMinutes,int? ThresholdMinutes,AccessDenyReason Reason);
public sealed class WarningDeduplicator
{
 private readonly Dictionary<int,int> lastRemaining=new();
 public IReadOnlyList<int> GetNewThresholds(SessionWarning warning,IReadOnlyList<int> thresholds)
 {
  if(warning.ThresholdMinutes is null)return [];
  lastRemaining.TryGetValue(warning.ThresholdMinutes.Value,out var prior);
  if(prior==warning.RemainingMinutes)return [];
  lastRemaining[warning.ThresholdMinutes.Value]=warning.RemainingMinutes;
  return [warning.ThresholdMinutes.Value];
 }
 public void ResetAbove(int remaining,IReadOnlyList<int> thresholds){foreach(var t in thresholds.Where(t=>remaining>t).ToArray())lastRemaining.Remove(t);}
}
public static class PolicyReasonText
{
 public static string ToDisplayText(AccessDenyReason reason)=>reason switch {AccessDenyReason.OutsideSchedule=>"OUTSIDE_SCHEDULE",AccessDenyReason.QuotaExhausted=>"QUOTA_EXHAUSTED",AccessDenyReason.ParentLock=>"PARENT_LOCK",_=>"ALLOWED"};
}