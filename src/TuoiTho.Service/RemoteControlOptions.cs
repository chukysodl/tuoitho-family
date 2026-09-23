namespace TuoiTho.Service;

public sealed class RemoteControlOptions
{
    public const string SectionName = "RemoteControl";
    public bool Enabled { get; init; }
    public string SupabaseUrl { get; init; } = string.Empty;
    public string SupabaseAnonKey { get; init; } = string.Empty;
    public string DeviceName { get; init; } = Environment.MachineName;
    public string EffectiveDeviceName => string.IsNullOrWhiteSpace(DeviceName) ? Environment.MachineName : DeviceName.Trim();
    public string PairingFunctionName { get; init; } = "device-gateway";
    public string ParentFunctionName { get; init; } = "parent-gateway";
    public int PollIntervalSeconds { get; init; } = 5;
    public int StatusIntervalSeconds { get; init; } = 15;
}
