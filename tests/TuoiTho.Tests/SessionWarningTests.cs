using TuoiTho.Core.Policy;
namespace TuoiTho.Tests;
public sealed class SessionWarningTests
{
 [Fact] public void ThresholdsWarnOnceAndResetAfterGrant(){var d=new WarningDeduplicator();var t=DeviceTimePolicy.DefaultWarnings;Assert.Equal([15],d.GetNewThresholds(new("child",7,15,15,AccessDenyReason.None),t));Assert.Empty(d.GetNewThresholds(new("child",7,15,15,AccessDenyReason.None),t));d.ResetAbove(20,t);Assert.Equal([15],d.GetNewThresholds(new("child",7,15,15,AccessDenyReason.None),t));}
 [Fact] public void MultipleThresholdsArePredictable(){var d=new WarningDeduplicator();var t=DeviceTimePolicy.DefaultWarnings;Assert.Equal([15],d.GetNewThresholds(new("child",7,1,15,AccessDenyReason.None),t));Assert.Equal([5],d.GetNewThresholds(new("child",7,1,5,AccessDenyReason.None),t));Assert.Equal([1],d.GetNewThresholds(new("child",7,1,1,AccessDenyReason.None),t));}
 [Fact] public void ReasonTextIsDisplayReady(){Assert.Equal("OUTSIDE_SCHEDULE",PolicyReasonText.ToDisplayText(AccessDenyReason.OutsideSchedule));Assert.Equal("QUOTA_EXHAUSTED",PolicyReasonText.ToDisplayText(AccessDenyReason.QuotaExhausted));Assert.Equal("PARENT_LOCK",PolicyReasonText.ToDisplayText(AccessDenyReason.ParentLock));}
}