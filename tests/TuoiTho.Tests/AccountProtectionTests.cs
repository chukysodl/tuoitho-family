using TuoiTho.Core.Security;
using TuoiTho.Parent;

namespace TuoiTho.Tests;

public sealed class AccountProtectionTests
{
    [Fact]
    public void CurrentParentAccountCannotBeDemoted()
    {
        var account = new LocalWindowsAccount(
            "Parent",
            "S-1-5-21-1-1001",
            true,
            true,
            true,
            false);

        var error = AccountProtectionRules.ValidateDemotion(
            account.Sid,
            account,
            2);

        Assert.Contains("phụ huynh", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuiltInAdministratorCannotBecomeChild()
    {
        var account = new LocalWindowsAccount(
            "Administrator",
            "S-1-5-21-1-500",
            true,
            true,
            false,
            true);

        var error = AccountProtectionRules.ValidateDemotion(
            "S-1-5-21-1-1001",
            account,
            2);

        Assert.NotNull(error);
    }

    [Fact]
    public void LastAdministratorIsProtectedFromDemotion()
    {
        var account = new LocalWindowsAccount(
            "Child",
            "S-1-5-21-1-1002",
            true,
            true,
            false,
            false);

        var error = AccountProtectionRules.ValidateDemotion(
            "S-1-5-21-1-1001",
            account,
            1);

        Assert.Contains("Administrator", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StandardChildIsAccepted()
    {
        var account = new LocalWindowsAccount(
            "Child",
            "S-1-5-21-1-1002",
            true,
            false,
            false,
            false);

        Assert.Null(AccountProtectionRules.ValidateDemotion(
            "S-1-5-21-1-1001",
            account,
            1));
    }

    [Fact]
    public void ConfigurationCanBeLoadedFromExplicitPath()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"account-protection-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                path,
                """
                {"Enabled":true,"ManagedChildSid":"S-1-5-21-1-1002","ManagedChildName":"Child"}
                """);

            var value = AccountProtectionConfiguration.TryLoad(path);

            Assert.NotNull(value);
            Assert.Equal("S-1-5-21-1-1002", value!.ManagedChildSid);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
