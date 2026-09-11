namespace TuoiTho.Core.Time;

public enum TimeEngineApplyResult
{
    Applied,
    IgnoredDuplicate,
    IgnoredOutOfOrder,
    IgnoredDifferentSession
}