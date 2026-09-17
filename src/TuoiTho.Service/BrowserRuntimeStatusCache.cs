using TuoiTho.Core.Policy;

namespace TuoiTho.Service;

/// <summary>Memory-only M4 runtime health. It intentionally never retains a navigation URL or identity.</summary>
public sealed class BrowserRuntimeStatusCache(TimeProvider? timeProvider = null)
{
    private readonly object gate = new();
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    private ParentBrowserRuntimeStatus status = new(false, null, "KHÔNG XÁC ĐỊNH");

    public void Record(BrowserNavigationResponse response)
    {
        lock (gate)
            status = new ParentBrowserRuntimeStatus(true, time.GetUtcNow(), response.Allowed ? "CHO PHÉP" : "CHẶN");
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
