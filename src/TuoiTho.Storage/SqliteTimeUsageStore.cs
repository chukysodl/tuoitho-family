using System.Globalization;

using Microsoft.Data.Sqlite;

using TuoiTho.Core.Time;

namespace TuoiTho.Storage;

public sealed class SqliteTimeUsageStore : ITimeUsageStore
{
    private readonly SqliteDatabase database;

    public SqliteTimeUsageStore(SqliteDatabase database)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public async Task<TimeTrackingCheckpoint?> LoadCheckpointAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ValidateProfileId(profileId);

        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT session_id, state, last_observed_at_utc, boot_started_at_utc
            FROM time_tracking_checkpoints
            WHERE profile_id = $profile_id;
            """;
        command.Parameters.AddWithValue("$profile_id", profileId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var stateText = reader.GetString(1);
        if (!Enum.TryParse<SessionActivityState>(stateText, ignoreCase: false, out var state))
        {
            throw new InvalidOperationException("The persisted session state is invalid.");
        }

        return new TimeTrackingCheckpoint(
            reader.GetInt32(0),
            state,
            ParseUtc(reader.GetString(2)),
            ParseUtc(reader.GetString(3))).NormalizeToUtc();
    }

    public async Task SaveAsync(
        string profileId,
        TimeTrackingCheckpoint checkpoint,
        IReadOnlyList<DailyUsageSlice> usageSlices,
        CancellationToken cancellationToken = default)
    {
        ValidateProfileId(profileId);
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(usageSlices);

        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;

        foreach (var usageSlice in usageSlices.Where(slice => slice.ActiveDuration > TimeSpan.Zero))
        {
            command.Parameters.Clear();
            command.CommandText = """
                INSERT INTO time_usage_daily (profile_id, usage_date, active_milliseconds, updated_at_utc)
                VALUES ($profile_id, $usage_date, $active_milliseconds, $updated_at_utc)
                ON CONFLICT(profile_id, usage_date) DO UPDATE SET
                    active_milliseconds = time_usage_daily.active_milliseconds + excluded.active_milliseconds,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            command.Parameters.AddWithValue("$profile_id", profileId);
            command.Parameters.AddWithValue("$usage_date", usageSlice.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$active_milliseconds", ToMilliseconds(usageSlice.ActiveDuration));
            command.Parameters.AddWithValue("$updated_at_utc", checkpoint.LastObservedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var normalizedCheckpoint = checkpoint.NormalizeToUtc();
        command.Parameters.Clear();
        command.CommandText = """
            INSERT INTO time_tracking_checkpoints (
                profile_id, session_id, state, last_observed_at_utc, boot_started_at_utc)
            VALUES (
                $profile_id, $session_id, $state, $last_observed_at_utc, $boot_started_at_utc)
            ON CONFLICT(profile_id) DO UPDATE SET
                session_id = excluded.session_id,
                state = excluded.state,
                last_observed_at_utc = excluded.last_observed_at_utc,
                boot_started_at_utc = excluded.boot_started_at_utc;
            """;
        command.Parameters.AddWithValue("$profile_id", profileId);
        command.Parameters.AddWithValue("$session_id", normalizedCheckpoint.SessionId);
        command.Parameters.AddWithValue("$state", normalizedCheckpoint.State.ToString());
        command.Parameters.AddWithValue("$last_observed_at_utc", normalizedCheckpoint.LastObservedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$boot_started_at_utc", normalizedCheckpoint.BootStartedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<TimeSpan> GetUsageAsync(
        string profileId,
        DateOnly usageDate,
        CancellationToken cancellationToken = default)
    {
        ValidateProfileId(profileId);

        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT active_milliseconds
            FROM time_usage_daily
            WHERE profile_id = $profile_id AND usage_date = $usage_date;
            """;
        command.Parameters.AddWithValue("$profile_id", profileId);
        command.Parameters.AddWithValue("$usage_date", usageDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null || result is DBNull
            ? TimeSpan.Zero
            : TimeSpan.FromMilliseconds(Convert.ToInt64(result, CultureInfo.InvariantCulture));
    }

    private static DateTimeOffset ParseUtc(string value) => DateTimeOffset.Parse(
        value,
        CultureInfo.InvariantCulture,
        DateTimeStyles.RoundtripKind).ToUniversalTime();

    private static long ToMilliseconds(TimeSpan duration) => checked((long)Math.Round(
        duration.TotalMilliseconds,
        MidpointRounding.AwayFromZero));

    private static void ValidateProfileId(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException("A profile ID is required.", nameof(profileId));
        }
    }
}