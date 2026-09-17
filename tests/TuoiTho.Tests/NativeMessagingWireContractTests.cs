using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using TuoiTho.Core.Policy;
using TuoiTho.BrowserHost;

namespace TuoiTho.Tests;

public sealed class NativeMessagingWireContractTests
{
    [Theory]
    [InlineData("Channel", "YouTube")]
    [InlineData("Video", "YouTube")]
    [InlineData("ShortForm", "YouTube")]
    [InlineData("Creator", "TikTok")]
    public async Task BrowserStyleCamelCaseRequestWithStringEnumsRoundTrips(string contentType, string provider)
    {
        var json = $$"""{"extensionId":"extension","profileId":"m1-child","managedSessionId":1,"provider":"{{provider}}","host":"www.youtube.com","path":"/@VịtBéoTV/featured","contentType":"{{contentType}}","channelHandle":"@VịtBéoTV"}""";
        await using var input = Framed(json);
        var request = await NativeMessaging.ReadAsync(input, CancellationToken.None);
        Assert.NotNull(request);
        Assert.Equal("extension", request.ExtensionId);
        Assert.Equal("m1-child", request.ProfileId);
        Assert.Equal(1, request.ManagedSessionId);
        Assert.Equal(Enum.Parse<BrowserProvider>(provider), request.Provider);
        Assert.Equal(Enum.Parse<BrowserContentType>(contentType), request.ContentType);
        Assert.Equal("@VịtBéoTV", request.ChannelHandle);
    }

    [Fact]
    public async Task BrowserResponseUsesCamelCaseFraming()
    {
        await using var output = new MemoryStream();
        await NativeMessaging.WriteAsync(output, new BrowserNavigationResponse(false, "EXPLICIT_BLOCK", "@VịtBéoTV"), CancellationToken.None);
        output.Position = 0;
        var header = new byte[4];
        Assert.Equal(4, await output.ReadAsync(header));
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        var payload = new byte[length];
        Assert.Equal(length, await output.ReadAsync(payload));
        var json = Encoding.UTF8.GetString(payload);
        Assert.Contains("\"allowed\":false", json, StringComparison.Ordinal);
        Assert.Contains("\"reason\":\"EXPLICIT_BLOCK\"", json, StringComparison.Ordinal);
        Assert.Contains("\"displayLabel\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Allowed\"", json, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.GetProperty("allowed").GetBoolean());
    }

    private static MemoryStream Framed(string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var stream = new MemoryStream();
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        stream.Write(header);
        stream.Write(payload);
        stream.Position = 0;
        return stream;
    }
}
