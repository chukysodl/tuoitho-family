namespace TuoiTho.Core.Policy;
public enum ParentControlAction { GrantMinutes, EmergencyOverride, ClearOverride, SetParentLock, ClearParentLock, GetStatus }
public sealed record ParentControlCommand(ParentControlAction Action,string ProfileId,int ManagedSessionId,int? Minutes=null);
public sealed record ParentControlStatus(string ProfileId,int ManagedSessionId,bool TestMode,int UsedMinutes,int QuotaMinutes,int GrantMinutes,int RemainingMinutes,string State);
public sealed record ParentControlResult(bool Accepted,string? Error,DeviceTimePolicy? Policy=null,ParentControlStatus? Status=null);