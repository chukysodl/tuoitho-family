using System.Globalization;

using Microsoft.Data.Sqlite;

using TuoiTho.Storage;

namespace TuoiTho.Tests;

public sealed class SqliteDatabaseTests
{
    [Fact]
    public async Task InitializeCreatesSchemaAndIsIdempotent()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tuoitho-tests", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(directory, "family.db");

        try
        {
            var database = new SqliteDatabase(databasePath);

            await database.InitializeAsync();
            await database.InitializeAsync();

            await using (var connection = await database.OpenConnectionAsync())
            {
                Assert.Equal(1, await CountTablesAsync(connection, "schema_migrations"));
                Assert.Equal(1, await CountTablesAsync(connection, "device_config"));
                Assert.Equal(1, await CountTablesAsync(connection, "time_usage_daily"));
                Assert.Equal(1, await CountTablesAsync(connection, "time_tracking_checkpoints"));
            }

            Assert.Equal(2, SqliteDatabase.CurrentSchemaVersion);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task OpenConnectionEnablesForeignKeys()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tuoitho-tests", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(directory, "foreign-keys.db");

        try
        {
            var database = new SqliteDatabase(databasePath);

            await using (var connection = await database.OpenConnectionAsync())
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA foreign_keys;";

                Assert.Equal(1L, await command.ExecuteScalarAsync());
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static async Task<int> CountTablesAsync(SqliteConnection connection, string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $table_name;";
        command.Parameters.AddWithValue("$table_name", tableName);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }
}