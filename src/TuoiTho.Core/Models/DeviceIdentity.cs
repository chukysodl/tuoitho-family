namespace TuoiTho.Core.Models;

/// <summary>
/// Stable, local identity for one managed device.
/// </summary>
public sealed record DeviceIdentity
{
    public DeviceIdentity(Guid deviceId, string displayName)
    {
        if (deviceId == Guid.Empty)
        {
            throw new ArgumentException("A device ID is required.", nameof(deviceId));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("A display name is required.", nameof(displayName));
        }

        DeviceId = deviceId;
        DisplayName = displayName.Trim();
    }

    public Guid DeviceId { get; }

    public string DisplayName { get; }
}
