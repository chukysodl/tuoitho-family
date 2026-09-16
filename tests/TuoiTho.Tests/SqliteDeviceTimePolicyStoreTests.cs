using Microsoft.Data.Sqlite;
using TuoiTho.Core.Policy;
using TuoiTho.Storage;
namespace TuoiTho.Tests;
public sealed class SqliteDeviceTimePolicyStoreTests
{
 [Fact]
 public async Task PolicyAndGrantsPersistAcrossDatabaseReopen()
 {
  var path=Path.Combine(Path.GetTempPath(),$"tuoitho-policy-{Guid.NewGuid():N}.db");
  try { var policy=new DeviceTimePolicy("child",7,30,[new AllowedUsageWindow(DayOfWeek.Monday,new(9,0),new(10,0)),new AllowedUsageWindow(DayOfWeek.Monday,new(19,0),new(20,30))],DeviceTimePolicy.DefaultWarnings,false,false,true); var expiry=DateTimeOffset.UtcNow.AddMinutes(15);
   var db=new SqliteDatabase(path);await db.InitializeAsync();var store=new SqliteDeviceTimePolicyStore(db);await store.SaveAsync(policy);await store.AddGrantAsync("child",new TemporaryGrant(15,expiry));
   var reopened=new SqliteDeviceTimePolicyStore(new SqliteDatabase(path));var loaded=await reopened.LoadAsync("child");var grants=await reopened.GetGrantsAsync("child");Assert.NotNull(loaded);Assert.Equal(policy.ProfileId,loaded.ProfileId);Assert.Equal(policy.ManagedSessionId,loaded.ManagedSessionId);Assert.Equal(policy.DailyQuotaMinutes,loaded.DailyQuotaMinutes);Assert.Equal(2,loaded.Windows.Count);Assert.Equal(new TimeOnly(19,0),loaded.Windows[1].Start);Assert.Single(grants);Assert.Equal(15,grants[0].Minutes);
  } finally { SqliteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path); }
 }
}