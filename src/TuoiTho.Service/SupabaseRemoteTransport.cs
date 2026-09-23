using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TuoiTho.Core.Remote;

namespace TuoiTho.Service;

/// <summary>Supabase Edge Function adapter. The service's policy/core layers depend only on IRemoteTransport.</summary>
public sealed class SupabaseRemoteTransport : IRemoteTransport, IDisposable
{
    private const int MaxResponseBytes = 1024 * 1024;
    private const int MaxRequestBytes = 1024 * 1024;
    private readonly HttpClient client;
    private readonly RemoteControlOptions options;
    private readonly JsonSerializerOptions json = CreateJsonOptions();

    public SupabaseRemoteTransport(IOptions<RemoteControlOptions> options)
    {
        this.options = options.Value;
        client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
    }

    public SupabaseRemoteTransport(RemoteControlOptions options, HttpClient client)
    {
        this.options = options;
        this.client = client;
    }

    public Task RegisterPairingAsync(RemotePairingRegistration pairing, CancellationToken cancellationToken = default)
        => PostAsync<object, JsonElement>(options.PairingFunctionName, new { action = "register_pairing", pairing }, null, cancellationToken);

    public async Task<IReadOnlyList<RemoteCommandEnvelope>> ReceiveCommandsAsync(RemoteDeviceCredential credential, CancellationToken cancellationToken = default)
    {
        var response = await PostAsync<object, PollResponse>(options.PairingFunctionName, new { action = "poll_commands" }, credential, cancellationToken);
        return response.Commands ?? [];
    }

    public Task PublishStatusAsync(RemoteDeviceCredential credential, RemoteDeviceStatus status, CancellationToken cancellationToken = default)
        => PostAsync<object, JsonElement>(options.PairingFunctionName, new { action = "publish_status", status }, credential, cancellationToken);

    public Task AcknowledgeAsync(RemoteDeviceCredential credential, RemoteCommandAcknowledgement acknowledgement, CancellationToken cancellationToken = default)
        => PostAsync<object, JsonElement>(options.PairingFunctionName, new { action = "acknowledge", acknowledgement }, credential, cancellationToken);

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string functionName, TRequest body, RemoteDeviceCredential? credential, CancellationToken token)
    {
        if (!options.Enabled || !Uri.TryCreate(options.SupabaseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(baseUri.UserInfo) || !string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment) || !SupabasePublicKeyValidator.IsValid(options.SupabaseAnonKey))
            throw new InvalidOperationException("Remote control is not configured with a valid HTTPS Supabase endpoint and public project key.");
        var uri = new Uri(baseUri, $"/functions/v1/{Uri.EscapeDataString(functionName)}");
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.Add("apikey", options.SupabaseAnonKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.SupabaseAnonKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (credential is not null)
        {
            request.Headers.Add("x-tuoi-tho-device-id", credential.DeviceId);
            request.Headers.Add("x-tuoi-tho-device-credential", credential.BearerToken);
        }
        var requestBytes = JsonSerializer.SerializeToUtf8Bytes(body, json);
        if (requestBytes.Length > MaxRequestBytes) throw new InvalidDataException("Remote request exceeded the allowed size.");
        request.Content = new ByteArrayContent(requestBytes);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, token);
            if (read == 0) break;
            if (buffer.Length + read > MaxResponseBytes) throw new InvalidDataException("Remote response exceeded the allowed size.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), token);
        }
        if (!response.IsSuccessStatusCode)
        {
            var safeCode = "REMOTE_HTTP_" + (int)response.StatusCode;
            throw new HttpRequestException(safeCode, null, response.StatusCode);
        }
        buffer.Position = 0;
        var parsed = await JsonSerializer.DeserializeAsync<TResponse>(buffer, json, token);
        return parsed is null ? throw new InvalidDataException("Remote endpoint returned an empty response.") : parsed;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var result = new JsonSerializerOptions(JsonSerializerDefaults.Web) { MaxDepth = 32 };
        result.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return result;
    }

    public void Dispose() => client.Dispose();
    private sealed record PollResponse(IReadOnlyList<RemoteCommandEnvelope>? Commands);
}
