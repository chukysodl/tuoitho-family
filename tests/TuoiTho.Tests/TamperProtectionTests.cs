using TuoiTho.Core.Security;
using TuoiTho.Service;

namespace TuoiTho.Tests;

public sealed class TamperProtectionTests
{
    [Fact]
    public void HardenedServiceAclKeepsSystemFullButRemovesAdministratorFullControl()
    {
        var sddl = WindowsTamperProtectionPlatform.HardenedServiceSddl;

        Assert.Contains("(A;;GA;;;SY)", sddl, StringComparison.Ordinal);
        Assert.Contains("(A;;GRRP;;;BA)", sddl, StringComparison.Ordinal);
        Assert.DoesNotContain("(A;;GA;;;BA)", sddl, StringComparison.Ordinal);
    }

    [Fact]
    public void MaintenanceServiceAclRestoresAdministratorFullControlTemporarily()
    {
        var sddl = WindowsTamperProtectionPlatform.MaintenanceServiceSddl;

        Assert.Contains("(A;;GA;;;SY)", sddl, StringComparison.Ordinal);
        Assert.Contains("(A;;GA;;;BA)", sddl, StringComparison.Ordinal);
    }

    [Fact]
    public void MaintenanceProtocolHasBoundedWindow()
    {
        Assert.True(TamperMaintenanceProtocol.MinimumDurationSeconds >= 30);
        Assert.True(TamperMaintenanceProtocol.MaximumDurationSeconds <= 600);
        Assert.InRange(
            TamperMaintenanceProtocol.DefaultDurationSeconds,
            TamperMaintenanceProtocol.MinimumDurationSeconds,
            TamperMaintenanceProtocol.MaximumDurationSeconds);
    }
}
