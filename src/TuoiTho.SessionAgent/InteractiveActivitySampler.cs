using System.Runtime.InteropServices;

namespace TuoiTho.SessionAgent;

public sealed record InteractiveActivitySnapshot(bool RawInputAvailable, double? DeviceIdleSeconds, double? WindowsIdleSeconds, string? Diagnostic)
{
    public string ActivitySource => RawInputAvailable ? "RAW_INPUT" : "UNAVAILABLE";
}

public interface IInteractiveActivitySampler : IDisposable
{
    void Start();
    InteractiveActivitySnapshot GetActivity();
}

public interface IWindowsIdleTimeDiagnostics
{
    double? GetIdleSeconds();
}

/// <summary>Samples only aggregate idle durations; Raw Input is authoritative for accounting.</summary>
public sealed class InteractiveActivitySampler(
    IRawInputActivityTracker rawInput,
    IWindowsIdleTimeDiagnostics windowsIdle,
    TimeProvider timeProvider) : IInteractiveActivitySampler
{
    public InteractiveActivitySampler() : this(new RawInputActivityTracker(), new WindowsIdleTimeDiagnostics(), TimeProvider.System) { }

    public void Start() => rawInput.Start();

    public InteractiveActivitySnapshot GetActivity()
    {
        var status = rawInput.GetStatus();
        var windowsIdleSeconds = windowsIdle.GetIdleSeconds();
        if (!status.IsAvailable || status.LastDeviceInputUtc is not { } lastInput)
        {
            return new(false, null, windowsIdleSeconds, status.Diagnostic ?? "Raw Input is unavailable.");
        }

        var deviceIdle = Math.Max(0, (timeProvider.GetUtcNow() - lastInput).TotalSeconds);
        return new(true, deviceIdle, windowsIdleSeconds, null);
    }

    public void Dispose() => rawInput.Dispose();
}

/// <summary>Diagnostic-only GetLastInputInfo comparison; never used for accounting state.</summary>
public sealed class WindowsIdleTimeDiagnostics : IWindowsIdleTimeDiagnostics
{
    public double? GetIdleSeconds()
    {
        var input = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref input))
        {
            return null;
        }

        var elapsedMilliseconds = unchecked((uint)Environment.TickCount) - input.TickCount;
        return TimeSpan.FromMilliseconds(elapsedMilliseconds).TotalSeconds;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetLastInputInfo(ref LastInputInfo lastInputInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint TickCount;
    }
}