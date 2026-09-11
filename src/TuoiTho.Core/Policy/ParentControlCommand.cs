namespace TuoiTho.Core.Policy;

public enum ParentControlAction { GrantMinutes, EmergencyOverride, ClearOverride, SetParentLock, ClearParentLock }
public sealed record ParentControlCommand(ParentControlAction Action, string ProfileId, int ManagedSessionId, int? Minutes = null);
public sealed record ParentControlResult(bool Accepted, string? Error, DeviceTimePolicy? Policy = null);