using System.Text.Json;
using TuoiTho.Core.Policy;

namespace TuoiTho.Tests;

public sealed class BrowserControlConfigurationTests
{
    [Fact]
    public void SharedConfigurationLoadsOnlyCompleteTrustedValues()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tuoitho-browser-{Guid.NewGuid():N}.json");
        try
        {
            var expected = new BrowserControlConfiguration("abcdefghijklmnopabcdefghijklmnop", "m1-child", 7, "S-1-5-21-test", true);
            File.WriteAllText(path, JsonSerializer.Serialize(expected));

            Assert.Equal(expected, BrowserControlConfiguration.Load(path));

            File.WriteAllText(path, "{\"ExtensionId\":\"\",\"ProfileId\":\"m1-child\",\"ManagedSessionId\":7,\"ManagedUserSid\":\"S-1-5-21-test\",\"TestMode\":true}");
            Assert.Throws<InvalidOperationException>(() => BrowserControlConfiguration.Load(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void TestModeFailOpenIsTransientAndAServiceResponseResumesEnforcement()
    {
        var unavailable = BrowserHostAvailabilityPolicy.ServiceUnavailable(testMode: true);
        var afterReconnect = new BrowserNavigationResponse(false, "SITE_BLOCK");
        var production = BrowserHostAvailabilityPolicy.ServiceUnavailable(testMode: false);

        Assert.True(unavailable.Allowed);
        Assert.Equal("Tuổi Thơ chưa kết nối.", unavailable.Diagnostic);
        Assert.False(afterReconnect.Allowed);
        Assert.False(production.Allowed);
    }
}
