using Microsoft.Extensions.Logging.Abstractions;
using TuoiTho.Core.Policy;
using TuoiTho.Service;
namespace TuoiTho.Tests;
public sealed class DeviceTimePolicyEngineTests
{
 [Fact] public void ScheduleBoundariesAreInclusiveStartExclusiveEnd(){var c=new FakeClock(Utc(2026,9,14,9,0),TimeZoneInfo.Utc);var e=new DeviceTimePolicyEngine(c);var p=Policy();Assert.True(e.Evaluate(p,TimeSpan.Zero,[]).Allowed);c.UtcNow=Utc(2026,9,14,10,0);Assert.Equal(AccessDenyReason.OutsideSchedule,e.Evaluate(p,TimeSpan.Zero,[]).Reason);}
 [Fact] public void QuotaAndGrantAreEvaluated(){var c=new FakeClock(Utc(2026,9,14,9,0),TimeZoneInfo.Utc);var e=new DeviceTimePolicyEngine(c);Assert.Equal(AccessDenyReason.QuotaExhausted,e.Evaluate(Policy(),TimeSpan.FromMinutes(30),[]).Reason);Assert.True(e.Evaluate(Policy(),TimeSpan.FromMinutes(30),[new TemporaryGrant(15,c.UtcNow.AddMinutes(15))]).Allowed);}
 [Fact] public void WarningThresholdsAndParentOverrideWork(){var c=new FakeClock(Utc(2026,9,14,9,0),TimeZoneInfo.Utc);var e=new DeviceTimePolicyEngine(c);Assert.Equal([15],e.Evaluate(Policy(),TimeSpan.FromMinutes(15),[]).Warnings);Assert.True(e.Evaluate(Policy() with { ParentOverride=true },TimeSpan.FromMinutes(30),[]).Allowed);Assert.Equal(AccessDenyReason.ParentLock,e.Evaluate(Policy() with { ParentLock=true,ParentOverride=true },TimeSpan.Zero,[]).Reason);}
 [Fact] public async Task EnforcementIsIdempotentAndSessionScoped(){var l=new FakeNative();var s=new SafeChildSessionEnforcer(l,NullLogger<SafeChildSessionEnforcer>.Instance);var p=Policy() with { TestMode=false};var d=new PolicyDecision(false,AccessDenyReason.QuotaExhausted,0,[]);Assert.Equal("NOT_TARGETED",(await s.EnforceAsync(p,8,d)).Outcome);Assert.Equal("REAL_SESSION_LOCK",(await s.EnforceAsync(p,7,d)).Outcome);Assert.Equal([7],l.Locks);}
 private static DeviceTimePolicy Policy()=>new("child",7,30,[new AllowedUsageWindow(DayOfWeek.Monday,new TimeOnly(9,0),new TimeOnly(10,0))],DeviceTimePolicy.DefaultWarnings,false,false,true){ManagedUserSid="child"};
 private static DateTimeOffset Utc(int y,int m,int d,int h,int min)=>new(y,m,d,h,min,0,TimeSpan.Zero);
 private sealed class FakeNative:IManagedSessionNativeApi { public List<int> Locks=[];public Task<bool> IsManagedChildSessionAsync(int id,string sid,CancellationToken c=default)=>Task.FromResult(id==7&&sid=="child");public Task DisconnectSessionAsync(int id,CancellationToken c=default){Locks.Add(id);return Task.CompletedTask;} }
}