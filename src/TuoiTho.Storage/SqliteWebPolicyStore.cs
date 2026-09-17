using Microsoft.Data.Sqlite;
using TuoiTho.Core.Policy;

namespace TuoiTho.Storage;

public sealed class SqliteWebPolicyStore(SqliteDatabase database) : IWebPolicyStore
{
    public async Task<IReadOnlyList<WebRule>> GetRulesAsync(string profileId, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        return await ReadRulesAsync(connection, profileId, cancellationToken);
    }

    public async Task<WebPolicySnapshot> GetSnapshotAsync(string profileId, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        var rules = await ReadRulesAsync(connection, profileId, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT revision FROM browser_policy_revisions WHERE profile_id=$id;";
        command.Parameters.AddWithValue("$id", profileId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        var revision = value is null || value == DBNull.Value ? 0L : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        return new(revision, rules);
    }

    public async Task SaveRuleAsync(WebRule rule, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await EnsureProfileAsync(connection, transaction, rule.ProfileId, cancellationToken);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO browser_content_rules(profile_id,provider,scope,normalized_key,display_label,decision,updated_at_utc)
                VALUES($id,$provider,$scope,$key,$label,$decision,$now)
                ON CONFLICT(profile_id,provider,scope,normalized_key) DO UPDATE SET
                    display_label=excluded.display_label, decision=excluded.decision, updated_at_utc=excluded.updated_at_utc;
                """;
            command.Parameters.AddWithValue("$id", rule.ProfileId);
            command.Parameters.AddWithValue("$provider", rule.Provider.ToString());
            command.Parameters.AddWithValue("$scope", rule.Scope.ToString());
            command.Parameters.AddWithValue("$key", rule.NormalizedKey);
            command.Parameters.AddWithValue("$label", rule.DisplayLabel);
            command.Parameters.AddWithValue("$decision", rule.Decision.ToString());
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await IncrementRevisionAsync(connection, transaction, rule.ProfileId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RemoveRuleAsync(string profileId, BrowserProvider provider, WebRuleScope scope, string normalizedKey, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM browser_content_rules WHERE profile_id=$id AND provider=$provider AND scope=$scope AND normalized_key=$key;";
        command.Parameters.AddWithValue("$id", profileId);
        command.Parameters.AddWithValue("$provider", provider.ToString());
        command.Parameters.AddWithValue("$scope", scope.ToString());
        command.Parameters.AddWithValue("$key", normalizedKey);
        var deleted = await command.ExecuteNonQueryAsync(cancellationToken);
        if (deleted != 0) await IncrementRevisionAsync(connection, transaction, profileId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<WebRule>> ReadRulesAsync(SqliteConnection connection, string profileId, CancellationToken token)
    {
        var result = new List<WebRule>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT provider,scope,decision,normalized_key,display_label FROM browser_content_rules WHERE profile_id=$id ORDER BY provider,scope,normalized_key;";
        command.Parameters.AddWithValue("$id", profileId);
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(new(profileId, Enum.Parse<BrowserProvider>(reader.GetString(0)), Enum.Parse<WebRuleScope>(reader.GetString(1)), Enum.Parse<WebRuleDecision>(reader.GetString(2)), reader.GetString(3), reader.GetString(4)));
        return result;
    }

    private static async Task EnsureProfileAsync(SqliteConnection connection, SqliteTransaction transaction, string profileId, CancellationToken token)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT OR IGNORE INTO child_profiles(profile_id,display_name,created_at_utc) VALUES($id,$id,$now);";
        command.Parameters.AddWithValue("$id", profileId); command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task IncrementRevisionAsync(SqliteConnection connection, SqliteTransaction transaction, string profileId, CancellationToken token)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO browser_policy_revisions(profile_id,revision,updated_at_utc) VALUES($id,1,$now) ON CONFLICT(profile_id) DO UPDATE SET revision=revision+1,updated_at_utc=excluded.updated_at_utc;";
        command.Parameters.AddWithValue("$id", profileId); command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(token);
    }
}