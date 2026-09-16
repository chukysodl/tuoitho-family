using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;
using TuoiTho.Core.Policy;

var expectedExtension = Environment.GetEnvironmentVariable("TUOITHO_EXTENSION_ID") ?? string.Empty;
var profileId = Environment.GetEnvironmentVariable("TUOITHO_BROWSER_PROFILE") ?? "m1-child";
var sessionId = int.TryParse(Environment.GetEnvironmentVariable("TUOITHO_BROWSER_SESSION"), out var configuredSession) ? configuredSession : -1;
var testMode = bool.TryParse(Environment.GetEnvironmentVariable("TUOITHO_BROWSER_TEST_MODE"), out var configuredTestMode) && configuredTestMode;
while (true)
{
    var request = await NativeMessaging.ReadAsync(Console.OpenStandardInput(), CancellationToken.None);
    if (request is null) break;
    BrowserNavigationResponse response;
    request = request with { ProfileId = profileId, ManagedSessionId = sessionId };
    if (!BrowserNavigationValidator.IsValid(request, expectedExtension)) response = new(false, "INVALID_BROWSER_REQUEST", Diagnostic: "Tuổi Thơ chưa kết nối.");
    else
    {
        try { response = await BrowserPolicyPipeClient.EvaluateAsync(request, CancellationToken.None); }
        catch (Exception) { response = BrowserHostAvailabilityPolicy.ServiceUnavailable(testMode); }
    }
    await NativeMessaging.WriteAsync(Console.OpenStandardOutput(), response, CancellationToken.None);
}

internal static class NativeMessaging
{
    public static async Task<BrowserNavigationRequest?> ReadAsync(Stream input, CancellationToken token)
    {
        var header = new byte[4]; if (!await ReadExactlyAsync(input, header, token)) return null;
        var length = BinaryPrimitives.ReadInt32LittleEndian(header); if (length is < 1 or > BrowserNavigationValidator.MaximumMessageBytes) return null;
        var payload = new byte[length]; if (!await ReadExactlyAsync(input, payload, token)) return null;
        try { return JsonSerializer.Deserialize<BrowserNavigationRequest>(payload); } catch (JsonException) { return null; }
    }
    public static async Task WriteAsync(Stream output, BrowserNavigationResponse response, CancellationToken token)
    { var data = JsonSerializer.SerializeToUtf8Bytes(response); var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, data.Length); await output.WriteAsync(header, token); await output.WriteAsync(data, token); await output.FlushAsync(token); }
    private static async Task<bool> ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken token) { var read = 0; while (read < buffer.Length) { var count = await stream.ReadAsync(buffer.AsMemory(read), token); if (count == 0) return false; read += count; } return true; }
}
internal static class BrowserPolicyPipeClient
{
    public static async Task<BrowserNavigationResponse> EvaluateAsync(BrowserNavigationRequest request, CancellationToken token)
    { using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(2)); using var pipe = new NamedPipeClientStream(".", "TuoiTho.BrowserPolicy", PipeDirection.InOut, PipeOptions.Asynchronous); await pipe.ConnectAsync(timeout.Token); using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true }; await writer.WriteLineAsync(JsonSerializer.Serialize(request)); using var reader = new StreamReader(pipe, leaveOpen: true); var response = await reader.ReadLineAsync(timeout.Token); return string.IsNullOrWhiteSpace(response) ? throw new IOException("Service returned no browser policy response.") : JsonSerializer.Deserialize<BrowserNavigationResponse>(response) ?? throw new JsonException("Service returned invalid browser policy response."); }
}