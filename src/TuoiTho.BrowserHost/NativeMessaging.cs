using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using TuoiTho.Core.Policy;

namespace TuoiTho.BrowserHost;

public static class NativeMessaging
{
    // Native Messaging is a browser wire contract, not a storage or Service-pipe serializer.
    // Web defaults provide camelCase and this explicit converter keeps browser enum values textual.
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
    public static async Task<BrowserNavigationRequest?> ReadAsync(Stream input, CancellationToken token)
    {
        var header = new byte[4];
        if (!await ReadExactlyAsync(input, header, token)) return null;
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 1 or > BrowserNavigationValidator.MaximumMessageBytes) return null;
        var payload = new byte[length];
        if (!await ReadExactlyAsync(input, payload, token)) return null;
        try { return JsonSerializer.Deserialize<BrowserNavigationRequest>(payload, JsonOptions); }
        catch (JsonException) { return null; }
    }

    public static async Task WriteAsync(Stream output, BrowserNavigationResponse response, CancellationToken token)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(response, JsonOptions);
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
