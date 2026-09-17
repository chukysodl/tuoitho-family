using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using TuoiTho.Core.Policy;

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
if (args.Length != 0)
{
    Console.Error.WriteLine("Usage: TuoiTho.BrowserHost [--bootstrap-config <extension-directory> <configuration-path> | --service-probe]");
    Environment.ExitCode = 2;
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

internal static class NativeMessaging
{
    public static async Task<BrowserNavigationRequest?> ReadAsync(Stream input, CancellationToken token)
    {
        var header = new byte[4];
        if (!await ReadExactlyAsync(input, header, token)) return null;
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 1 or > BrowserNavigationValidator.MaximumMessageBytes) return null;
        var payload = new byte[length];
        if (!await ReadExactlyAsync(input, payload, token)) return null;
        try { return JsonSerializer.Deserialize<BrowserNavigationRequest>(payload); }
        catch (JsonException) { return null; }
    }

    public static async Task WriteAsync(Stream output, BrowserNavigationResponse response, CancellationToken token)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(response);
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, data.Length);
        await output.WriteAsync(header, token);
        await output.WriteAsync(data, token);
        await output.FlushAsync(token);
    }

    private static async Task<bool> ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken token)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(read), token);
            if (count == 0) return false;
            read += count;
        }
        return true;
    }
}

internal static class BrowserPolicyPipeClient
{
    public static async Task<BrowserNavigationResponse> EvaluateAsync(BrowserNavigationRequest request, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        using var pipe = new NamedPipeClientStream(".", "TuoiTho.BrowserPolicy", PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeout.Token);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(request));
        using var reader = new StreamReader(pipe, leaveOpen: true);
        var response = await reader.ReadLineAsync(timeout.Token);
        return string.IsNullOrWhiteSpace(response)
            ? throw new IOException("Service returned no browser policy response.")
            : JsonSerializer.Deserialize<BrowserNavigationResponse>(response)
                ?? throw new JsonException("Service returned invalid browser policy response.");
    }
}
