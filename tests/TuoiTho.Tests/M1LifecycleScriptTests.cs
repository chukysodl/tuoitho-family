namespace TuoiTho.Tests;

public sealed class M1LifecycleScriptTests
{
    private static string Script(string name) => File.ReadAllText(Path.Combine(TestRepositoryRoot.Get(), "scripts", name));

    [Fact]
    public void StartReplacesOnlyTrackedM1ComponentsAndWaitsForCurrentParentProtocol()
    {
        var script = Script("M1-START.ps1");
        Assert.Contains("M1-STOP.ps1') -Quiet", script);
        Assert.Contains("Test-M1ParentServiceReady", script);
        Assert.Contains("Action = 5", script); // GetStatus is the Parent/Service readiness protocol.
        Assert.Contains("Parent Service did not become ready with the current protocol", script);
        Assert.Contains("Components", script);
    }

    [Fact]
    public void StartSupportsPersistentRealAndTestModes()
    {
        var script = Script("M1-START.ps1");
        Assert.Contains("run-mode.txt", script);
        Assert.Contains("M1Bootstrap__TestMode", script);
        Assert.Contains("SessionAgent__M1TestMode", script);
        Assert.Contains("Mode={3}", script);
    }

    [Fact]
    public void RealModeStartsEnforcementAsLocalSystemService()
    {
        var script = Script("M1-START.ps1");
        Assert.Contains("runtime-control.json", script);
        Assert.Contains("ServiceHost=LocalSystem", script);
        Assert.Contains("sc.exe create", script);
        Assert.Contains("obj= LocalSystem", script);
        Assert.Contains("sc.exe start", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RootModeSwitchScriptsPersistRealAndTestSelections()
    {
        var root = TestRepositoryRoot.Get();
        var real = File.ReadAllText(Path.Combine(root, "TUOITHO-REAL-MODE.cmd"));
        var test = File.ReadAllText(Path.Combine(root, "TUOITHO-TEST-MODE.cmd"));
        Assert.Contains("run-mode.txt", real);
        Assert.Contains("echo REAL", real);
        Assert.Contains("choice /C YN", real);
        Assert.Contains("run-mode.txt", test);
        Assert.Contains("echo TEST", test);
    }

    [Fact]
    public void StopVerifiesExecutablePathsAndNeverUsesBroadProcessTermination()
    {
        var script = Script("M1-STOP.ps1");
        Assert.Contains("executable path does not match", script);
        Assert.Contains("Refusing to stop", script);
        Assert.Contains("Start-Sleep -Milliseconds 200", script);
        Assert.DoesNotContain("taskkill", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dotnet.exe", script, StringComparison.OrdinalIgnoreCase);
    }
}
