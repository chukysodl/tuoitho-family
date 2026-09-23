using System.Diagnostics;

namespace TuoiTho.Tests;

public sealed class CustomUrlDnrFixtureTests
{
    [Fact]
    public async Task DynamicRuleGenerationIsDeterministicAndFrameScoped()
    {
        var fixture = Path.Combine(RepositoryRoot(), "tests", "browser-extension", "custom-url-dnr.test.cjs");
        var start = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("--test"); start.ArgumentList.Add(fixture);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Node.js could not start the DNR fixtures.");
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        var output = await process.StandardOutput.ReadToEndAsync(); var error = await process.StandardError.ReadToEndAsync();
        Assert.True(process.ExitCode == 0, $"DNR fixtures failed.{Environment.NewLine}{output}{Environment.NewLine}{error}");
    }
    private static string RepositoryRoot() => TestRepositoryRoot.Get();
}
