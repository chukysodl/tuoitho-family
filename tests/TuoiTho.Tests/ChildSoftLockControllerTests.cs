using TuoiTho.Core.Policy;
using TuoiTho.SessionAgent;

namespace TuoiTho.Tests;

public sealed class ChildSoftLockControllerTests
{
    [Fact]
    public void QuotaExhaustedShowsOnlyOneM1BlockerAndAllowedHidesIt()
    {
        var view = new FakeView();
        using var controller = new ChildSoftLockController(view, "m1-child", m1TestMode: true);

        controller.UpdatePolicyState(Warning(AccessDenyReason.QuotaExhausted));
        controller.UpdatePolicyState(Warning(AccessDenyReason.QuotaExhausted));

        Assert.Single(view.Shown);
        Assert.True(controller.IsBlocked);
        Assert.True(view.Shown[0].ShowM1EmergencyExit);
        Assert.Equal("00:00:00", view.Shown[0].Remaining);

        controller.UpdatePolicyState(Warning(AccessDenyReason.None, remainingMinutes: 15));
        Assert.Equal(1, view.Hidden);
        Assert.False(controller.IsBlocked);
    }

    [Fact]
    public void GrantAllowedTransitionHidesQuotaBlockerWithoutChangingPolicy()
    {
        var view = new FakeView();
        using var controller = new ChildSoftLockController(view, "m1-child", m1TestMode: true);
        controller.UpdatePolicyState(Warning(AccessDenyReason.QuotaExhausted));
        controller.UpdatePolicyState(Warning(AccessDenyReason.None, remainingMinutes: 15));

        Assert.Equal(1, view.Hidden);
        Assert.False(controller.IsBlocked);
    }

    [Fact]
    public void ParentLockShowsAndClearParentLockWhenAllowedHides()
    {
        var view = new FakeView();
        using var controller = new ChildSoftLockController(view, "m1-child", m1TestMode: true);
        controller.UpdatePolicyState(Warning(AccessDenyReason.ParentLock));
        Assert.Single(view.Shown);
        Assert.Equal(AccessDenyReason.ParentLock, view.Shown[0].Reason);

        controller.UpdatePolicyState(Warning(AccessDenyReason.None, remainingMinutes: 3));
        Assert.Equal(1, view.Hidden);
    }

    [Fact]
    public void M1EmergencyCloseOnlyHidesVisualAndLeavesBlockedStateUntouched()
    {
        var view = new FakeView();
        using var controller = new ChildSoftLockController(view, "m1-child", m1TestMode: true);
        controller.UpdatePolicyState(Warning(AccessDenyReason.QuotaExhausted));
        view.RequestEmergencyExit();

        Assert.True(controller.IsBlocked);
        Assert.Equal(1, view.Hidden);
        controller.UpdatePolicyState(Warning(AccessDenyReason.QuotaExhausted));
        Assert.Single(view.Shown);
    }

    [Fact]
    public void M1ParentRecoveryHidesOnlyVisualUntilPolicyChangesThenCanBlockAgain()
    {
        var view = new FakeView();
        var launcher = new FakeLauncher(true);
        using var controller = new ChildSoftLockController(view, "m1-child", m1TestMode: true, launcher);
        controller.UpdatePolicyState(Warning(AccessDenyReason.QuotaExhausted));

        view.RequestParentControl();

        Assert.Equal(1, launcher.Calls);
        Assert.Equal(1, view.Hidden);
        Assert.True(controller.IsBlocked);
        controller.UpdatePolicyState(Warning(AccessDenyReason.QuotaExhausted));
        Assert.Single(view.Shown);

        controller.UpdatePolicyState(Warning(AccessDenyReason.None, remainingMinutes: 15));
        controller.UpdatePolicyState(Warning(AccessDenyReason.QuotaExhausted));
        Assert.Equal(2, view.Shown.Count);
    }

    [Fact]
    public void M1ParentRecoveryFailureRestoresOverlayWithoutChangingPolicy()
    {
        var view = new FakeView();
        using var controller = new ChildSoftLockController(view, "m1-child", m1TestMode: true, new FakeLauncher(false));
        controller.UpdatePolicyState(Warning(AccessDenyReason.QuotaExhausted));
        view.RequestParentControl();

        Assert.True(controller.IsBlocked);
        Assert.Equal(1, view.Hidden);
        Assert.Equal(2, view.Shown.Count);
    }
    [Fact]
    public void ProductionModeDoesNotExposeEmergencyCloseControl()
    {
        var view = new FakeView();
        using var controller = new ChildSoftLockController(view, "child", m1TestMode: false);
        controller.UpdatePolicyState(new SessionWarning("child", 7, 0, null, AccessDenyReason.QuotaExhausted));
        view.RequestEmergencyExit();

        Assert.False(view.Shown[0].ShowM1EmergencyExit);
        Assert.True(controller.IsBlocked);
        Assert.Equal(0, view.Hidden);
    }

    [Fact]
    public void F12IsTheOnlyM1EmergencyExitKey()
    {
        Assert.True(ChildSoftLockForm.IsM1EmergencyExitKey(System.Windows.Forms.Keys.F12));
        Assert.False(ChildSoftLockForm.IsM1EmergencyExitKey(System.Windows.Forms.Keys.Escape));
    }

    [Fact]
    public void ProductionModeCannotLaunchParentRecovery()
    {
        var view = new FakeView();
        var launcher = new FakeLauncher(true);
        using var controller = new ChildSoftLockController(view, "child", m1TestMode: false, launcher);
        controller.UpdatePolicyState(new SessionWarning("child", 7, 0, null, AccessDenyReason.QuotaExhausted));
        view.RequestParentControl();

        Assert.Equal(0, launcher.Calls);
        Assert.True(controller.IsBlocked);
    }
    [Fact]
    public void ForeignProfileIsIgnoredAndNoNativeEnforcementInterfaceExistsHere()
    {
        var view = new FakeView();
        using var controller = new ChildSoftLockController(view, "m1-child", m1TestMode: true);
        controller.UpdatePolicyState(new SessionWarning("other", 7, 0, null, AccessDenyReason.QuotaExhausted));

        Assert.Empty(view.Shown);
        Assert.DoesNotContain(typeof(ChildSoftLockController).GetInterfaces(), type => type.Name.Contains("ManagedSessionNative", StringComparison.Ordinal));
    }

    private static SessionWarning Warning(AccessDenyReason reason, int remainingMinutes = 0) => new("m1-child", 7, remainingMinutes, null, reason);

    private sealed class FakeView : IChildSoftLockView
    {
        public List<ChildSoftLockState> Shown { get; } = [];
        public int Hidden { get; private set; }
        public event Action? EmergencyExitRequested;
        public event Action? ParentControlRequested;
        public void Show(ChildSoftLockState state) => Shown.Add(state);
        public void Hide() => Hidden++;
        public void RequestEmergencyExit() => EmergencyExitRequested?.Invoke();
        public void RequestParentControl() => ParentControlRequested?.Invoke();
        public void Dispose() { }
    }
    private sealed class FakeLauncher(bool result) : IM1ParentControlLauncher
    {
        public int Calls { get; private set; }
        public bool TryOpenParentControl() { Calls++; return result; }
    }
}