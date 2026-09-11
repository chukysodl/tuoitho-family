using TuoiTho.Core.Models;

namespace TuoiTho.Tests;

public sealed class DeviceIdentityTests
{
    [Fact]
    public void ConstructorTrimsDisplayNameAndPreservesId()
    {
        var deviceId = Guid.NewGuid();

        var identity = new DeviceIdentity(deviceId, "  Child laptop  ");

        Assert.Equal(deviceId, identity.DeviceId);
        Assert.Equal("Child laptop", identity.DisplayName);
    }

    [Fact]
    public void ConstructorRejectsEmptyDeviceId()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new DeviceIdentity(Guid.Empty, "Child laptop"));

        Assert.Equal("deviceId", exception.ParamName);
    }
}
