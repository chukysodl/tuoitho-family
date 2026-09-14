using TuoiTho.Core.Policy;
namespace TuoiTho.Parent;

public sealed record ParentUiResult(bool Success,string Message,ParentControlStatus? Status);
public sealed class ParentDesktopController(IParentControlClient client,string profileId,int sessionId)
{
 public async Task<ParentUiResult> RefreshAsync(CancellationToken token=default)=>await SendAsync(ParentControlAction.GetStatus,minutes:null,token:token);
 public async Task<ParentUiResult> SendAsync(ParentControlAction action,int? minutes=null,CancellationToken token=default)
 {
  try{var result=await client.SendAsync(new ParentControlCommand(action,profileId,sessionId,minutes),token);return result.Accepted?new(true,"THÀNH CÔNG",result.Status):new(false,$"TỪ CHỐI: {FriendlyError(result.Error)}",result.Status);}
  catch(Exception e)when(e is IOException or UnauthorizedAccessException or OperationCanceledException or System.Text.Json.JsonException){return new(false,"TỪ CHỐI: Dịch vụ cục bộ chưa sẵn sàng hoặc bạn không được cấp quyền.",null);}
 }
 public async Task<ParentUiResult> SendAppAsync(ParentControlAction action,AppIdentity application,CancellationToken token=default){ try { var result=await client.SendAsync(new ParentControlCommand(action,profileId,sessionId,null,application),token);return result.Accepted?new(true,"THÀNH CÔNG",result.Status):new(false,$"TỪ CHỐI: {FriendlyError(result.Error)}",result.Status); } catch(Exception e)when(e is IOException or UnauthorizedAccessException or OperationCanceledException or System.Text.Json.JsonException){return new(false,"TỪ CHỐI: Dịch vụ cục bộ chưa sẵn sàng hoặc bạn không được cấp quyền.",null);} }
 private static string FriendlyError(string? error)=>error switch{"UNAUTHORIZED"=>"Bạn không có quyền thực hiện thao tác này.","PROFILE_OR_SESSION_MISMATCH"=>"Hồ sơ hoặc phiên quản lý không khớp.","INVALID_GRANT"=>"Số phút phải từ 1 đến 1440.","EMPTY_RESPONSE" or "MALFORMED_RESPONSE"=>"Phản hồi từ Dịch vụ không hợp lệ.",_=>"Dịch vụ không chấp nhận yêu cầu."};
}