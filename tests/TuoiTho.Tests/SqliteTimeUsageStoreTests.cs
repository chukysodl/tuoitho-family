using System.Globalization;

using Microsoft.Data.Sqlite;

using TuoiTho.Core.Time;
using TuoiTho.Storage;

namespace TuoiTho.Tests;

public sealed class SqliteTimeUsageStoreTests
{
    [Fact]
    public async Task SaveCommitsCheckpointAndDailyUsageTogether()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tuoitho-tests", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(directory, "time-accounting.db");

        try
        {
            var database = new SqliteDatabase(databasePath);
            await database.InitializeAsync();
            var store = new SqliteTimeUsageStore(database);
            var checkpoint = new TimeTrackingCheckpoint(
                1,
                SessionActivityState.Active,
                DateTimeOffset.Parse("2026-09-11T09:05:00+00:00", CultureInfo.InvariantCulture),
                DateTimeOffset.Parse("2026-09-11T08:00:00+00:00", CultureInfo.InvariantCulture));

            await store.SaveAsync(
                "child-1",
                checkpoint,
                [
                    new DailyUsageSlice(new DateOnly(2026, 9, 11), TimeSpan.FromMinutes(2)),
                    new DailyUsageSlice(new DateOnly(2026, 9, 11), TimeSpan.FromMinutes(3))
                ]);

            Assert.Equal(TimeSpan.FromMinutes(5), await store.GetUsageAsync("child-1", new DateOnly(2026, 9, 11)));
            Assert.Equal(checkpoint.NormalizeToUtc(), await store.LoadCheckpointAsync("child-1"));
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
}