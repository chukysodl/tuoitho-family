using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;
using TuoiTho.Service;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("WTS DIAGNOSTIC FAILED: Windows is required.");
    return 2;
}

var sessionId = GetCurrentInteractiveSessionId();
Console.WriteLine($"SessionId: {sessionId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "NONE"}");
if (sessionId is null)
{
    Console.WriteLine("QuerySucceeded: False");
    Console.WriteLine("State: LOGGED_OUT");
    Console.WriteLine("IdleSeconds: N/A");
    Console.WriteLine("CurrentTime: N/A");
    Console.WriteLine("LastInputTime: N/A");
    Console.WriteLine("Win32Error: N/A");
    Console.WriteLine("Diagnostic: No interactive Windows session was detected.");
    return 1;
}

var provider = new WtsSessionActivityProvider(Options.Create(new WindowsTimeTrackingOptions
{
    SessionId = sessionId,
    IdleThresholdMinutes = 5,
    IdlePollIntervalSeconds = 5
}));
var activity = provider.GetActivity(sessionId.Value);

Console.WriteLine($"QuerySucceeded: {activity.QuerySucceeded}");
Console.WriteLine($"State: {activity.State.ToString().ToUpperInvariant()}");
Console.WriteLine($"IdleSeconds: {activity.IdleDuration?.TotalSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) ?? "N/A"}");
Console.WriteLine($"CurrentTime: {activity.CurrentTimeUtc?.ToString("O") ?? "N/A"}");
Console.WriteLine($"LastInputTime: {activity.LastInputTimeUtc?.ToString("O") ?? "N/A"}");
Console.WriteLine($"Win32Error: {activity.Win32Error?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "N/A"}");
Console.WriteLine($"Diagnostic: {activity.Diagnostic ?? "None"}");
try
{
    using var sampler = new TuoiTho.SessionAgent.InteractiveActivitySampler();
    sampler.Start();
    var agentActivity = sampler.GetActivity();
    Console.WriteLine("SessionAgentConnected: direct interactive probe");
    Console.WriteLine($"ProfileId: {Environment.GetEnvironmentVariable("SessionAgent__ProfileId") ?? "local-child"}");
    Console.WriteLine($"AgentSessionId: {sessionId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "NONE"}");
    Console.WriteLine($"ActivitySource: {agentActivity.ActivitySource}");
    Console.WriteLine($"DeviceIdleSeconds: {agentActivity.DeviceIdleSeconds?.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) ?? "N/A"}");
    Console.WriteLine($"WindowsIdleSeconds: {agentActivity.WindowsIdleSeconds?.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) ?? "N/A"}");
    Console.WriteLine($"ActivityState: {(!agentActivity.RawInputAvailable ? "UNKNOWN" : agentActivity.DeviceIdleSeconds < 300 ? "ACTIVE" : "IDLE")}");
    Console.WriteLine($"ActivityDiagnostic: {agentActivity.Diagnostic ?? "None"}");
    Console.WriteLine($"LastActivitySampleUtc: {DateTimeOffset.UtcNow:O}");
    Console.WriteLine("SampleAgeSeconds: 0.0");
}
catch (Exception exception)
{
    Console.WriteLine("SessionAgentConnected: false");
    Console.WriteLine($"ActivityDiagnostic: {exception.Message}");
}
if (activity.Raw is { } raw)
{
    Console.WriteLine($"RawWtsInfoExBytes: {raw.InfoExBytesReturned}");
    Console.WriteLine($"RawWtsInfoExLevel: {raw.InfoExLevel}");
    Console.WriteLine($"RawWtsSessionId: {raw.SessionId}");
    Console.WriteLine($"RawWtsSessionState: {raw.SessionState}");
    Console.WriteLine($"RawWtsSessionFlags: {raw.SessionFlags}");
    Console.WriteLine($"RawInfoExLastInputFileTime: {raw.InfoExLastInputFileTime}");
    Console.WriteLine($"RawInfoExCurrentFileTime: {raw.InfoExCurrentFileTime}");
    Console.WriteLine($"RawSessionInfoBytes: {raw.SessionInfoBytesReturned?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "N/A"}");
    Console.WriteLine($"RawSessionInfoLastInputFileTime: {raw.SessionInfoLastInputFileTime?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "N/A"}");
    Console.WriteLine($"RawSessionInfoCurrentFileTime: {raw.SessionInfoCurrentFileTime?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "N/A"}");
}

return activity.QuerySucceeded && activity.State != TuoiTho.Core.Time.SessionActivityState.Unknown ? 0 : 1;

static int? GetCurrentInteractiveSessionId()
{
    if (ProcessIdToSessionId(Environment.ProcessId, out var processSessionId) && processSessionId > 0)
    {
        return checked((int)processSessionId);
    }

    var consoleSessionId = WTSGetActiveConsoleSessionId();
    return consoleSessionId == uint.MaxValue ? null : checked((int)consoleSessionId);
}

[DllImport("kernel32.dll", SetLastError = true)]
static extern bool ProcessIdToSessionId(int processId, out uint sessionId);

[DllImport("kernel32.dll")]
static extern uint WTSGetActiveConsoleSessionId();