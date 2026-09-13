namespace TuoiTho.Core.Policy;
public enum ParentControlAction { GrantMinutes, EmergencyOverride, ClearOverride, SetParentLock, ClearParentLock, GetStatus, ResetM1 }
public sealed record ParentControlCommand(ParentControlAction Action,string ProfileId,int ManagedSessionId,int? Minutes=null);
public sealed record ParentActivityDiagnostics(bool SessionAgentConnected,string ActivityState,double? IdleSeconds,double? SampleAgeSeconds,int TrackedSessionId,double RecordedTodaySeconds,DateTimeOffset? LastCheckpointAtUtc,bool ExplicitLockLatched,int? WtsConnectionState,int? WtsSessionFlags);
public sealed record ParentControlStatus(string ProfileId,int ManagedSessionId,bool TestMode,int UsedMinutes,int QuotaMinutes,int GrantMinutes,int RemainingMinutes,string State,ParentActivityDiagnostics? Diagnostics=null);
public sealed record ParentControlResult(bool Accepted,string? Error,DeviceTimePolicy? Policy=null,ParentControlStatus? Status=null);