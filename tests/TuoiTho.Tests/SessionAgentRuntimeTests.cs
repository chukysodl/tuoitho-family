using System.Security.Principal;
using Microsoft.Extensions.Logging.Abstractions;
using TuoiTho.Core.Policy;
using TuoiTho.Service;
using TuoiTho.SessionAgent;

namespace TuoiTho.Tests;

public sealed class SessionAgentRuntimeTests
{
    [Fact]
    public async Task ServicePublisherReachesRealListenerAndUpdatesSoftLockState()
    {
        var sid = WindowsIdentity.GetCurrent().User!;
        var session = System.Diagnostics.Process.GetCurrentProcess().SessionId;
        var profile = "child";
        var sink = new Sink();
        var view = new View();
        using var softLock = new ChildSoftLockController(view, profile, m1TestMode: false);
        var pipeName = $"TuoiTho.Warning.Test.{Guid.NewGuid():N}";
        var listener = new LocalWarningListener(profile, session, sink, NullLogger<LocalWarningListener>.Instance, sid, softLock, _ => pipeName);
        var listen = listener.ListenOnceAsync(CancellationToken.None);
        await Task.Delay(150);
        var publisher = new LocalSessionWarningPublisher(NullLogger<LocalSessionWarningPublisher>.Instance, _ => pipeName);
        var sent = false;
        for (var attempt = 0; attempt < 5 && !sent; attempt++)
        {
            sent = await publisher.TryPublishAsync(new SessionWarning(profile, session, 0, null, AccessDenyReason.QuotaExhausted));
            if (!sent) await Task.Delay(50);
        }

        Assert.True(sent);
        await listen.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Contains("QUOTA_EXHAUSTED", sink.Messages);
        Assert.Single(view.Shown);
        Assert.Equal(AccessDenyReason.QuotaExhausted, view.Shown[0].Reason);
    }

    private sealed class Sink : IChildWarningSink { public List<string> Messages { get; } = []; public void Show(string text) => Messages.Add(text); }
    private sealed class View : IChildSoftLockView
    {
        public List<ChildSoftLockState> Shown { get; } = [];
        public event Action? EmergencyExitRequested { add { } remove { } }
        public event Action? ParentControlRequested { add { } remove { } }
        public void Show(ChildSoftLockState state) => Shown.Add(state);
        public void Hide() { }
        public void Dispose() { }
    }
}