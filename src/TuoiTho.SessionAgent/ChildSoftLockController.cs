using TuoiTho.Core.Policy;

namespace TuoiTho.SessionAgent;

public sealed record ChildSoftLockState(
    string ProfileId,
    AccessDenyReason Reason,
    string Remaining,
    bool ShowM1EmergencyExit);

public interface IChildSoftLockView : IDisposable
{
    event Action? EmergencyExitRequested;
    event Action? ParentControlRequested;
    void Show(ChildSoftLockState state);
    void Hide();
}

public interface IChildSoftLockController : IDisposable
{
    void UpdatePolicyState(SessionWarning warning);
    void CloseM1OverlayForSafety();
    bool IsBlocked { get; }
}
public interface IM1ParentControlLauncher
{
    bool TryOpenParentControl();
}

/// <summary>
/// M1 visual-only blocker. It never calls Windows lock, logoff, shutdown, or process-control APIs.
/// </summary>
public sealed class ChildSoftLockController : IChildSoftLockController
{
    private readonly object gate = new();
    private readonly IChildSoftLockView view;
    private readonly string profileId;
    private readonly bool allowM1EmergencyExit;
    private readonly IM1ParentControlLauncher? parentControlLauncher;
    private AccessDenyReason blockedReason;
    private bool disposed;
    private bool visualDismissed;

    public ChildSoftLockController(IChildSoftLockView view, string profileId, bool m1TestMode, IM1ParentControlLauncher? parentControlLauncher = null)
    {
        this.view = view ?? throw new ArgumentNullException(nameof(view));
        this.profileId = string.IsNullOrWhiteSpace(profileId) ? throw new ArgumentException("A profile ID is required.", nameof(profileId)) : profileId;
        allowM1EmergencyExit = m1TestMode && string.Equals(profileId, "m1-child", StringComparison.Ordinal);
        this.parentControlLauncher = parentControlLauncher;
        view.EmergencyExitRequested += CloseM1OverlayForSafety;
        view.ParentControlRequested += OpenParentControlForM1;
    }

    public bool IsBlocked
    {
        get { lock (gate) return blockedReason != AccessDenyReason.None; }
    }

    public void UpdatePolicyState(SessionWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);
        if (!string.Equals(warning.ProfileId, profileId, StringComparison.Ordinal))
        {
            return;
        }

        var nextReason = IsSoftBlockedReason(warning.Reason) ? warning.Reason : AccessDenyReason.None;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (nextReason == AccessDenyReason.None)
            {
                if (blockedReason != AccessDenyReason.None || visualDismissed)
                {
                    view.Hide();
                }

                blockedReason = AccessDenyReason.None;
                visualDismissed = false;
                return;
            }

            if (blockedReason == nextReason)
            {
                return;
            }

            blockedReason = nextReason;
            visualDismissed = false;
            view.Show(new ChildSoftLockState(profileId, nextReason, "00:00:00", allowM1EmergencyExit));
        }
    }

    public void CloseM1OverlayForSafety()
    {
        lock (gate)
        {
            if (disposed || !allowM1EmergencyExit || blockedReason == AccessDenyReason.None || visualDismissed)
            {
                return;
            }

            visualDismissed = true;
            view.Hide();
        }
    }

    private void OpenParentControlForM1()
    {
        lock (gate)
        {
            if (disposed || !allowM1EmergencyExit || blockedReason == AccessDenyReason.None || visualDismissed)
            {
                return;
            }

            // The visual overlay must yield before the existing Parent window can be foregrounded.
            view.Hide();
            if (parentControlLauncher?.TryOpenParentControl() == true)
            {
                // No policy data changes here. It can be shown again only after ALLOWED then blocked.
                visualDismissed = true;
                return;
            }

            view.Show(new ChildSoftLockState(profileId, blockedReason, "00:00:00", allowM1EmergencyExit));
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            view.EmergencyExitRequested -= CloseM1OverlayForSafety;
            view.ParentControlRequested -= OpenParentControlForM1;
            view.Hide();
            view.Dispose();
        }
    }

    private static bool IsSoftBlockedReason(AccessDenyReason reason) => reason is AccessDenyReason.QuotaExhausted or AccessDenyReason.ParentLock;
}