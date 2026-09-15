using TuoiTho.Core.Policy;
using TuoiTho.Parent;
namespace TuoiTho.Tests;
public sealed class ParentUiTests
{
 [Fact] public void AppDecisionMapsToParentFriendlyVietnamese(){Assert.Equal("ĐƯỢC PHÉP",AppPolicyPanel.SimulationText(AppSimulationDecision.WouldAllow));Assert.Equal("SẼ BỊ CHẶN",AppPolicyPanel.SimulationText(AppSimulationDecision.WouldBlock));Assert.Equal("CHƯA DUYỆT — SẼ BỊ CHẶN",AppPolicyPanel.SimulationText(AppSimulationDecision.BlockUnknown));}
 [Fact] public void EmptyStateGuidesM2Tester(){Assert.Contains("Chưa phát hiện ứng dụng nào",AppPolicyPanel.EmptyStateText);Assert.Contains("Làm mới danh sách",AppPolicyPanel.EmptyStateText);}
 [Fact] public void AppActionsRequireASelectedRow(){using var panel=new AppPolicyPanel(new ParentDesktopController(new FakeClient(),"child",7));Assert.False(panel.ActionsEnabled);var identity=AppIdentity.FromExecutablePath("C:\\Apps\\Note.exe","Notepad");panel.Update(new ParentAppControlStatus(DefaultAppPolicy.BlockUnknown,[new ParentObservedApp(identity,AppSimulationDecision.BlockUnknown,"BLOCK_UNKNOWN",DateTimeOffset.UtcNow)]));Assert.False(panel.ActionsEnabled);panel.SelectRow(0);Assert.True(panel.ActionsEnabled);panel.SelectRow(-1);Assert.False(panel.ActionsEnabled);}
 private sealed class FakeClient : IParentControlClient { public Task<ParentControlResult> SendAsync(ParentControlCommand command,CancellationToken token=default)=>Task.FromResult(new ParentControlResult(false,"UNAVAILABLE")); }
}