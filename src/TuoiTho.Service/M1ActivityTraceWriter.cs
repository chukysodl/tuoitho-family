using System.Globalization;
using TuoiTho.Core.Policy;
using TuoiTho.Core.Time;

namespace TuoiTho.Service;

/// <summary>Writes only aggregate M1 activity telemetry; no input payload is ever written.</summary>
public static class M1ActivityTraceWriter
{
    private const string Header = "Timestamp,RawInputIdleSeconds,WindowsIdleSeconds,ActivityState,RecordedTodaySeconds";

    public static async Task AppendAsync(DeviceTimePolicy policy, SessionActivitySample sample, SessionActivityState state, TimeSpan recordedToday, CancellationToken cancellationToken)
    {
        if (!policy.TestMode || !string.Equals(policy.ProfileId, "m1-child", StringComparison.Ordinal))
        {
            return;
        }

        var path = Path.Combine(Path.GetTempPath(), "tuoitho-m1-activity.csv");
        if (!File.Exists(path))
        {
            await File.WriteAllTextAsync(path, Header + Environment.NewLine, cancellationToken);
        }

        var rawIdle = sample.RawInputAvailable ? sample.IdleSeconds.ToString("F1", CultureInfo.InvariantCulture) : string.Empty;
        var windowsIdle = sample.WindowsIdleSeconds?.ToString("F1", CultureInfo.InvariantCulture) ?? string.Empty;
        var line = string.Join(',', sample.ObservedAtUtc.ToString("O", CultureInfo.InvariantCulture), rawIdle, windowsIdle, state.ToString().ToUpperInvariant(), recordedToday.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture));
        await File.AppendAllTextAsync(path, line + Environment.NewLine, cancellationToken);
    }
}