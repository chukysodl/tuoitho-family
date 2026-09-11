using TuoiTho.Core.Policy;
namespace TuoiTho.Tests;
public sealed class SessionWarningTests
{
 [Fact] public void FifteenThenFourteenThenThirteenWarnsOnce(){var d=new WarningDeduplicator();var t=DeviceTimePolicy.DefaultWarnings;Assert.Empty(d.GetNewThresholds(16,t));Assert.Equal([15],d.GetNewThresholds(15,t));Assert.Empty(d.GetNewThresholds(14,t));Assert.Empty(d.GetNewThresholds(13,t));}
 [Fact] public void GrantResetsThresholdForLaterCrossing(){var d=new WarningDeduplicator();var t=DeviceTimePolicy.DefaultWarnings;d.GetNewThresholds(16,t);Assert.NotEmpty(d.GetNewThresholds(15,t));Assert.Empty(d.GetNewThresholds(20,t));Assert.Equal([15],d.GetNewThresholds(15,t));}
 [Fact] public void MultipleThresholdsCrossedAtOnceAreOrdered(){var d=new WarningDeduplicator();var t=DeviceTimePolicy.DefaultWarnings;d.GetNewThresholds(20,t);Assert.Equal([15,5,1],d.GetNewThresholds(0,t));}
 [Fact] public void RestartBaselineDoesNotCreateWarningStorm(){var d=new WarningDeduplicator();Assert.Empty(d.GetNewThresholds(1,DeviceTimePolicy.DefaultWarnings));Assert.Empty(d.GetNewThresholds(1,DeviceTimePolicy.DefaultWarnings));}
 [Fact] public void ReasonTextIsDisplayReady(){Assert.Equal("OUTSIDE_SCHEDULE",PolicyReasonText.ToDisplayText(AccessDenyReason.OutsideSchedule));Assert.Equal("QUOTA_EXHAUSTED",PolicyReasonText.ToDisplayText(AccessDenyReason.QuotaExhausted));Assert.Equal("PARENT_LOCK",PolicyReasonText.ToDisplayText(AccessDenyReason.ParentLock));}
}