using System.Globalization;
using Microsoft.Data.Sqlite;
using TuoiTho.Storage;

namespace TuoiTho.Tests;

public sealed class SqliteWebPolicyMigrationRepairTests
{
    [Fact]
    public async Task InitializeRepairsARecordedBrowserMigrationThatLacksItsTable()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tuoitho-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "repair.db");
        try
        {
            Directory.CreateDirectory(directory);
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE schema_migrations (migration_id TEXT NOT NULL PRIMARY KEY, applied_at_utc TEXT NOT NULL); INSERT INTO schema_migrations (migration_id, applied_at_utc) VALUES ('0006-browser-content-policy', $at);";
                command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
                await command.ExecuteNonQueryAsync();
            }

            var database = new SqliteDatabase(path);
            await database.InitializeAsync();
            await using var verified = await database.OpenConnectionAsync();
            await using var query = verified.CreateCommand();
            query.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'browser_content_rules';";
            Assert.Equal(1, Convert.ToInt32(await query.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
