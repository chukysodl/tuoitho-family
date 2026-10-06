using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TuoiTho.Core.Policy;
using TuoiTho.Service;

namespace TuoiTho.Tests;

public sealed class ProductionProtectionTests
{
    [Fact]
    public async Task RebootRebindPreservesParentLockAndRestartsMissingAgent()
    {
        var original = Policy(parentLock: true) with { ManagedSessionId = 3, ManagedUserSid = "S-1-5-21-old", TestMode = true };
        var store = new Store(original);
        var runtime = new Runtime(new ActiveInteractiveSession(7, "S-1-5-21-child", false));
        var options = new ProductionProtectionOptions
        {
            Enabled = true,
            ProfileId = "m1-child",
            SessionAgentPath = Path.Combine(Path.GetTempPath(), "TuoiTho.SessionAgent.exe"),
            PollIntervalSeconds = 2
        };
        var watchdog = new ProductionSessionAgentWatchdog(
            Options.Create(options),
            store,
            new PolicyChangeSignal(),
            runtime,
            NullLogger<ProductionSessionAgentWatchdog>.Instance);

        await watchdog.TickAsync(options, CancellationToken.None);

        Assert.NotNull(store.Current);
        Assert.True(store.Current!.ParentLock);
        Assert.False(store.Current.TestMode);
        Assert.Equal(7, store.Current.ManagedSessionId);
        Assert.Equal("S-1-5-21-child", store.Current.ManagedUserSid);
        Assert.Equal(1, runtime.Launches);
    }

    [Fact]
    public async Task AdministratorSessionIsNeverReboundAsChild()
    {
        var original = Policy(parentLock: true) with
        {
            ManagedSessionId = 7,
            ManagedUserSid = "S-1-5-21-child",
            TestMode = false
        };
        var store = new Store(original);
        var runtime = new Runtime(
            new ActiveInteractiveSession(9, "S-1-5-21-parent", true));
        var options = new ProductionProtectionOptions
        {
            Enabled = true,
            ProfileId = "m1-child",
            SessionAgentPath = Path.Combine(
                Path.GetTempPath(),
                "TuoiTho.SessionAgent.exe"),
            PollIntervalSeconds = 2
        };
        var watchdog = new ProductionSessionAgentWatchdog(
            Options.Create(options),
            store,
            new PolicyChangeSignal(),
            runtime,
            NullLogger<ProductionSessionAgentWatchdog>.Instance);

        await watchdog.TickAsync(options, CancellationToken.None);

        Assert.Equal(7, store.Current!.ManagedSessionId);
        Assert.Equal("S-1-5-21-child", store.Current.ManagedUserSid);
        Assert.Equal(0, runtime.Launches);
    }

    [Fact]
    public async Task RunningAgentIsNotDuplicated()
    {
        var store = new Store(Policy(parentLock: false) with { ManagedSessionId = 7, ManagedUserSid = "S-1-5-21-child", TestMode = false });
        var runtime = new Runtime(new ActiveInteractiveSession(7, "S-1-5-21-child")) { Running = true };
        var options = new ProductionProtectionOptions
        {
            Enabled = true,
            ProfileId = "m1-child",
            SessionAgentPath = Path.Combine(Path.GetTempPath(), "TuoiTho.SessionAgent.exe"),
            PollIntervalSeconds = 2
        };
        var watchdog = new ProductionSessionAgentWatchdog(
            Options.Create(options),
            store,
            new PolicyChangeSignal(),
            runtime,
            NullLogger<ProductionSessionAgentWatchdog>.Instance);

        await watchdog.TickAsync(options, CancellationToken.None);

        Assert.Equal(0, runtime.Launches);
    }

    private static DeviceTimePolicy Policy(bool parentLock) =>
        new(
            "m1-child",
            7,
            60,
            [new AllowedUsageWindow(DayOfWeek.Monday, TimeOnly.MinValue, new TimeOnly(23, 59))],
            DeviceTimePolicy.DefaultWarnings,
            parentLock,
            false,
            false);

    private sealed class Runtime(ActiveInteractiveSession session) : IInteractiveSessionAgentRuntime
    {
        public bool Running { get; init; }
        public int Launches { get; private set; }
        public ActiveInteractiveSession? GetActiveSession() => session;
        public bool IsAgentRunning(int sessionId, string executablePath) => Running;
        public void LaunchAgent(ActiveInteractiveSession active, string executablePath, string profileId, string? parentExecutablePath) => Launches++;
    }

    private sealed class Store(DeviceTimePolicy? policy) : IDeviceTimePolicyStore
    {
        public DeviceTimePolicy? Current { get; private set; } = policy;
        public Task<DeviceTimePolicy?> LoadAsync(string profileId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Current?.ProfileId == profileId ? Current : null);
        public Task SaveAsync(DeviceTimePolicy value, CancellationToken cancellationToken = default)
        {
            Current = value;
            return Task.CompletedTask;
        }
        public Task AddGrantAsync(string profileId, TemporaryGrant grant, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<TemporaryGrant>> GetGrantsAsync(string profileId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TemporaryGrant>>([]);
    }
}
