using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TuoiTho.Core.Policy;
using TuoiTho.Service;
using TuoiTho.Storage;

namespace TuoiTho.Tests;

public sealed class CheckpointCControlAndEnforcementTests
{
    [Fact]
    public async Task ApprovedParentPersistsGrantAndOverrideAcrossStoreRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tuoitho-c-{Guid.NewGuid():N}.db");
        try
        {
            var database = new SqliteDatabase(path);
            await database.InitializeAsync();
            var store = new SqliteDeviceTimePolicyStore(database);
            var policy = Policy();
            await store.SaveAsync(policy);
            var service = new ParentControlService(store, new FakeClock(new DateTimeOffset(2026,9,14,9,0,0,TimeSpan.Zero),TimeZoneInfo.Utc), new PolicyChangeSignal());
            var parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "S-1-5-21-parent" };

            Assert.True((await service.ExecuteAsync(new(ParentControlAction.GrantMinutes, "child", 7, 15), "S-1-5-21-parent", parents)).Accepted);
            Assert.True((await service.ExecuteAsync(new(ParentControlAction.EmergencyOverride, "child", 7), "S-1-5-21-parent", parents)).Accepted);

            var reopened = new SqliteDeviceTimePolicyStore(new SqliteDatabase(path));
            Assert.True((await reopened.LoadAsync("child"))!.ParentOverride);
            Assert.Single(await reopened.GetGrantsAsync("child"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ChildOrSpoofedParentCommandFailsClosed()
    {
        var store = new InMemoryPolicyStore(Policy());
        var service = new ParentControlService(store, new FakeClock(new DateTimeOffset(2026,9,14,9,0,0,TimeSpan.Zero),TimeZoneInfo.Utc), new PolicyChangeSignal());
        var result = await service.ExecuteAsync(new(ParentControlAction.SetParentLock, "child", 7), "S-1-5-21-child", new HashSet<string> { "S-1-5-21-parent" });
        Assert.False(result.Accepted);
        Assert.Equal("UNAUTHORIZED", result.Error);
        var spoofed = await service.ExecuteAsync(new(ParentControlAction.SetParentLock, "child", 8), "S-1-5-21-parent", new HashSet<string> { "S-1-5-21-parent" });
        Assert.False(spoofed.Accepted);
        Assert.Equal("PROFILE_OR_SESSION_MISMATCH", spoofed.Error);
        Assert.True((await service.ExecuteAsync(new(ParentControlAction.SetParentLock, "child", 7), "S-1-5-18", new HashSet<string>())).Accepted);
    }

    [Fact]
    public async Task OverrideAndClearingOverrideRestoreNormalPolicy()
    {
        var store = new InMemoryPolicyStore(Policy());
        var service = new ParentControlService(store, new FakeClock(new DateTimeOffset(2026,9,14,9,0,0,TimeSpan.Zero),TimeZoneInfo.Utc), new PolicyChangeSignal());
        var parents = new HashSet<string> { "S-1-5-21-parent" };
        Assert.True((await service.ExecuteAsync(new(ParentControlAction.EmergencyOverride, "child", 7), "S-1-5-21-parent", parents)).Policy!.ParentOverride);
        var cleared = await service.ExecuteAsync(new(ParentControlAction.ClearOverride, "child", 7), "S-1-5-21-parent", parents);
        Assert.True(cleared.Accepted);
        Assert.False(cleared.Policy!.ParentOverride);
        Assert.True((await service.ExecuteAsync(new(ParentControlAction.SetParentLock, "child", 7), "S-1-5-21-parent", parents)).Policy!.ParentLock);
        Assert.False((await service.ExecuteAsync(new(ParentControlAction.ClearParentLock, "child", 7), "S-1-5-21-parent", parents)).Policy!.ParentLock);
    }

    [Fact]
    public async Task EnforcerTargetsOnlyManagedChildAndIsIdempotent()
    {
        var native = new FakeNativeApi { IdentityMatches = true };
        var enforcer = new SafeChildSessionEnforcer(native, NullLogger<SafeChildSessionEnforcer>.Instance);
        var denial = new PolicyDecision(false, AccessDenyReason.QuotaExhausted, 0, []);
        var policy = Policy() with { TestMode = false };
        Assert.Equal("NOT_TARGETED", (await enforcer.EnforceAsync(policy, 8, denial)).Outcome);
        Assert.Equal("REAL_SESSION_LOCK", (await enforcer.EnforceAsync(policy, 7, denial)).Outcome);
        Assert.Equal("ALREADY_ENFORCED", (await enforcer.EnforceAsync(policy, 7, denial)).Outcome);
        Assert.Equal([7], native.Disconnected);
    }

    [Fact]
    public async Task TestModeSimulatesAndParentOrAdminSessionsAreNeverDisconnected()
    {
        var native = new FakeNativeApi { IdentityMatches = true };
        var enforcer = new SafeChildSessionEnforcer(native, NullLogger<SafeChildSessionEnforcer>.Instance);
        var denial = new PolicyDecision(false, AccessDenyReason.ParentLock, 0, []);
        Assert.Equal("SIMULATED_LOCK", (await enforcer.EnforceAsync(Policy() with { TestMode = true }, 7, denial)).Outcome);
        Assert.Empty(native.Disconnected);
        var foreignIdentity = new FakeNativeApi { IdentityMatches = false };
        var real = new SafeChildSessionEnforcer(foreignIdentity, NullLogger<SafeChildSessionEnforcer>.Instance);
        Assert.Equal("SESSION_IDENTITY_REJECTED", (await real.EnforceAsync(Policy() with { TestMode = false }, 7, denial)).Outcome);
        Assert.Empty(foreignIdentity.Disconnected);
    }

    [Fact]
    public void ParentPipeAclIsSpecificAndHasNoBroadUserGroups()
    {
        var parent = new SecurityIdentifier("S-1-5-21-111-222-333-444");
        var rules = ParentControlPipeSecurity.Create([parent.Value]).GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToArray();
        Assert.Contains(rules, rule => rule.IdentityReference == parent && (rule.PipeAccessRights & PipeAccessRights.WriteData) != 0);
        Assert.Contains(rules, rule => rule.IdentityReference == new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
        foreach (var type in new[] { WellKnownSidType.WorldSid, WellKnownSidType.AuthenticatedUserSid, WellKnownSidType.BuiltinUsersSid }) Assert.DoesNotContain(rules, rule => rule.IdentityReference == new SecurityIdentifier(type, null));
    }

    [Fact]
    public async Task MalformedParentPayloadFailsSafely()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{bad"));
        Assert.Null(await ParentControlListener.ReadCommandAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task AllowedEpisodeResetsEnforcementLatch()
    {
        var native = new FakeNativeApi { IdentityMatches = true };
        var enforcer = new SafeChildSessionEnforcer(native, NullLogger<SafeChildSessionEnforcer>.Instance);
        var policy = Policy() with { TestMode = false };
        var denied = new PolicyDecision(false, AccessDenyReason.QuotaExhausted, 0, []);
        await enforcer.EnforceAsync(policy, 7, denied);
        await enforcer.EnforceAsync(policy, 7, new PolicyDecision(true, AccessDenyReason.None, 10, []));
        await enforcer.EnforceAsync(policy, 7, denied);
        Assert.Equal([7, 7], native.Disconnected);
    }
    private static DeviceTimePolicy Policy() => new("child", 7, 30, [new AllowedUsageWindow(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(10, 0))], DeviceTimePolicy.DefaultWarnings, false, false, true) { ManagedUserSid = "S-1-5-21-child" };

    private sealed class FixedTimeProvider : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 9, 14, 9, 0, 0, TimeSpan.Zero); }
    private sealed class FakeNativeApi : IManagedSessionNativeApi
    {
        public bool IdentityMatches { get; init; }
        public List<int> Disconnected { get; } = [];
        public Task<bool> IsManagedChildSessionAsync(int sessionId, string managedUserSid, CancellationToken cancellationToken = default) => Task.FromResult(IdentityMatches && sessionId == 7 && managedUserSid == "S-1-5-21-child");
        public Task DisconnectSessionAsync(int sessionId, CancellationToken cancellationToken = default) { Disconnected.Add(sessionId); return Task.CompletedTask; }
    }

    private sealed class InMemoryPolicyStore(DeviceTimePolicy policy) : IDeviceTimePolicyStore
    {
        private DeviceTimePolicy policy = policy;
        private readonly List<TemporaryGrant> grants = [];
        public Task<DeviceTimePolicy?> LoadAsync(string profileId, CancellationToken cancellationToken = default) => Task.FromResult<DeviceTimePolicy?>(profileId == policy.ProfileId ? policy : null);
        public Task SaveAsync(DeviceTimePolicy value, CancellationToken cancellationToken = default) { policy = value; return Task.CompletedTask; }
        public Task AddGrantAsync(string profileId, TemporaryGrant grant, CancellationToken cancellationToken = default) { grants.Add(grant); return Task.CompletedTask; }
        public Task<IReadOnlyList<TemporaryGrant>> GetGrantsAsync(string profileId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TemporaryGrant>>(grants);
    }
}