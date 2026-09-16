using System.Text.Json;
using TuoiTho.Core.Policy;

namespace TuoiTho.Parent;

public sealed record ParentUiResult(bool Success, string Message, ParentControlStatus? Status);

/// <summary>Serializes every local Parent pipe request so refreshes cannot race parent actions.</summary>
public sealed class ParentDesktopController(IParentControlClient client, string profileId, int sessionId) : IDisposable
{
    private const string ServiceUnavailableMessage = "Không thể kết nối dịch vụ Tuổi Thơ. Vui lòng thử lại.";
    private readonly SemaphoreSlim requestGate = new(1, 1);
    private int autoRefreshPending;

    public Task<ParentUiResult> RefreshAsync(CancellationToken token = default) =>
        ExecuteAsync(new ParentControlCommand(ParentControlAction.GetStatus, profileId, sessionId), token);

    /// <summary>Returns null when an earlier automatic refresh is still in progress; it never queues timer work.</summary>
    public async Task<ParentUiResult?> TryAutoRefreshAsync(CancellationToken token = default)
    {
        if (Interlocked.CompareExchange(ref autoRefreshPending, 1, 0) != 0) return null;
        try { return await RefreshAsync(token); }
        finally { Volatile.Write(ref autoRefreshPending, 0); }
    }

    public Task<ParentUiResult> RefreshAppsAsync(CancellationToken token = default) =>
        ExecuteAsync(new ParentControlCommand(ParentControlAction.RefreshApps, profileId, sessionId), token);

    public Task<ParentUiResult> SendAsync(ParentControlAction action, int? minutes = null, CancellationToken token = default) =>
        ExecuteAsync(new ParentControlCommand(action, profileId, sessionId, minutes), token);

    public Task<ParentUiResult> SendAppAsync(ParentControlAction action, AppIdentity application, CancellationToken token = default) =>
        ExecuteAsync(new ParentControlCommand(action, profileId, sessionId, null, application), token);

    public Task<ParentUiResult> SaveTimePolicyAsync(int dailyQuotaMinutes, IReadOnlyList<AllowedUsageWindow> windows, CancellationToken token = default) =>
        ExecuteAsync(new ParentControlCommand(ParentControlAction.SaveTimePolicy, profileId, sessionId, DailyQuotaMinutes: dailyQuotaMinutes, Windows: windows), token);

    private async Task<ParentUiResult> ExecuteAsync(ParentControlCommand command, CancellationToken token)
    {
        var entered = false;
        try
        {
            await requestGate.WaitAsync(token);
            entered = true;
            var result = await client.SendAsync(command, token);
            return result.Accepted
                ? new ParentUiResult(true, result.Message ?? "THÀNH CÔNG", result.Status)
                : new ParentUiResult(false, $"TỪ CHỐI: {result.Message ?? FriendlyError(result.Error)}", result.Status);
        }
        catch (Exception exception) when (exception is TimeoutException or IOException or UnauthorizedAccessException or OperationCanceledException or JsonException)
        {
            return new ParentUiResult(false, ServiceUnavailableMessage, null);
        }
        finally
        {
            if (entered) requestGate.Release();
        }
    }

    public void Dispose() => requestGate.Dispose();

    private static string FriendlyError(string? error) => error switch
    {
        "UNAUTHORIZED" => "Bạn không có quyền thực hiện thao tác này.",
        "PROFILE_OR_SESSION_MISMATCH" => "Hồ sơ hoặc phiên quản lý không khớp.",
        "INVALID_GRANT" => "Số phút phải từ 1 đến 1440.",
        "INVALID_DAILY_QUOTA" => "Hạn mức mỗi ngày phải từ 1 đến 1440 phút.",
        "INVALID_SCHEDULE" => "Khung giờ sử dụng không hợp lệ.",
        "EMPTY_RESPONSE" or "MALFORMED_RESPONSE" => "Phản hồi từ Dịch vụ không hợp lệ.",
        "APP_ENFORCEMENT_TESTMODE_REQUIRED" => "Chặn thử nghiệm chỉ khả dụng trong TestMode.",
        _ => "Dịch vụ không chấp nhận yêu cầu."
    };
}