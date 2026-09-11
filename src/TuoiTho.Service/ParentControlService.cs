using TuoiTho.Core.Policy;
namespace TuoiTho.Service;
public sealed class ParentControlService(IDeviceTimePolicyStore store, TimeProvider timeProvider, PolicyChangeSignal changes)
{
 public async Task<ParentControlResult> ExecuteAsync(ParentControlCommand command,string authenticatedSid,IReadOnlySet<string> allowedParentSids,CancellationToken cancellationToken=default)
 {
  if(string.IsNullOrWhiteSpace(authenticatedSid)||(!allowedParentSids.Contains(authenticatedSid)&&!string.Equals(authenticatedSid,"S-1-5-18",StringComparison.OrdinalIgnoreCase)))return new(false,"UNAUTHORIZED");
  if(string.IsNullOrWhiteSpace(command.ProfileId)||command.ManagedSessionId<0)return new(false,"INVALID_COMMAND");
  var policy=await store.LoadAsync(command.ProfileId,cancellationToken);if(policy is null||policy.ManagedSessionId!=command.ManagedSessionId)return new(false,"PROFILE_OR_SESSION_MISMATCH");
  switch(command.Action){case ParentControlAction.GrantMinutes:if(command.Minutes is null or <=0 or >1440)return new(false,"INVALID_GRANT");await store.AddGrantAsync(policy.ProfileId,new TemporaryGrant(command.Minutes.Value,EndOfLocalDay(timeProvider.GetUtcNow())),cancellationToken);changes.Notify();return new(true,null,policy);case ParentControlAction.EmergencyOverride:policy=policy with{ParentOverride=true,ParentLock=false};break;case ParentControlAction.ClearOverride:policy=policy with{ParentOverride=false};break;case ParentControlAction.SetParentLock:policy=policy with{ParentLock=true,ParentOverride=false};break;case ParentControlAction.ClearParentLock:policy=policy with{ParentLock=false};break;default:return new(false,"UNKNOWN_ACTION");}
  await store.SaveAsync(policy,cancellationToken);changes.Notify();return new(true,null,policy);
 }
 private static DateTimeOffset EndOfLocalDay(DateTimeOffset utcNow){var local=TimeZoneInfo.ConvertTime(utcNow,TimeZoneInfo.Local);var next=local.Date.AddDays(1);return new DateTimeOffset(next,TimeZoneInfo.Local.GetUtcOffset(next)).ToUniversalTime();}
}