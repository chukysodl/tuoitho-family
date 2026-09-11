namespace TuoiTho.Service;

public sealed class WindowsTimeTrackingOptions
{
    public const string SectionName = "TimeTracking";

    public string ProfileId { get; set; } = "local-child";

    public int? SessionId { get; set; }

    public int IdleThresholdMinutes { get; set; } = 5;

    public int IdlePollIntervalSeconds { get; set; } = 15;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ProfileId))
        {
            throw new InvalidOperationException("TimeTracking:ProfileId is required.");
        }

        if (SessionId is < 0)
        {
            throw new InvalidOperationException("TimeTracking:SessionId cannot be negative.");
        }

        if (IdleThresholdMinutes <= 0)
        {
            throw new InvalidOperationException("TimeTracking:IdleThresholdMinutes must be positive.");
        }

        if (IdlePollIntervalSeconds <= 0)
        {
            throw new InvalidOperationException("TimeTracking:IdlePollIntervalSeconds must be positive.");
        }
    }
}