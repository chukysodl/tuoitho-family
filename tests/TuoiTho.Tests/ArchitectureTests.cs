using TuoiTho.Core.Models;
using TuoiTho.Storage;

namespace TuoiTho.Tests;

public sealed class ArchitectureTests
{
    private static readonly string[] ForbiddenCorePrefixes =
    [
        "Microsoft.AspNetCore",
        "Microsoft.Windows",
        "System.Windows",
        "TuoiTho.Parent",
        "TuoiTho.Service",
        "TuoiTho.SessionAgent"
    ];

    [Fact]
    public void CoreDoesNotReferenceWindowsUiOrHostComponents()
    {
        var references = typeof(DeviceIdentity).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(
            references,
            reference => ForbiddenCorePrefixes.Any(prefix =>
                reference.Name?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true));
    }

    [Fact]
    public void StorageDoesNotReferenceUiOrServiceProjects()
    {
        var references = typeof(SqliteDatabase).Assembly.GetReferencedAssemblies();
        var referenceNames = references.Select(reference => reference.Name).ToArray();

        Assert.DoesNotContain("TuoiTho.Parent", referenceNames);
        Assert.DoesNotContain("TuoiTho.Service", referenceNames);
        Assert.DoesNotContain("TuoiTho.SessionAgent", referenceNames);
    }
}
