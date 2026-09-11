using System.IO.Pipes;using System.Text.Json;using TuoiTho.Core.Policy;
namespace TuoiTho.SessionAgent;
public sealed class LocalWarningListener(string profileId,int sessionId,ILogger<LocalWarningListener> logger)
{
 private readonly WarningDeduplicator deduplicator=new();
 public async Task ListenOnceAsync(CancellationToken cancellationToken)
 { using var pipe=new NamedPipeServerStream($"TuoiTho.Warning.{sessionId}",PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);await pipe.WaitForConnectionAsync(cancellationToken);var warning=await JsonSerializer.DeserializeAsync<SessionWarning>(pipe,cancellationToken:cancellationToken);if(warning is null||warning.ProfileId!=profileId||warning.SessionId!=sessionId)return;var thresholds=DeviceTimePolicy.DefaultWarnings;deduplicator.ResetAbove(warning.RemainingMinutes,thresholds);foreach(var threshold in deduplicator.GetNewThresholds(warning,thresholds))SessionAgentWarningLog.Warning(logger,$"Còn {threshold} phút sử dụng máy.");if(warning.Reason!=AccessDenyReason.None)SessionAgentWarningLog.Status(logger,PolicyReasonText.ToDisplayText(warning.Reason)); }
}
internal static partial class SessionAgentWarningLog
{
 [LoggerMessage(EventId=2100,Level=LogLevel.Warning,Message="Child warning: {Message}")] public static partial void Warning(ILogger logger,string message);
 [LoggerMessage(EventId=2101,Level=LogLevel.Warning,Message="Child policy status: {Reason}")] public static partial void Status(ILogger logger,string reason);
}