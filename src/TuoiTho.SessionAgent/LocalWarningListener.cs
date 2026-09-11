using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using TuoiTho.Core.Policy;

namespace TuoiTho.SessionAgent;

public sealed class LocalWarningListener(string profileId, int sessionId, ILogger<LocalWarningListener> logger)
{
    private readonly WarningDeduplicator deduplicator = new();

    public async Task ListenOnceAsync(CancellationToken cancellationToken)
    {
        using var pipe = WarningPipeSecurity.CreateServer(
            $"TuoiTho.Warning.{sessionId}",
            WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Managed user SID is unavailable"));
        await pipe.WaitForConnectionAsync(cancellationToken);
        var warning = await ReadWarningAsync(pipe, cancellationToken);
        if (!Accepts(warning, profileId, sessionId))
        {
            return;
        }

        var acceptedWarning = warning!;
        foreach (var threshold in deduplicator.GetNewThresholds(acceptedWarning.RemainingMinutes, DeviceTimePolicy.DefaultWarnings))
        {
            var text = $"Còn {threshold} phút sử dụng máy.";
            SessionAgentWarningLog.Warning(logger, text);
            _ = Task.Run(() => ChildWarningDialog.Show(text), CancellationToken.None);
        }

        if (acceptedWarning.Reason != AccessDenyReason.None)
        {
            var status = PolicyReasonText.ToDisplayText(acceptedWarning.Reason);
            SessionAgentWarningLog.Status(logger, status);
            _ = Task.Run(() => ChildWarningDialog.Show(status), CancellationToken.None);
        }
    }

    public static bool Accepts(SessionWarning? warning, string expectedProfileId, int expectedSessionId) =>
        warning is not null && warning.ProfileId == expectedProfileId && warning.SessionId == expectedSessionId;

    public static async Task<SessionWarning?> ReadWarningAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<SessionWarning>(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

internal static partial class SessionAgentWarningLog
{
    [LoggerMessage(EventId = 2100, Level = LogLevel.Warning, Message = "Child warning: {Message}")]
    public static partial void Warning(ILogger logger, string message);

    [LoggerMessage(EventId = 2101, Level = LogLevel.Warning, Message = "Child policy status: {Reason}")]
    public static partial void Status(ILogger logger, string reason);
}