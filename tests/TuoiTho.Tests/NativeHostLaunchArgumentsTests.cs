using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using TuoiTho.BrowserHost;
using TuoiTho.Core.Policy;

namespace TuoiTho.Tests;

public sealed class NativeHostLaunchArgumentsTests
{
    private static readonly BrowserControlConfiguration Config = new("expected-id", "m1-child", 1, "S-1-5-21-1", true);

    [Theory]
    [InlineData("chrome-extension://expected-id/")]
    [InlineData("chrome-extension://expected-id/", "--parent-window=0")]
    [InlineData("chrome-extension://expected-id/", "--parent-window=123456")]
    public void ChromeOrEdgeLaunchArgumentsAreAccepted(params string[] args) => Assert.True(NativeHostLaunchArguments.TryValidate(args, Config, out _));

    [Theory]
    [InlineData("chrome-extension://wrong-id/")]
    [InlineData("chrome-extension://expected-id/", "--parent-window=-1")]
    [InlineData("chrome-extension://expected-id/", "--unexpected")]
    [InlineData("--service-probe")]
    public void WrongOrUnexpectedNativeHostArgumentsAreRejected(params string[] args) => Assert.False(NativeHostLaunchArguments.TryValidate(args, Config, out _));

    [LocalM4ConfigFact]
    public async Task ChromeLikeNativeHostProcessReturnsOnlyFramedResponseWhenLocalM4ConfigExists()
    {
        var config = BrowserControlConfiguration.Load();
        var root = RepositoryRoot();
        var executable = Path.Combine(root, "src", "TuoiTho.BrowserHost", "bin", "Release", "net10.0-windows", "TuoiTho.BrowserHost.exe");
        if (!File.Exists(executable)) return;
        using var process = Process.Start(new ProcessStartInfo(executable)
        {
            Arguments = $"chrome-extension://{config.ExtensionId}/ --parent-window=0",
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        }) ?? throw new InvalidOperationException("BrowserHost did not start.");
        var request = $$"""{"extensionId":"{{config.ExtensionId}}","profileId":"m1-child","managedSessionId":1,"provider":"YouTube","host":"www.youtube.com","path":"/","contentType":"Site"}""";
        var payload = Encoding.UTF8.GetBytes(request);
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await process.StandardInput.BaseStream.WriteAsync(header);
        await process.StandardInput.BaseStream.WriteAsync(payload);
        process.StandardInput.Close();
        var responseHeader = new byte[4]; await process.StandardOutput.BaseStream.ReadExactlyAsync(responseHeader);
        var responseLength = BinaryPrimitives.ReadInt32LittleEndian(responseHeader);
        Assert.InRange(responseLength, 1, BrowserNavigationValidator.MaximumMessageBytes);
        var responsePayload = new byte[responseLength]; await process.StandardOutput.BaseStream.ReadExactlyAsync(responsePayload);
        var json = Encoding.UTF8.GetString(responsePayload);
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.TryGetProperty("allowed", out _));
        Assert.True(document.RootElement.TryGetProperty("reason", out _));
        await process.WaitForExitAsync();
        Assert.NotEqual(2, process.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(await process.StandardError.ReadToEndAsync()));
    }

    private static string RepositoryRoot() => TestRepositoryRoot.Get();
}

public sealed class LocalM4ConfigFactAttribute : FactAttribute
{
    public LocalM4ConfigFactAttribute()
    {
        var path = BrowserControlConfiguration.DefaultPath;
        if (!File.Exists(path))
        {
            Skip = "Local M4 browser config is not installed; live native-host integration is not applicable.";
            return;
        }

        try
        {
            using var stream = File.OpenRead(path);
        }
        catch (UnauthorizedAccessException)
        {
            Skip = "Local M4 browser config exists but is ACL-protected from this test identity.";
        }
    }
}
