namespace TuoiTho.Core.Policy;
public sealed record SessionWarning(string ProfileId,int SessionId,int RemainingMinutes,int? ThresholdMinutes,AccessDenyReason Reason);
public sealed class WarningDeduplicator
{
 private int? previousRemaining;
 private readonly HashSet<int> fired=[];
 public IReadOnlyList<int> GetNewThresholds(int remaining,IReadOnlyList<int> thresholds)
 {
  if(previousRemaining is null){previousRemaining=remaining;return [];}
  var prior=previousRemaining.Value; previousRemaining=remaining;
  foreach(var t in thresholds.Where(t=>remaining>t))fired.Remove(t);
  return thresholds.Where(t=>prior>t&&remaining<=t&&!fired.Contains(t)).OrderByDescending(t=>t).Select(t=>{fired.Add(t);return t;}).ToArray();
 }
}
public static class PolicyReasonText { public static string ToDisplayText(AccessDenyReason reason)=>reason switch {AccessDenyReason.OutsideSchedule=>"OUTSIDE_SCHEDULE",AccessDenyReason.QuotaExhausted=>"QUOTA_EXHAUSTED",AccessDenyReason.ParentLock=>"PARENT_LOCK",_=>"ALLOWED"}; }