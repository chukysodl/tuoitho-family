using TuoiTho.Core.Policy;

namespace TuoiTho.Service;

/// <summary>Memory-only M4 runtime health. It intentionally never retains a navigation URL or identity.</summary>
public sealed class BrowserRuntimeStatusCache(TimeProvider? timeProvider = null)
{
    private readonly object gate = new();
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    private ParentBrowserRuntimeStatus status = new(false, null, "KHÔNG XÁC ĐỊNH", "STARTING");

    public void Record(BrowserNavigationResponse response, string? shortOwnerState = null, string? shortContainer = null, int? shortOwnerCandidateCount = null, string? shortOwnerSource = null)
    {
        lock (gate)
            status = status with { ExtensionConnected = true, LastCheckedAtUtc = time.GetUtcNow(), LastResult = response.Allowed ? "CHO PHÉP" : "CHẶN", ShortOwnerState = shortOwnerState, ShortContainer = shortContainer, ShortOwnerCandidateCount = shortOwnerCandidateCount, ShortOwnerSource = shortOwnerSource };
    }

    public void RecordDnrSync(int customRuleCount, int activeDnrRuleCount, string state, string? error)
    {
        lock (gate)
            status = status with
            {
                ExtensionConnected = true,
                LastCheckedAtUtc = time.GetUtcNow(),
                CustomPolicyRuleCount = customRuleCount,
                ActiveDnrRuleCount = activeDnrRuleCount,
                DnrSyncState = state,
                DnrError = error
            };
    }
    public void SetBrowserPolicyReadiness(string state, string? error = null)
    {
        lock (gate) status = status with { BrowserPolicyState = state, BrowserPolicyError = error };
    }

    public ParentBrowserRuntimeStatus Snapshot()
    {
        lock (gate)
        {
            if (status.LastCheckedAtUtc is { } last && time.GetUtcNow() - last > TimeSpan.FromSeconds(45))
                return status with { ExtensionConnected = false };
            return status;
        }
    }
}
