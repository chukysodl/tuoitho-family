using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using TuoiTho.Core.Policy;
using TuoiTho.BrowserHost;

if (args is ["--bootstrap-config", var extensionDirectory, var configurationPath])
{
    Console.WriteLine(BrowserDeploymentBootstrapper.Create(extensionDirectory, configurationPath));
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

internal static class BrowserDeploymentBootstrapper
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };
    public static string Create(string extensionDirectory, string configurationPath)
    {
        var manifestPath = Path.Combine(extensionDirectory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException("The staged browser extension manifest is unavailable.");
        }

        using var rsa = RSA.Create(2048);
        var publicKey = rsa.ExportSubjectPublicKeyInfo();
        var extensionId = ToChromeExtensionId(SHA256.HashData(publicKey));
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject()
            ?? throw new InvalidOperationException("The staged browser extension manifest is invalid.");
        manifest["key"] = Convert.ToBase64String(publicKey);
        File.WriteAllText(manifestPath, manifest.ToJsonString(IndentedJson));

        var sid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("The current Windows user SID is unavailable.");
        var configuration = new BrowserControlConfiguration(
            extensionId,
            "m1-child",
            Process.GetCurrentProcess().SessionId,
            sid,
            TestMode: true);
        configuration.Validate();

        var directory = Path.GetDirectoryName(configurationPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("The browser control configuration path is invalid.");
        }

        Directory.CreateDirectory(directory);
        File.WriteAllText(configurationPath, JsonSerializer.Serialize(configuration, IndentedJson));
        File.WriteAllText(Path.Combine(extensionDirectory, "m4-runtime-config.js"), $"export const TEST_MODE = {configuration.TestMode.ToString().ToLowerInvariant()};{Environment.NewLine}");
        return extensionId;
    }

    private static string ToChromeExtensionId(byte[] keyHash)
    {
        if (keyHash.Length < 16) throw new InvalidOperationException("The extension key hash is invalid.");
        var characters = new char[32];
        for (var index = 0; index < 16; index++)
        {
            characters[index * 2] = (char)('a' + (keyHash[index] >> 4));
            characters[index * 2 + 1] = (char)('a' + (keyHash[index] & 0x0f));
        }
        return new string(characters);
    }
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
