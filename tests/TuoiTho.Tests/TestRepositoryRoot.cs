namespace TuoiTho.Tests;

internal static class TestRepositoryRoot
{
    public static string Get()
    {
        var configured = Environment.GetEnvironmentVariable("TUOITHO_REPOSITORY_ROOT");
        if (IsRepositoryRoot(configured)) return Path.GetFullPath(configured!);

        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "TuoiTho.sln"))) return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("TuoiTho.sln was not found. Set TUOITHO_REPOSITORY_ROOT to the checked-out repository root.");
    }

    private static bool IsRepositoryRoot(string? path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(path, "TuoiTho.sln"));
}
