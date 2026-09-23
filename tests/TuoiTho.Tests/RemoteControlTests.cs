using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TuoiTho.Core.Policy;
using TuoiTho.Core.Remote;
using TuoiTho.Core.Time;
using TuoiTho.Service;
using TuoiTho.Storage;

namespace TuoiTho.Tests;

public sealed class RemoteControlTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PairingCodeHasHighEntropyAndOnlyUsesUnambiguousCharacters()
    {
        var codes = Enumerable.Range(0, 100).Select(_ => RemotePairingCode.Create()).ToArray();
        Assert.All(codes, code => Assert.Matches("^[A-HJ-NP-Z2-9]{4}(-[A-HJ-NP-Z2-9]{4}){3}$", code));
        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.NotEqual(RemotePairingCode.Sha256(codes[0]), RemotePairingCode.Sha256(codes[1]));
    }

    [Theory]
    [InlineData("WRONG_DEVICE", "11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222", 10, true)]
    [InlineData("COMMAND_EXPIRED", "11111111-1111-1111-1111-111111111111", "11111111-1111-1111-1111-111111111111", -1, true)]
    [InlineData("COMMAND_TTL_TOO_LONG", "11111111-1111-1111-1111-111111111111", "11111111-1111-1111-1111-111111111111", 20, true)]
    [InlineData("INVALID_NONCE", "11111111-1111-1111-1111-111111111111", "11111111-1111-1111-1111-111111111111", 10, false)]
    public void ValidatorRejectsWrongDeviceExpiredLongOrMalformedCommands(string expectedError, string id, string expectedDevice, int expiresMinutes, bool validNonce)
    {
        var now = Now;
        var issued = now.AddMinutes(-1);
        var command = new RemoteCommandEnvelope(Guid.NewGuid().ToString(), id, validNonce ? "1234567890abcdef" : "x", now, issued, now.AddMinutes(expiresMinutes), RemoteCommandKind.LockNow, JsonDocument.Parse("{}").RootElement.Clone());
        var result = RemoteCommandValidator.Validate(command, expectedDevice, now);
        Assert.False(result.Valid);
        Assert.Equal(expectedError, result.ErrorCode);
    }

    [Fact]
    public async Task PairingExpiresAndCanOnlyBeConsumedOnce()
    {
        await WithDatabase(async db =>
        {
            var store = new SqliteRemoteCommandStateStore(db);
            var hash = RemotePairingCode.Sha256(RemotePairingCode.Create());
            await store.SavePendingPairingAsync(new("device-id", hash, Now.AddMinutes(5), false));
            Assert.False(await store.TryConsumePairingAsync(hash, Now.AddMinutes(6)));
            Assert.True(await store.TryConsumePairingAsync(hash, Now));
            Assert.False(await store.TryConsumePairingAsync(hash, Now.AddSeconds(1)));
        });
    }

    [Fact]
    public async Task ParentPairingActionDisplaysOnlyShortLivedCodeAndRegistersItsHash()
    {
        await WithDatabase(async db =>
        {
            var state = new SqliteRemoteCommandStateStore(db);
            var transport = new FakeRemoteTransport();
            var clock = new FakeClock(Now, TimeZoneInfo.Utc);
            var options = Options.Create(new RemoteControlOptions { Enabled = true, SupabaseUrl = "https://example.supabase.co", SupabaseAnonKey = "public", DeviceName = "Test PC" });
            var identity = new RemoteDeviceIdentityManager(state, new FakeCredentialProtector(), transport, clock, options);
            var policyStore = new SqliteDeviceTimePolicyStore(db);
            await policyStore.SaveAsync(Policy());
            var controls = new ParentControlService(policyStore, new FakeTimeUsageStore(), clock, new DeviceTimePolicyEngine(clock), new PolicyChangeSignal(), remoteIdentity: identity);

            var result = await controls.ExecuteAsync(new(ParentControlAction.CreateRemotePairing, "child", 7), "parent", new HashSet<string> { "parent" });
            Assert.True(result.Accepted);
            Assert.NotNull(result.Message);
            var code = result.Message!.Split(':').Last().Trim();
            Assert.Matches("^[A-HJ-NP-Z2-9]{4}(-[A-HJ-NP-Z2-9]{4}){3}$", code);
            var registration = Assert.Single(transport.Pairings);
            Assert.Equal(RemotePairingCode.Sha256(code), registration.PairCodeSha256);
            Assert.DoesNotContain(code, JsonSerializer.Serialize(registration));
            Assert.InRange(registration.ExpiresAtUtc - Now, TimeSpan.FromMinutes(4.99), TimeSpan.FromMinutes(5));
        });
    }

    [Fact]
    public async Task CommandIdAndNonceReplayAreDurablyRejectedAcrossReopen()
    {
        await WithDatabase(async db =>
        {
            var command = MakeCommand();
            var first = new SqliteRemoteCommandStateStore(db);
            Assert.True(await first.TryRecordCommandAsync(command, Now));
            Assert.False(await new SqliteRemoteCommandStateStore(db).TryRecordCommandAsync(command, Now.AddSeconds(1)));
            var sameNonceDifferentId = command with { CommandId = Guid.NewGuid().ToString() };
            Assert.False(await new SqliteRemoteCommandStateStore(db).TryRecordCommandAsync(sameNonceDifferentId, Now.AddSeconds(2)));
            var acknowledgement = new RemoteCommandAcknowledgement(command.CommandId, command.DeviceId, RemoteCommandState.Applied, Now, null, "ALLOWED", 600, true);
            await first.CompleteCommandAsync(acknowledgement);
            Assert.Equal(acknowledgement, Assert.Single(await new SqliteRemoteCommandStateStore(db).GetUnacknowledgedResultsAsync()));
            await first.MarkAcknowledgementSentAsync(command.CommandId);
            Assert.Empty(await first.GetUnacknowledgedResultsAsync());
        });
    }

    [Fact]
    public async Task RemotePolicySnapshotAppliesAtomicallyButCannotChangeTestModeLockOrSession()
    {
        await WithDatabase(async db =>
        {
            var localStore = new SqliteDeviceTimePolicyStore(db);
            var original = Policy() with { ParentLock = true, ParentOverride = false, TestMode = true };
            await localStore.SaveAsync(original);
            var appStore = new SqliteAppPolicyStore(db);
            var webStore = new SqliteWebPolicyStore(db);
            var remote = new SqliteRemotePolicyStore(db);
            var app = new AppRule("child", AppIdentity.FromExecutablePath("C:\\Apps\\Reader.exe", "Reader"), AppRuleDecision.Allow);
            var web = new WebRule("child", BrowserProvider.GenericWeb, WebRuleScope.Domain, WebRuleDecision.Block, "domain:example.com", "example.com");
            var snapshot = new RemotePolicySnapshot(Guid.NewGuid().ToString(), "child", 7, 1,
                original with { DailyQuotaMinutes = 45, ParentLock = false, ParentOverride = true, TestMode = false },
                DefaultAppPolicy.BlockUnknown, [app], [web]);

            Assert.True(await remote.ApplyAsync(snapshot));
            var stored = await localStore.LoadAsync("child");
            Assert.NotNull(stored);
            Assert.Equal(45, stored.DailyQuotaMinutes);
            Assert.True(stored.TestMode);
            Assert.True(stored.ParentLock);
            Assert.False(stored.ParentOverride);
            Assert.Equal(7, stored.ManagedSessionId);
            Assert.Single(await appStore.GetRulesAsync("child"));
            Assert.Equal("domain:example.com", Assert.Single(await webStore.GetRulesAsync("child")).NormalizedKey);
            Assert.Equal(1, await remote.GetAppliedRevisionAsync(snapshot.DeviceId));
            Assert.False(await remote.ApplyAsync(snapshot));
        });
    }

    [Fact]
    public async Task RemotePolicyRejectsWrongSessionAndMalformedRuleWithoutPartialWrites()
    {
        await WithDatabase(async db =>
        {
            var localStore = new SqliteDeviceTimePolicyStore(db);
            await localStore.SaveAsync(Policy());
            var remote = new SqliteRemotePolicyStore(db);
            var device = Guid.NewGuid().ToString();
            var wrongSession = new RemotePolicySnapshot(device, "child", 8, 1, Policy() with { ManagedSessionId = 8 }, DefaultAppPolicy.BlockUnknown, [], []);
            Assert.False(await remote.ApplyAsync(wrongSession));
            var malformed = new RemotePolicySnapshot(device, "child", 7, 1, Policy(), DefaultAppPolicy.BlockUnknown,
                [new AppRule("wrong-profile", AppIdentity.FromExecutablePath("C:\\Bad.exe"), AppRuleDecision.Block)], []);
            Assert.False(await remote.ApplyAsync(malformed));
            Assert.Equal(0, await remote.GetAppliedRevisionAsync(device));
            Assert.Equal(30, (await localStore.LoadAsync("child"))!.DailyQuotaMinutes);
        });
    }

    [Fact]
    public async Task RemoteLockUnlockAndGrantReuseLocalParentPolicyOperations()
    {
        var store = new RemoteMemoryPolicyStore(Policy());
        var clock = new FakeClock(Now, TimeZoneInfo.Utc);
        var service = new ParentControlService(store, new FakeTimeUsageStore(), clock, new DeviceTimePolicyEngine(clock), new PolicyChangeSignal());

        var locked = await service.ExecuteRemoteAsync(new(ParentControlAction.SetParentLock, "child", 7));
        Assert.True(locked.Accepted);
        Assert.Equal("PARENT_LOCK", locked.Status!.State);
        var unlocked = await service.ExecuteRemoteAsync(new(ParentControlAction.ClearParentLock, "child", 7));
        Assert.True(unlocked.Accepted);
        Assert.Equal("ALLOWED", unlocked.Status!.State);
        var granted = await service.ExecuteRemoteAsync(new(ParentControlAction.GrantMinutes, "child", 7, 30));
        Assert.True(granted.Accepted);
        Assert.Equal(60, granted.Status!.RemainingMinutes);
    }

    [Fact]
    public void UnlockDoesNotBypassQuotaScheduleAndTestModeIsVisible()
    {
        var policy = Policy() with { ParentLock = false };
        var clock = new FakeClock(Now, TimeZoneInfo.Utc);
        var engine = new DeviceTimePolicyEngine(clock);
        var exhausted = engine.Evaluate(policy, TimeSpan.FromMinutes(30), []);
        Assert.Equal(AccessDenyReason.QuotaExhausted, exhausted.Reason);
        Assert.True(policy.TestMode);
    }

    [Fact]
    public async Task SupabaseAdapterSendsOnlyPairHashesAndUsesDeviceCredentialForPolling()
    {
        var deviceId = Guid.NewGuid().ToString();
        var captured = new List<(string Body, string? DeviceId, string? Credential, string Uri)>();
        var handler = new DelegateHttpHandler(request =>
        {
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            captured.Add((body, request.Headers.TryGetValues("x-tuoi-tho-device-id", out var deviceHeader) ? deviceHeader.Single() : null,
                request.Headers.TryGetValues("x-tuoi-tho-device-credential", out var credentialHeader) ? credentialHeader.Single() : null, request.RequestUri!.ToString()));
            if (body.Contains("register_pairing", StringComparison.Ordinal)) return new(HttpStatusCode.OK) { Content = new StringContent("{\"accepted\":true}") };
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"commands\":[]}") };
        });
        using var client = new HttpClient(handler);
        using var transport = new SupabaseRemoteTransport(new RemoteControlOptions { Enabled = true, SupabaseUrl = "https://example.supabase.co", SupabaseAnonKey = "public-anon" }, client);
        const string code = "ABCD-EFGH-JKLM-NPQR";
        await transport.RegisterPairingAsync(new(deviceId, "Family PC", RemotePairingCode.Sha256(code), RemotePairingCode.HashCredential("secret-token"), Now.AddMinutes(5)));
        var pairingBody = captured[0].Body;
        Assert.Contains(RemotePairingCode.Sha256(code), pairingBody);
        Assert.DoesNotContain(code, pairingBody);
        Assert.Null(captured[0].Credential);

        await transport.ReceiveCommandsAsync(new(deviceId, "secret-token"));
        Assert.Equal(deviceId, captured[1].DeviceId);
        Assert.Equal("secret-token", captured[1].Credential);
        Assert.Equal("https://example.supabase.co/functions/v1/device-gateway", captured[1].Uri);
    }

    [Fact]
    public async Task FakeTransportLockAndGrantCommandsReuseLocalServiceAndReplayIsNotExecutedTwice()
    {
        await WithDatabase(async db =>
        {
            var deviceId = Guid.NewGuid().ToString();
            var token = "device-credential-secret-0123456789abcdef";
            var state = new SqliteRemoteCommandStateStore(db);
            await state.SaveDeviceAsync(new(deviceId, "Test PC", Encoding.UTF8.GetBytes(token), Now));
            var protector = new FakeCredentialProtector();
            var transport = new FakeRemoteTransport();
            var identity = new RemoteDeviceIdentityManager(state, protector, transport, new FakeClock(Now, TimeZoneInfo.Utc), Options.Create(new RemoteControlOptions { Enabled = true, DeviceName = "Test PC", SupabaseUrl = "https://example.supabase.co", SupabaseAnonKey = "public" }));
            var clock = new FakeClock(Now, TimeZoneInfo.Utc);
            var changes = new PolicyChangeSignal();
            var policies = new SqliteDeviceTimePolicyStore(db);
            await policies.SaveAsync(Policy());
            var web = new SqliteWebPolicyStore(db);
            var remotePolicyStore = new SqliteRemotePolicyStore(db);
            var parent = new ParentControlService(policies, new SqliteTimeUsageStore(db), clock, new DeviceTimePolicyEngine(clock), changes, webPolicies: web, remotePolicies: remotePolicyStore);
            var appStore = new SqliteAppPolicyStore(db);
            transport.Commands.Add(MakeCommand(deviceId, RemoteCommandKind.LockNow, "{}"));
            var worker = MakeWorker(transport, state, remotePolicyStore, identity, parent, policies, appStore, web, clock);

            await worker.RunCycleAsync();
            Assert.True((await policies.LoadAsync("child"))!.ParentLock);
            Assert.True(transport.Acknowledgements[0].State == RemoteCommandState.Applied);
            Assert.True(transport.Acknowledgements[0].TestMode);

            transport.Commands.Clear();
            transport.Commands.Add(MakeCommand(deviceId, RemoteCommandKind.GrantTime, "{\"minutes\":30}"));
            await worker.RunCycleAsync();
            Assert.Single(await policies.GetGrantsAsync("child"));
            var firstGrantAck = Assert.Single(transport.Acknowledgements, item => item.State == RemoteCommandState.Applied && item.CommandId == transport.Commands[0].CommandId);

            await worker.RunCycleAsync();
            Assert.Single(await policies.GetGrantsAsync("child"));
            Assert.Contains(transport.Acknowledgements, item => item.CommandId == firstGrantAck.CommandId && item.State == RemoteCommandState.Duplicate);
        });
    }

    [Fact]
    public async Task RemoteSyncAppliesSnapshotAndProviderOutageDoesNotChangeLocalPolicy()
    {
        await WithDatabase(async db =>
        {
            var deviceId = Guid.NewGuid().ToString();
            var state = new SqliteRemoteCommandStateStore(db);
            await state.SaveDeviceAsync(new(deviceId, "Test PC", Encoding.UTF8.GetBytes("device-credential-secret-0123456789abcdef"), Now));
            var clock = new FakeClock(Now, TimeZoneInfo.Utc);
            var changes = new PolicyChangeSignal();
            var timeStore = new SqliteDeviceTimePolicyStore(db);
            await timeStore.SaveAsync(Policy());
            var apps = new SqliteAppPolicyStore(db);
            var web = new SqliteWebPolicyStore(db);
            var remotePolicies = new SqliteRemotePolicyStore(db);
            var parent = new ParentControlService(timeStore, new SqliteTimeUsageStore(db), clock, new DeviceTimePolicyEngine(clock), changes, appPolicies: apps, appPolicyEngine: new AppPolicyEngine(), webPolicies: web, remotePolicies: remotePolicies);
            var snapshot = new RemotePolicySnapshot(deviceId, "child", 7, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Policy() with { DailyQuotaMinutes = 50, TestMode = false }, DefaultAppPolicy.BlockUnknown, [], []);
            var transport = new FakeRemoteTransport();
            transport.Commands.Add(MakeCommand(deviceId, RemoteCommandKind.SyncPolicy, JsonSerializer.Serialize(snapshot, RemoteJson)));
            var manager = new RemoteDeviceIdentityManager(state, new FakeCredentialProtector(), transport, clock, Options.Create(new RemoteControlOptions { Enabled = true, DeviceName = "Test PC" }));
            var worker = MakeWorker(transport, state, remotePolicies, manager, parent, timeStore, apps, web, clock);
            await worker.RunCycleAsync();
            var stored = await timeStore.LoadAsync("child");
            Assert.Equal(50, stored!.DailyQuotaMinutes);
            Assert.True(stored.TestMode);
            Assert.Equal(RemoteCommandState.Applied, Assert.Single(transport.Acknowledgements).State);
            Assert.Equal(snapshot.Revision, await remotePolicies.GetAppliedRevisionAsync(deviceId));

            transport.ThrowOnPoll = true;
            await Assert.ThrowsAsync<HttpRequestException>(() => worker.RunCycleAsync());
            Assert.Equal(50, (await timeStore.LoadAsync("child"))!.DailyQuotaMinutes);
            Assert.True(new DeviceTimePolicyEngine(clock).Evaluate(stored, TimeSpan.Zero, []).Allowed);
        });
    }

    [Fact]
    public async Task OfflineCommandWaitsInProviderQueueAndIsAppliedAfterReconnectWhileLocalPolicyRemainsAvailable()
    {
        await WithDatabase(async db =>
        {
            var deviceId = Guid.NewGuid().ToString();
            var state = new SqliteRemoteCommandStateStore(db);
            await state.SaveDeviceAsync(new(deviceId, "Test PC", Encoding.UTF8.GetBytes("device-credential-secret-0123456789abcdef"), Now));
            var clock = new FakeClock(Now, TimeZoneInfo.Utc);
            var policyStore = new SqliteDeviceTimePolicyStore(db);
            await policyStore.SaveAsync(Policy());
            var apps = new SqliteAppPolicyStore(db);
            var web = new SqliteWebPolicyStore(db);
            var remotePolicies = new SqliteRemotePolicyStore(db);
            var parent = new ParentControlService(policyStore, new SqliteTimeUsageStore(db), clock, new DeviceTimePolicyEngine(clock), new PolicyChangeSignal(), appPolicies: apps, appPolicyEngine: new AppPolicyEngine(), webPolicies: web, remotePolicies: remotePolicies);
            var transport = new FakeRemoteTransport { ThrowOnPoll = true };
            var command = MakeCommand(deviceId, RemoteCommandKind.LockNow, "{}");
            transport.Commands.Add(command);
            var manager = new RemoteDeviceIdentityManager(state, new FakeCredentialProtector(), transport, clock, Options.Create(new RemoteControlOptions { Enabled = true, DeviceName = "Test PC" }));
            var worker = MakeWorker(transport, state, remotePolicies, manager, parent, policyStore, apps, web, clock);

            await Assert.ThrowsAsync<HttpRequestException>(() => worker.RunCycleAsync());
            var unchanged = (await policyStore.LoadAsync("child"))!;
            Assert.False(unchanged.ParentLock);
            Assert.True(new DeviceTimePolicyEngine(clock).Evaluate(unchanged, TimeSpan.Zero, []).Allowed);

            transport.ThrowOnPoll = false;
            await worker.RunCycleAsync();
            Assert.True((await policyStore.LoadAsync("child"))!.ParentLock);
            Assert.Contains(transport.Acknowledgements, ack => ack.CommandId == command.CommandId && ack.State == RemoteCommandState.Applied);
        });
    }

    [Fact]
    public async Task ExpiredWrongDeviceAndMalformedCommandsFailClosedAndAreAcknowledgedWhenAddressedToDevice()
    {
        await WithDatabase(async db =>
        {
            var deviceId = Guid.NewGuid().ToString();
            var otherDeviceId = Guid.NewGuid().ToString();
            var state = new SqliteRemoteCommandStateStore(db);
            await state.SaveDeviceAsync(new(deviceId, "Test PC", Encoding.UTF8.GetBytes("device-credential-secret-0123456789abcdef"), Now));
            var clock = new FakeClock(Now, TimeZoneInfo.Utc);
            var policyStore = new SqliteDeviceTimePolicyStore(db);
            await policyStore.SaveAsync(Policy());
            var appStore = new SqliteAppPolicyStore(db);
            var webStore = new SqliteWebPolicyStore(db);
            var remoteStore = new SqliteRemotePolicyStore(db);
            var parent = new ParentControlService(policyStore, new SqliteTimeUsageStore(db), clock, new DeviceTimePolicyEngine(clock), new PolicyChangeSignal(), appPolicies: appStore, appPolicyEngine: new AppPolicyEngine(), webPolicies: webStore, remotePolicies: remoteStore);
            var transport = new FakeRemoteTransport();
            transport.Commands.Add(MakeCommand(otherDeviceId, RemoteCommandKind.LockNow, "{}"));
            transport.Commands.Add(MakeCommand(deviceId, RemoteCommandKind.LockNow, "{}") with { ExpiresAtUtc = Now.AddSeconds(-1) });
            transport.Commands.Add(MakeCommand(deviceId, RemoteCommandKind.GrantTime, "{\"minutes\":999}"));
            transport.Commands.Add(MakeCommand(deviceId, RemoteCommandKind.GrantTime, "{\"minutes\":15}") with { Nonce = "bad" });
            transport.Commands.Add(MakeCommand(deviceId, RemoteCommandKind.SyncPolicy, "\"not-a-policy-object\""));
            var manager = new RemoteDeviceIdentityManager(state, new FakeCredentialProtector(), transport, clock, Options.Create(new RemoteControlOptions { Enabled = true, DeviceName = "Test PC" }));
            var worker = MakeWorker(transport, state, remoteStore, manager, parent, policyStore, appStore, webStore, clock);

            await worker.RunCycleAsync();
            Assert.False((await policyStore.LoadAsync("child"))!.ParentLock);
            Assert.Empty(await policyStore.GetGrantsAsync("child"));
            Assert.Contains(transport.Acknowledgements, item => item.State == RemoteCommandState.Expired);
            Assert.Contains(transport.Acknowledgements, item => item.ErrorCode == "INVALID_GRANT");
            Assert.Contains(transport.Acknowledgements, item => item.ErrorCode == "INVALID_NONCE");
            Assert.Contains(transport.Acknowledgements, item => item.ErrorCode == "POLICY_SNAPSHOT_REJECTED");
            Assert.DoesNotContain(transport.Acknowledgements, item => item.DeviceId == otherDeviceId);
        });
    }

    private static RemoteCommandEnvelope MakeCommand() => new(Guid.NewGuid().ToString(), "device-id", "0123456789abcdef", Now, Now, Now.AddMinutes(5), RemoteCommandKind.LockNow, JsonDocument.Parse("{}").RootElement.Clone());
    private static RemoteCommandEnvelope MakeCommand(string deviceId, RemoteCommandKind kind, string payload) => new(Guid.NewGuid().ToString(), deviceId, Guid.NewGuid().ToString("N"), Now, Now, Now.AddMinutes(5), kind, JsonDocument.Parse(payload).RootElement.Clone());
    private static DeviceTimePolicy Policy() => new("child", 7, 30, [new AllowedUsageWindow(Now.DayOfWeek, TimeOnly.MinValue, new TimeOnly(23, 59))], DeviceTimePolicy.DefaultWarnings, false, false, true);

    private static RemoteControlWorker MakeWorker(FakeRemoteTransport transport, SqliteRemoteCommandStateStore state, SqliteRemotePolicyStore remotePolicies, RemoteDeviceIdentityManager identity, ParentControlService parent, IDeviceTimePolicyStore policies, IAppPolicyStore apps, IWebPolicyStore web, FakeClock clock)
    {
        return new RemoteControlWorker(transport, state, remotePolicies, identity, parent, policies, apps, web, clock,
            Options.Create(new WindowsTimeTrackingOptions { ProfileId = "child", SessionId = 7 }),
            Options.Create(new RemoteControlOptions { Enabled = true, SupabaseUrl = "https://example.supabase.co", SupabaseAnonKey = "public", DeviceName = "Test PC", PollIntervalSeconds = 5, StatusIntervalSeconds = 15 }),
            NullLogger<RemoteControlWorker>.Instance);
    }

    private static readonly JsonSerializerOptions RemoteJson = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    private static async Task WithDatabase(Func<SqliteDatabase, Task> body)
    {
        var path = Path.Combine(Path.GetTempPath(), $"tuoitho-remote-{Guid.NewGuid():N}.db");
        try
        {
            var database = new SqliteDatabase(path);
            await database.InitializeAsync();
            await body(database);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private sealed class RemoteMemoryPolicyStore(DeviceTimePolicy initial) : IDeviceTimePolicyStore
    {
        private DeviceTimePolicy policy = initial;
        private readonly List<TemporaryGrant> grants = [];
        public Task<DeviceTimePolicy?> LoadAsync(string profileId, CancellationToken cancellationToken = default) => Task.FromResult<DeviceTimePolicy?>(policy.ProfileId == profileId ? policy : null);
        public Task SaveAsync(DeviceTimePolicy value, CancellationToken cancellationToken = default) { policy = value; return Task.CompletedTask; }
        public Task AddGrantAsync(string profileId, TemporaryGrant grant, CancellationToken cancellationToken = default) { grants.Add(grant); return Task.CompletedTask; }
        public Task ClearGrantsAsync(string profileId, CancellationToken cancellationToken = default) { grants.Clear(); return Task.CompletedTask; }
        public Task<IReadOnlyList<TemporaryGrant>> GetGrantsAsync(string profileId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TemporaryGrant>>(grants.ToArray());
    }

    private sealed class FakeCredentialProtector : IDeviceCredentialProtector
    {
        public byte[] Protect(ReadOnlySpan<byte> secret) => secret.ToArray();
        public byte[] Unprotect(ReadOnlySpan<byte> protectedSecret) => protectedSecret.ToArray();
    }

    private sealed class FakeRemoteTransport : IRemoteTransport
    {
        public List<RemoteCommandEnvelope> Commands { get; } = [];
        public List<RemoteCommandAcknowledgement> Acknowledgements { get; } = [];
        public List<RemotePairingRegistration> Pairings { get; } = [];
        public RemoteDeviceStatus? Status { get; private set; }
        public bool ThrowOnPoll { get; set; }
        public Task RegisterPairingAsync(RemotePairingRegistration pairing, CancellationToken cancellationToken = default) { Pairings.Add(pairing); return Task.CompletedTask; }
        public Task<IReadOnlyList<RemoteCommandEnvelope>> ReceiveCommandsAsync(RemoteDeviceCredential credential, CancellationToken cancellationToken = default)
            => ThrowOnPoll ? Task.FromException<IReadOnlyList<RemoteCommandEnvelope>>(new HttpRequestException("offline")) : Task.FromResult<IReadOnlyList<RemoteCommandEnvelope>>(Commands.ToArray());
        public Task PublishStatusAsync(RemoteDeviceCredential credential, RemoteDeviceStatus status, CancellationToken cancellationToken = default) { Status = status; return Task.CompletedTask; }
        public Task AcknowledgeAsync(RemoteDeviceCredential credential, RemoteCommandAcknowledgement acknowledgement, CancellationToken cancellationToken = default) { Acknowledgements.Add(acknowledgement); return Task.CompletedTask; }
    }

    private sealed class DelegateHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request));
    }
}
