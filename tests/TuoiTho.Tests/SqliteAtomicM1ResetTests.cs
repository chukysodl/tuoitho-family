using System.Globalization;
using Microsoft.Data.Sqlite;
using TuoiTho.Core.Time;
using TuoiTho.Storage;

namespace TuoiTho.Tests;

public sealed class SqliteAtomicM1ResetTests
{
    [Fact]
    public async Task ResetUsageAndCheckpointIsAtomicAndProfileScoped()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tuoitho-tests", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(directory, "time-accounting.db");
        try
        {
            var database = new SqliteDatabase(databasePath);
            await database.InitializeAsync();
            var store = new SqliteTimeUsageStore(database);
            var date = new DateOnly(2026, 9, 14);
            var old = new TimeTrackingCheckpoint(7, SessionActivityState.Active, DateTimeOffset.Parse("2026-09-14T09:00:00+00:00", CultureInfo.InvariantCulture), DateTimeOffset.Parse("2026-09-14T08:00:00+00:00", CultureInfo.InvariantCulture));
            var baseline = old with { LastObservedAtUtc = DateTimeOffset.Parse("2026-09-14T11:00:00+00:00", CultureInfo.InvariantCulture) };
            await store.SaveAsync("m1-child", old, [new DailyUsageSlice(date, TimeSpan.FromMinutes(120))]);
            await store.SaveAsync("other-child", old, [new DailyUsageSlice(date, TimeSpan.FromMinutes(9))]);

            await store.ResetUsageAndSaveCheckpointAsync("m1-child", date, baseline);

            Assert.Equal(TimeSpan.Zero, await store.GetUsageAsync("m1-child", date));
            Assert.Equal(baseline.NormalizeToUtc(), await store.LoadCheckpointAsync("m1-child"));
            Assert.Equal(TimeSpan.FromMinutes(9), await store.GetUsageAsync("other-child", date));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}