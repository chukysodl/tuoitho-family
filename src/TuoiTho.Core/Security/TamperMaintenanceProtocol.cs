namespace TuoiTho.Core.Security;

public static class TamperMaintenanceProtocol
{
    public const string PipeName = "TuoiTho.TamperMaintenance";
    public const string AuthorizeCommand = "authorize-maintenance";
    public const int DefaultDurationSeconds = 180;
    public const int MinimumDurationSeconds = 30;
    public const int MaximumDurationSeconds = 600;
    public const int MaximumPasswordLength = 256;
}

public sealed record TamperMaintenanceRequest(
    string Command,
    string Password,
    int DurationSeconds = TamperMaintenanceProtocol.DefaultDurationSeconds);

public sealed record TamperMaintenanceResponse(
    bool Authorized,
    string Message,
    DateTimeOffset? ExpiresUtc = null);
