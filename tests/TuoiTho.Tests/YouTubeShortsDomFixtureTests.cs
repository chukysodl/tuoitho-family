using System.Diagnostics;

namespace TuoiTho.Tests;

public sealed class YouTubeShortsDomFixtureTests
{
    [Fact]
    public async Task CurrentShortOwnerSelectionIsVerifiedAgainstDomFixtures()
    {
        var fixture = Path.Combine(RepositoryRoot(), "tests", "browser-extension", "youtube-shorts-fixtures.test.js");
        var start = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("--test");
        start.ArgumentList.Add(fixture);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Node.js could not start the Shorts DOM fixtures.");
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        Assert.True(process.ExitCode == 0, $"Shorts DOM fixtures failed.{Environment.NewLine}{output}{Environment.NewLine}{error}");
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "TuoiTho.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("TuoiTho.sln was not found.");
    }
}