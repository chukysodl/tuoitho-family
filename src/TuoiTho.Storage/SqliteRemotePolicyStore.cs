using System.Text.Json;
using Microsoft.Data.Sqlite;
using TuoiTho.Core.Policy;
using TuoiTho.Core.Remote;

namespace TuoiTho.Storage;

/// <summary>Applies an authenticated, validated full policy snapshot atomically for local-first enforcement.</summary>
public sealed class SqliteRemotePolicyStore(SqliteDatabase database) : IRemotePolicyStore
{
    public async Task<long> GetAppliedRevisionAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT applied_revision FROM remote_policy_state WHERE device_id=$device;";
        command.Parameters.AddWithValue("$device", deviceId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? 0 : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<bool> ApplyAsync(RemotePolicySnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (!Validate(snapshot)) return false;
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        long currentRevision;
        DeviceTimePolicy? localPolicy;
        await using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT applied_revision FROM remote_policy_state WHERE device_id=$device;";
            read.Parameters.AddWithValue("$device", snapshot.DeviceId);
            var revision = await read.ExecuteScalarAsync(cancellationToken);
            currentRevision = revision is null or DBNull ? 0 : Convert.ToInt64(revision, System.Globalization.CultureInfo.InvariantCulture);
            read.Parameters.Clear();
            read.CommandText = "SELECT policy_json FROM device_time_policies WHERE profile_id=$profile;";
            read.Parameters.AddWithValue("$profile", snapshot.ProfileId);
            var stored = await read.ExecuteScalarAsync(cancellationToken);
            localPolicy = stored is string json ? JsonSerializer.Deserialize<DeviceTimePolicy>(json) : null;
        }

        // First pairing/remote policy sync must not silently retarget a different profile or session.
        if (snapshot.Revision <= currentRevision || localPolicy is null || localPolicy.ManagedSessionId != snapshot.ManagedSessionId)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var safePolicy = snapshot.TimePolicy with
        {
            ProfileId = localPolicy.ProfileId,
            ManagedSessionId = localPolicy.ManagedSessionId,
            ManagedUserSid = localPolicy.ManagedUserSid,
            ParentLock = localPolicy.ParentLock,
            ParentOverride = localPolicy.ParentOverride,
            TestMode = localPolicy.TestMode
        };
        await using (var profile = connection.CreateCommand())
        {
            profile.Transaction = transaction;
            profile.CommandText = "INSERT OR IGNORE INTO child_profiles(profile_id,display_name,created_at_utc) VALUES($id,$id,$now);";
            profile.Parameters.AddWithValue("$id", snapshot.ProfileId);
            profile.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            await profile.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var saveTime = connection.CreateCommand())
        {
            saveTime.Transaction = transaction;
            saveTime.CommandText = "INSERT INTO device_time_policies(profile_id,policy_json,updated_at_utc) VALUES($id,$json,$now) ON CONFLICT(profile_id) DO UPDATE SET policy_json=excluded.policy_json,updated_at_utc=excluded.updated_at_utc;";
            saveTime.Parameters.AddWithValue("$id", snapshot.ProfileId);
            saveTime.Parameters.AddWithValue("$json", JsonSerializer.Serialize(safePolicy));
            saveTime.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            await saveTime.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var appDefault = connection.CreateCommand())
        {
            appDefault.Transaction = transaction;
            appDefault.CommandText = "INSERT INTO app_policy(profile_id,default_policy,updated_at_utc) VALUES($id,$value,$now) ON CONFLICT(profile_id) DO UPDATE SET default_policy=excluded.default_policy,updated_at_utc=excluded.updated_at_utc;";
            appDefault.Parameters.AddWithValue("$id", snapshot.ProfileId);
            appDefault.Parameters.AddWithValue("$value", snapshot.DefaultAppPolicy.ToString());
            appDefault.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            await appDefault.ExecuteNonQueryAsync(cancellationToken);
        }
        await DeleteForProfileAsync(connection, transaction, "app_rules", "profile_id", snapshot.ProfileId, cancellationToken);
        foreach (var rule in snapshot.AppRules)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO app_rules(rule_id,profile_id,executable_identity,decision,identity_json,enabled,daily_quota_minutes,schedule_json) VALUES($rid,$pid,$key,$decision,$identity,$enabled,$quota,$schedule);";
            insert.Parameters.AddWithValue("$rid", AppIdentity.StableId(snapshot.ProfileId, rule.Identity.RuleKey));
            insert.Parameters.AddWithValue("$pid", snapshot.ProfileId);
            insert.Parameters.AddWithValue("$key", rule.Identity.RuleKey);
            insert.Parameters.AddWithValue("$decision", rule.Decision.ToString());
            insert.Parameters.AddWithValue("$identity", JsonSerializer.Serialize(rule.Identity));
            insert.Parameters.AddWithValue("$enabled", rule.Enabled ? 1 : 0);
            insert.Parameters.AddWithValue("$quota", (object?)rule.DailyQuotaMinutes ?? DBNull.Value);
            insert.Parameters.AddWithValue("$schedule", (object?)rule.Schedule ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await DeleteForProfileAsync(connection, transaction, "browser_content_rules", "profile_id", snapshot.ProfileId, cancellationToken);
        foreach (var rule in snapshot.WebRules)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO browser_content_rules(profile_id,provider,scope,normalized_key,display_label,decision,updated_at_utc) VALUES($id,$provider,$scope,$key,$label,$decision,$now);";
            insert.Parameters.AddWithValue("$id", snapshot.ProfileId);
            insert.Parameters.AddWithValue("$provider", rule.Provider.ToString());
            insert.Parameters.AddWithValue("$scope", rule.Scope.ToString());
            insert.Parameters.AddWithValue("$key", rule.NormalizedKey);
            insert.Parameters.AddWithValue("$label", rule.DisplayLabel);
            insert.Parameters.AddWithValue("$decision", rule.Decision.ToString());
            insert.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        var now = DateTimeOffset.UtcNow.ToString("O");
        await using (var browserRevision = connection.CreateCommand())
        {
            browserRevision.Transaction = transaction;
            browserRevision.CommandText = "INSERT INTO browser_policy_revisions(profile_id,revision,updated_at_utc) VALUES($id,1,$now) ON CONFLICT(profile_id) DO UPDATE SET revision=revision+1,updated_at_utc=excluded.updated_at_utc;";
            browserRevision.Parameters.AddWithValue("$id", snapshot.ProfileId);
            browserRevision.Parameters.AddWithValue("$now", now);
            await browserRevision.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var remoteRevision = connection.CreateCommand())
        {
            remoteRevision.Transaction = transaction;
            remoteRevision.CommandText = "INSERT INTO remote_policy_state(device_id,applied_revision,applied_at_utc) VALUES($device,$revision,$now) ON CONFLICT(device_id) DO UPDATE SET applied_revision=excluded.applied_revision,applied_at_utc=excluded.applied_at_utc;";
            remoteRevision.Parameters.AddWithValue("$device", snapshot.DeviceId);
            remoteRevision.Parameters.AddWithValue("$revision", snapshot.Revision);
            remoteRevision.Parameters.AddWithValue("$now", now);
            await remoteRevision.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static bool Validate(RemotePolicySnapshot? snapshot)
    {
        if (snapshot is null || snapshot.TimePolicy is null || snapshot.AppRules is null || snapshot.WebRules is null || snapshot.TimePolicy.Windows is null || snapshot.TimePolicy.WarningThresholdMinutes is null) return false;
        if (!Guid.TryParse(snapshot.DeviceId, out _) || string.IsNullOrWhiteSpace(snapshot.ProfileId) || snapshot.ProfileId.Length > 128 || snapshot.ManagedSessionId < 0 || snapshot.Revision <= 0) return false;
        if (snapshot.TimePolicy.ProfileId != snapshot.ProfileId || snapshot.TimePolicy.ManagedSessionId != snapshot.ManagedSessionId || snapshot.TimePolicy.DailyQuotaMinutes is < 1 or > 1440) return false;
        if (WeeklyScheduleValidator.Validate(snapshot.TimePolicy.Windows) is not null || !Enum.IsDefined(snapshot.DefaultAppPolicy) || snapshot.TimePolicy.WarningThresholdMinutes.Count > 10 || snapshot.TimePolicy.WarningThresholdMinutes.Any(value => value is < 1 or > 1440)) return false;
        if (snapshot.AppRules.Count > 2000 || snapshot.WebRules.Count > 2000) return false;
        if (snapshot.AppRules.Any(rule => rule is null || rule.Identity is null || rule.ProfileId != snapshot.ProfileId || !Enum.IsDefined(rule.Decision) || string.IsNullOrWhiteSpace(rule.Identity.NormalizedExecutablePath) || rule.Identity.NormalizedExecutablePath.Length > 2048 || string.IsNullOrWhiteSpace(rule.Identity.FileName) || rule.Identity.FileName.Length > 260 || rule.Identity.DisplayName?.Length > 512 || rule.Identity.Publisher?.Length > 512 || rule.Identity.ProductName?.Length > 512 || rule.Schedule?.Length > 512)) return false;
        if (snapshot.AppRules.Select(rule => rule.Identity.RuleKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() != snapshot.AppRules.Count) return false;
        if (snapshot.WebRules.Any(rule => rule is null || rule.ProfileId != snapshot.ProfileId || !Enum.IsDefined(rule.Provider) || !Enum.IsDefined(rule.Scope) || !Enum.IsDefined(rule.Decision) || !ValidWebRule(rule))) return false;
        return snapshot.WebRules.Select(rule => $"{rule.Provider}:{rule.Scope}:{rule.NormalizedKey}").Distinct(StringComparer.OrdinalIgnoreCase).Count() == snapshot.WebRules.Count;
    }

    private static bool ValidWebRule(WebRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.NormalizedKey) || rule.NormalizedKey.Length > 512 || string.IsNullOrWhiteSpace(rule.DisplayLabel) || rule.DisplayLabel.Length > 512) return false;
        if (rule.Provider == BrowserProvider.GenericWeb)
            return WebIdentityNormalizer.TryNormalizeCustomWebsite(rule.DisplayLabel, rule.Scope, out var custom) && custom.NormalizedKey == rule.NormalizedKey;
        if (rule.Scope == WebRuleScope.Site) return rule.NormalizedKey == WebPolicyEngine.SiteKey(rule.Provider);
        if (rule.Provider == BrowserProvider.YouTube)
        {
            if (rule.NormalizedKey.StartsWith("id:", StringComparison.Ordinal)) return WebIdentityNormalizer.IsChannelId(rule.NormalizedKey[3..]);
            return WebIdentityNormalizer.TryNormalizeYouTubeChannel(rule.DisplayLabel, out var normalized, out _) && normalized == rule.NormalizedKey;
        }
        return rule.Provider == BrowserProvider.TikTok && WebIdentityNormalizer.TryNormalizeTikTokCreator(rule.DisplayLabel, out var creator, out _) && creator == rule.NormalizedKey;
    }

    private static async Task DeleteForProfileAsync(SqliteConnection connection, SqliteTransaction transaction, string table, string profileColumn, string profileId, CancellationToken token)
    {
        // Table/column values are internal constants only.
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DELETE FROM {table} WHERE {profileColumn}=$profile;";
        command.Parameters.AddWithValue("$profile", profileId);
        await command.ExecuteNonQueryAsync(token);
    }
}
