using System.Globalization;

using Microsoft.Data.Sqlite;

namespace TuoiTho.Storage;

/// <summary>
/// Opens and initializes the local-first SQLite database.
/// </summary>
public sealed class SqliteDatabase
{
    private readonly string connectionString;

    public SqliteDatabase(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("A database path is required.", nameof(databasePath));
        }

        DatabasePath = databasePath.Equals(":memory:", StringComparison.OrdinalIgnoreCase)
            ? databasePath
            : Path.GetFullPath(databasePath);

        var directory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
    }

    public string DatabasePath { get; }

    public static int CurrentSchemaVersion => SqliteSchema.CurrentVersion;

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            connection.Open();
            EnableForeignKeys(connection);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await EnableForeignKeysAsync(connection, cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;

        command.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                migration_id TEXT NOT NULL PRIMARY KEY,
                applied_at_utc TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        foreach (var migration in SqliteSchema.Migrations)
        {
            command.Parameters.Clear();
            command.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE migration_id = $migration_id;";
            command.Parameters.AddWithValue("$migration_id", migration.Id);
            var applied = Convert.ToInt32(
                await command.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture);
            if (applied != 0)
            {
                continue;
            }

            command.Parameters.Clear();
            command.CommandText = migration.Sql;
            await command.ExecuteNonQueryAsync(cancellationToken);

            command.CommandText = """
                INSERT INTO schema_migrations (migration_id, applied_at_utc)
                VALUES ($migration_id, $applied_at_utc);
                """;
            command.Parameters.AddWithValue("$migration_id", migration.Id);
            command.Parameters.AddWithValue("$applied_at_utc", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static void EnableForeignKeys(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        command.ExecuteNonQuery();
    }

    private static async Task EnableForeignKeysAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
