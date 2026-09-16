using System.Text.Json;
using TuoiTho.Core.Policy;
using TuoiTho.Parent;

namespace TuoiTho.Tests;

public sealed class ParentIpcResilienceTests
{
    [Fact]
    public async Task TimeoutExceptionReturnsFriendlyVietnameseResult()
    {
        using var controller = new ParentDesktopController(new ThrowingClient(new TimeoutException()), "child", 7);
        var result = await controller.RefreshAsync();
        Assert.False(result.Success);
        Assert.Equal("Không thể kết nối dịch vụ Tuổi Thơ. Vui lòng thử lại.", result.Message);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("unauthorized")]
    [InlineData("cancelled")]
    [InlineData("json")]
    public async Task ExpectedIpcFailuresNeverEscapeToUi(string failure)
    {
        using var controller = new ParentDesktopController(new ThrowingClient(failure switch
        {
            "io" => new IOException(),
            "unauthorized" => new UnauthorizedAccessException(),
            "cancelled" => new OperationCanceledException(),
            _ => new JsonException()
        }), "child", 7);
        var result = await controller.SendAsync(ParentControlAction.BulkAllowRunningApps);
        Assert.False(result.Success);
        Assert.Equal("Không thể kết nối dịch vụ Tuổi Thơ. Vui lòng thử lại.", result.Message);
    }

    [Fact]
    public async Task TimerAndAllowAppAreSerialized()
    {
        var client = new BlockingClient();
        using var controller = new ParentDesktopController(client, "child", 7);
        var timer = controller.RefreshAsync();
        await client.Entered.Task;
        var allow = controller.SendAppAsync(ParentControlAction.AllowApp, AppIdentity.FromExecutablePath("C:\\Apps\\Note.exe"));
        await Task.Delay(30);
        Assert.Equal(1, client.Calls);
        client.Release.SetResult();
        await Task.WhenAll(timer, allow);
        Assert.Equal(1, client.MaxConcurrent);
    }

    [Theory]
    [InlineData(ParentControlAction.BulkAllowRunningApps)]
    [InlineData(ParentControlAction.EnableM2AllowlistEnforcement)]
    public async Task TimerAndManualM2ActionAreSerialized(ParentControlAction action)
    {
        var client = new BlockingClient();
        using var controller = new ParentDesktopController(client, "child", 7);
        var timer = controller.RefreshAsync();
        await client.Entered.Task;
        var manual = controller.SendAsync(action);
        await Task.Delay(30);
        Assert.Equal(1, client.Calls);
        client.Release.SetResult();
        await Task.WhenAll(timer, manual);
        Assert.Equal(1, client.MaxConcurrent);
    }

    [Fact]
    public async Task RepeatedTimerTicksDoNotStack()
    {
        var client = new BlockingClient();
        using var controller = new ParentDesktopController(client, "child", 7);
        var first = controller.TryAutoRefreshAsync();
        await client.Entered.Task;
        var second = await controller.TryAutoRefreshAsync();
        Assert.Null(second);
        Assert.Equal(1, client.Calls);
        client.Release.SetResult();
        Assert.NotNull(await first);
        Assert.Equal(1, client.MaxConcurrent);
    }

    private sealed class ThrowingClient(Exception error) : IParentControlClient
    {
        public Task<ParentControlResult> SendAsync(ParentControlCommand command, CancellationToken token = default) => Task.FromException<ParentControlResult>(error);
    }

    private sealed class BlockingClient : IParentControlClient
    {
        private int concurrent;
        public int Calls { get; private set; }
        public int MaxConcurrent { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ParentControlResult> SendAsync(ParentControlCommand command, CancellationToken token = default)
        {
            var now = Interlocked.Increment(ref concurrent);
            MaxConcurrent = Math.Max(MaxConcurrent, now);
            Calls++;
            Entered.TrySetResult();
            try
            {
                await Release.Task.WaitAsync(token);
                return new ParentControlResult(true, null, null, new ParentControlStatus("child", 7, true, 0, 3, 0, 3, "ALLOWED"));
            }
            finally { Interlocked.Decrement(ref concurrent); }
        }
    }
}