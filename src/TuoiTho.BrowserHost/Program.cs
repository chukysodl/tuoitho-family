using System.IO.Pipes;
using System.Text.Json;
using TuoiTho.Core.Policy;
using TuoiTho.BrowserHost;

if (args is ["--bootstrap-config", var extensionDirectory, var configurationPath])
{
    Console.WriteLine(BrowserDeploymentBootstrapper.Create(extensionDirectory, configurationPath).ExtensionId);
    return;
}

if (args is ["--repair-browser-config", var repairExtensionDirectory, var repairConfigurationPath])
{
    Console.WriteLine(BrowserDeploymentBootstrapper.Repair(repairExtensionDirectory, repairConfigurationPath).ExtensionId);
    return;
}

if (args is ["--extension-id", var manifestPath])
{
    Console.WriteLine(BrowserDeploymentBootstrapper.GetExtensionId(manifestPath));
    return;
}

if (args is ["--preserve-extension-key", var priorManifestPath, var stagedManifestPath])
{
    Console.WriteLine(BrowserDeploymentBootstrapper.PreserveKeyDuringUpdate(priorManifestPath, stagedManifestPath));
    return;
}

if (args is ["--service-probe"])
{
    try
    {
        var probeConfig = BrowserControlConfiguration.Load();
        var probe = new BrowserNavigationRequest(probeConfig.ExtensionId, probeConfig.ProfileId, probeConfig.ManagedSessionId,
            BrowserProvider.YouTube, "www.youtube.com", "/", BrowserContentType.Site, IsDiagnosticProbe: true);
        var result = await BrowserPolicyPipeClient.EvaluateAsync(probe, CancellationToken.None);
        Console.WriteLine(JsonSerializer.Serialize(result));
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Service probe failed: {exception.Message}");
        Environment.ExitCode = 4;
    }
    return;
}

BrowserControlConfiguration config;
try
{
    config = BrowserControlConfiguration.Load();
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Browser control configuration is unavailable: {exception.Message}");
    Environment.ExitCode = 3;
    return;
}

if (!NativeHostLaunchArguments.TryValidate(args, config, out var launchError))
{
    // STDOUT is reserved exclusively for framed Native Messaging responses.
    Console.Error.WriteLine($"Native Messaging launch rejected: {launchError}");
    Environment.ExitCode = 2;
    return;
}

while (true)
{
    var request = await NativeMessaging.ReadAsync(Console.OpenStandardInput(), CancellationToken.None);
    if (request is null) break;

    request = request with { ProfileId = config.ProfileId, ManagedSessionId = config.ManagedSessionId };
    BrowserNavigationResponse response;
    if (!BrowserNavigationValidator.IsValid(request, config.ExtensionId))
    {
        response = new(false, "INVALID_BROWSER_REQUEST", Diagnostic: "Tuổi Thơ chưa kết nối.");
    }
    else
    {
        try
        {
            response = await BrowserPolicyPipeClient.EvaluateAsync(request, CancellationToken.None);
        }
        catch (Exception)
        {
            // This is intentionally temporary and memory-only. Every following browser request
            // reconnects to the service, so production stays fail-closed and M4 TestMode resumes.
            response = BrowserHostAvailabilityPolicy.ServiceUnavailable(config.TestMode);
        }
    }

    await NativeMessaging.WriteAsync(Console.OpenStandardOutput(), response, CancellationToken.None);
}

internal static class BrowserPolicyPipeClient
{
    public static async Task<BrowserNavigationResponse> EvaluateAsync(BrowserNavigationRequest request, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var pipe = new NamedPipeClientStream(".", "TuoiTho.BrowserPolicy", PipeDirection.InOut, PipeOptions.Asynchronous);
        try { await pipe.ConnectAsync(timeout.Token); }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested) { throw new IOException("PIPE_CONNECT_TIMEOUT"); }
        catch (IOException exception) { throw new IOException("PIPE_NOT_AVAILABLE", exception); }
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(request));
        using var reader = new StreamReader(pipe, leaveOpen: true);
        string? response;
        try { response = await reader.ReadLineAsync(timeout.Token); }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested) { throw new IOException("SERVICE_RESPONSE_TIMEOUT"); }
        if (string.IsNullOrWhiteSpace(response)) throw new IOException("SERVICE_RESPONSE_TIMEOUT");
        try { return JsonSerializer.Deserialize<BrowserNavigationResponse>(response) ?? throw new JsonException(); }
        catch (JsonException exception) { throw new IOException("INVALID_RESPONSE", exception); }
    }
}