using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using TuoiTho.Core.Policy;
namespace TuoiTho.SessionAgent;
public interface IChildWarningSink { void Show(string text); }
public sealed class DialogWarningSink : IChildWarningSink { public void Show(string text) => ChildWarningDialog.Show(text); }
public sealed class LocalWarningListener(string profileId,int sessionId,IChildWarningSink sink,ILogger<LocalWarningListener> logger, SecurityIdentifier? publisherSid = null)
{
 private readonly WarningDeduplicator deduplicator=new();
 private AccessDenyReason lastReason=AccessDenyReason.None;
 public async Task ListenOnceAsync(CancellationToken cancellationToken)
 {
  using var pipe=WarningPipeSecurity.CreateServer($"TuoiTho.Warning.{sessionId}",WindowsIdentity.GetCurrent().User??throw new InvalidOperationException("Managed user SID is unavailable"),publisherSid);
  await pipe.WaitForConnectionAsync(cancellationToken);var warning=await ReadWarningAsync(pipe,cancellationToken);if(!Accepts(warning,profileId,sessionId))return;var accepted=warning!;
  foreach(var threshold in deduplicator.GetNewThresholds(accepted.RemainingMinutes,DeviceTimePolicy.DefaultWarnings)){var text=$"Còn {threshold} phút sử dụng máy.";SessionAgentWarningLog.Warning(logger,text);sink.Show(text);}
  if(accepted.Reason!=AccessDenyReason.None&&accepted.Reason!=lastReason){var status=PolicyReasonText.ToDisplayText(accepted.Reason);SessionAgentWarningLog.Status(logger,status);sink.Show(status);}lastReason=accepted.Reason;
 }
 public static bool Accepts(SessionWarning? warning,string expectedProfileId,int expectedSessionId)=>warning is not null&&warning.ProfileId==expectedProfileId&&warning.SessionId==expectedSessionId;
 public static async Task<SessionWarning?> ReadWarningAsync(Stream stream,CancellationToken cancellationToken){try{return await JsonSerializer.DeserializeAsync<SessionWarning>(stream,cancellationToken:cancellationToken);}catch(JsonException){return null;}}
}
internal static partial class SessionAgentWarningLog{[LoggerMessage(EventId=2100,Level=LogLevel.Warning,Message="Child warning: {Message}")]public static partial void Warning(ILogger logger,string message);[LoggerMessage(EventId=2101,Level=LogLevel.Warning,Message="Child policy status: {Reason}")]public static partial void Status(ILogger logger,string reason);}