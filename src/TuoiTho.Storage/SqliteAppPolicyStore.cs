using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TuoiTho.Core.Policy;

namespace TuoiTho.Storage;

public sealed class SqliteAppPolicyStore(SqliteDatabase database) : IAppPolicyStore
{
    public async Task<DefaultAppPolicy> GetDefaultPolicyAsync(string profileId, CancellationToken cancellationToken = default)
    {
        await using var c = await database.OpenConnectionAsync(cancellationToken);
        await using var q = c.CreateCommand();
        q.CommandText = "SELECT default_policy FROM app_policy WHERE profile_id=$id";
        q.Parameters.AddWithValue("$id", profileId);
        var value = await q.ExecuteScalarAsync(cancellationToken);
        return value is string text && Enum.TryParse<DefaultAppPolicy>(text, out var policy) ? policy : DefaultAppPolicy.BlockUnknown;
    }

    public async Task SetDefaultPolicyAsync(string profileId, DefaultAppPolicy policy, CancellationToken cancellationToken = default)
    {
        await using var c = await database.OpenConnectionAsync(cancellationToken);
        await Ensure(c, profileId, cancellationToken);
        await using var q = c.CreateCommand();
        q.CommandText = "INSERT INTO app_policy(profile_id,default_policy,updated_at_utc) VALUES($id,$p,$now) ON CONFLICT(profile_id) DO UPDATE SET default_policy=excluded.default_policy,updated_at_utc=excluded.updated_at_utc";
        q.Parameters.AddWithValue("$id", profileId); q.Parameters.AddWithValue("$p", policy.ToString()); q.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await q.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AppRule>> GetRulesAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var list = new List<AppRule>();
        await using var c = await database.OpenConnectionAsync(cancellationToken);
        await using var q = c.CreateCommand();
        q.CommandText = "SELECT identity_json,decision,enabled,daily_quota_minutes,schedule_json FROM app_rules WHERE profile_id=$id AND identity_json IS NOT NULL";
        q.Parameters.AddWithValue("$id", profileId);
        await using var r = await q.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            var id = JsonSerializer.Deserialize<AppIdentity>(r.GetString(0));
            if (id is not null && Enum.TryParse<AppRuleDecision>(r.GetString(1), out var decision)) list.Add(new(profileId, id, decision, r.GetInt64(2) != 0, r.IsDBNull(3) ? null : r.GetInt32(3), r.IsDBNull(4) ? null : r.GetString(4)));
        }
        return list;
    }

    public async Task SaveRuleAsync(AppRule rule, CancellationToken cancellationToken = default)
    {
        await using var c = await database.OpenConnectionAsync(cancellationToken);
        await Ensure(c, rule.ProfileId, cancellationToken);
        await using var q = c.CreateCommand();
        q.CommandText = "INSERT INTO app_rules(rule_id,profile_id,executable_identity,decision,identity_json,enabled,daily_quota_minutes,schedule_json) VALUES($rid,$pid,$key,$d,$i,$e,$q,$s) ON CONFLICT(rule_id) DO UPDATE SET decision=excluded.decision,identity_json=excluded.identity_json,enabled=excluded.enabled,daily_quota_minutes=excluded.daily_quota_minutes,schedule_json=excluded.schedule_json";
        q.Parameters.AddWithValue("$rid", AppIdentity.StableId(rule.ProfileId, rule.Identity.RuleKey)); q.Parameters.AddWithValue("$pid", rule.ProfileId); q.Parameters.AddWithValue("$key", rule.Identity.RuleKey); q.Parameters.AddWithValue("$d", rule.Decision.ToString()); q.Parameters.AddWithValue("$i", JsonSerializer.Serialize(rule.Identity)); q.Parameters.AddWithValue("$e", rule.Enabled ? 1 : 0); q.Parameters.AddWithValue("$q", (object?)rule.DailyQuotaMinutes ?? DBNull.Value); q.Parameters.AddWithValue("$s", (object?)rule.Schedule ?? DBNull.Value);
        await q.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RemoveRuleAsync(string profileId, AppIdentity identity, CancellationToken cancellationToken = default)
    {
        await using var c = await database.OpenConnectionAsync(cancellationToken);
        await using var q = c.CreateCommand();
        q.CommandText = "DELETE FROM app_rules WHERE rule_id=$id";
        q.Parameters.AddWithValue("$id", AppIdentity.StableId(profileId, identity.RuleKey));
        await q.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ObservedApp>> GetObservedAppsAsync(string profileId, int sessionId, CancellationToken cancellationToken = default)
    {
        var list = new List<ObservedApp>();
        await using var c = await database.OpenConnectionAsync(cancellationToken);
        await using var q = c.CreateCommand();
        q.CommandText = "SELECT identity_json,first_seen_at_utc,last_seen_at_utc,classification FROM observed_apps WHERE profile_id=$p AND session_id=$s ORDER BY last_seen_at_utc DESC";
        q.Parameters.AddWithValue("$p", profileId); q.Parameters.AddWithValue("$s", sessionId);
        await using var r = await q.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            var identity = JsonSerializer.Deserialize<AppIdentity>(r.GetString(0));
            if (identity is not null) list.Add(new(profileId, sessionId, identity, DateTimeOffset.Parse(r.GetString(1), CultureInfo.InvariantCulture), DateTimeOffset.Parse(r.GetString(2), CultureInfo.InvariantCulture), r.IsDBNull(3) || !Enum.TryParse<AppClassification>(r.GetString(3), out var classification) ? AppClassification.BackgroundHelper : classification));
        }
        // Existing databases can contain keys whose only difference is Windows path casing.
        return list.GroupBy(app => app.Identity.NormalizedExecutablePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(app => app.LastSeenUtc).First()).ToArray();
    }

    public async Task RecordObservationAsync(ObservedApp observation, CancellationToken cancellationToken = default)
    {
        await using var c = await database.OpenConnectionAsync(cancellationToken);
        await Ensure(c, observation.ProfileId, cancellationToken);
        await using var transaction = c.BeginTransaction();
        var updated = await UpdateObservationAsync(c, transaction, observation, cancellationToken);
        if (updated == 0) await InsertObservationAsync(c, transaction, observation, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<int> UpdateObservationAsync(SqliteConnection connection, SqliteTransaction transaction, ObservedApp observation, CancellationToken token)
    {
        await using var q = connection.CreateCommand(); q.Transaction = transaction;
        q.CommandText = "UPDATE observed_apps SET session_id=$s,identity_json=$i,last_seen_at_utc=$l,classification=$c WHERE profile_id=$p AND normalized_executable_path=$x COLLATE NOCASE";
        AddObservationParameters(q, observation);
        return await q.ExecuteNonQueryAsync(token);
    }

    private static async Task InsertObservationAsync(SqliteConnection connection, SqliteTransaction transaction, ObservedApp observation, CancellationToken token)
    {
        await using var q = connection.CreateCommand(); q.Transaction = transaction;
        q.CommandText = "INSERT INTO observed_apps(profile_id,normalized_executable_path,session_id,identity_json,first_seen_at_utc,last_seen_at_utc,classification) VALUES($p,$x,$s,$i,$f,$l,$c)";
        AddObservationParameters(q, observation);
        await q.ExecuteNonQueryAsync(token);
    }

    private static void AddObservationParameters(SqliteCommand command, ObservedApp observation)
    {
        command.Parameters.AddWithValue("$p", observation.ProfileId); command.Parameters.AddWithValue("$x", observation.Identity.NormalizedExecutablePath); command.Parameters.AddWithValue("$s", observation.SessionId); command.Parameters.AddWithValue("$i", JsonSerializer.Serialize(observation.Identity)); command.Parameters.AddWithValue("$f", observation.FirstSeenUtc.ToUniversalTime().ToString("O")); command.Parameters.AddWithValue("$l", observation.LastSeenUtc.ToUniversalTime().ToString("O")); command.Parameters.AddWithValue("$c", observation.Classification.ToString());
    }

    private static async Task Ensure(SqliteConnection c, string id, CancellationToken t)
    {
        await using var q = c.CreateCommand(); q.CommandText = "INSERT OR IGNORE INTO child_profiles(profile_id,display_name,created_at_utc) VALUES($id,$id,$n)"; q.Parameters.AddWithValue("$id", id); q.Parameters.AddWithValue("$n", DateTimeOffset.UtcNow.ToString("O")); await q.ExecuteNonQueryAsync(t);
    }
}