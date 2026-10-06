using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;
using TuoiTho.Core.Security;

namespace TuoiTho.Parent;

public sealed record LocalWindowsAccount(
    string Name,
    string Sid,
    bool Enabled,
    bool IsAdministrator,
    bool IsCurrent,
    bool IsBuiltInAdministrator);

public sealed record AccountProtectionSnapshot(
    string CurrentUser,
    string CurrentSid,
    bool CurrentProcessElevated,
    bool CurrentAccountAdministrator,
    bool UacEnabled,
    string? ManagedChildSid,
    string? ManagedChildName,
    bool ManagedChildExists,
    bool ManagedChildIsAdministrator,
    IReadOnlyList<LocalWindowsAccount> Accounts)
{
    public bool Safe =>
        UacEnabled &&
        CurrentAccountAdministrator &&
        !string.IsNullOrWhiteSpace(ManagedChildSid) &&
        ManagedChildExists &&
        !ManagedChildIsAdministrator;
}

public sealed record AccountProtectionOperationResult(bool Success, string Message);

public static class AccountProtectionRules
{
    public static string? ValidateDemotion(
        string currentSid,
        LocalWindowsAccount target,
        int enabledAdministratorCount)
    {
        if (!target.Enabled)
            return "Tài khoản đã chọn đang bị vô hiệu hóa.";
        if (target.IsCurrent || string.Equals(currentSid, target.Sid, StringComparison.OrdinalIgnoreCase))
            return "Không thể dùng chính tài khoản phụ huynh đang đăng nhập làm tài khoản trẻ.";
        if (target.IsBuiltInAdministrator)
            return "Không thể chuyển tài khoản Administrator tích hợp của Windows thành tài khoản trẻ.";
        if (target.IsAdministrator && enabledAdministratorCount < 2)
            return "Máy phải còn ít nhất một tài khoản Administrator khác để tránh bị khóa quyền quản trị.";
        return null;
    }
}

public sealed class AccountProtectionManager
{
    private const int NerrSuccess = 0;
    private const int MaxPreferredLength = -1;
    private const int FilterNormalAccount = 2;
    private const uint UfAccountDisable = 0x0002;
    private const int ErrorNoSuchMember = 1387;

    public static AccountProtectionSnapshot Snapshot()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var currentSid = identity.User?.Value ?? string.Empty;
        var currentUser = identity.Name;
        var elevated = new WindowsPrincipal(identity)
            .IsInRole(WindowsBuiltInRole.Administrator);

        var accounts = EnumerateLocalAccounts(currentSid);
        var current = accounts.FirstOrDefault(account =>
            string.Equals(account.Sid, currentSid, StringComparison.OrdinalIgnoreCase));

        var configured = AccountProtectionConfiguration.TryLoad();
        var configuredAccount = configured is null
            ? null
            : accounts.FirstOrDefault(account =>
                string.Equals(account.Sid, configured.ManagedChildSid, StringComparison.OrdinalIgnoreCase));

        return new AccountProtectionSnapshot(
            currentUser,
            currentSid,
            elevated,
            current?.IsAdministrator ?? IsSidInAdministrators(currentSid),
            ReadUacEnabled(),
            configured?.ManagedChildSid,
            configured?.ManagedChildName,
            configuredAccount is not null,
            configuredAccount?.IsAdministrator ?? false,
            accounts);
    }

    public static AccountProtectionOperationResult ConfigureChild(string sid)
    {
        var snapshot = Snapshot();
        if (!snapshot.CurrentProcessElevated)
            return new(false, "Cần quyền Administrator của Windows để thay đổi loại tài khoản.");

        var target = snapshot.Accounts.FirstOrDefault(account =>
            string.Equals(account.Sid, sid, StringComparison.OrdinalIgnoreCase));
        if (target is null)
            return new(false, "Không tìm thấy tài khoản Windows đã chọn.");

        var enabledAdminCount = snapshot.Accounts.Count(account =>
            account.Enabled && account.IsAdministrator);

        var validation = AccountProtectionRules.ValidateDemotion(
            snapshot.CurrentSid,
            target,
            enabledAdminCount);
        if (validation is not null)
            return new(false, validation);

        if (target.IsAdministrator)
        {
            var group = AdministratorsGroupName();
            var member = new LocalGroupMembersInfo3
            {
                DomainAndName = $"{Environment.MachineName}\\{target.Name}"
            };

            var result = NetLocalGroupDelMembers(
                null,
                group,
                3,
                ref member,
                1);

            if (result != NerrSuccess && result != ErrorNoSuchMember)
                return new(false, $"Windows không thể bỏ quyền Administrator (mã {result}).");
        }

        var refreshed = EnumerateLocalAccounts(snapshot.CurrentSid)
            .FirstOrDefault(account =>
                string.Equals(account.Sid, target.Sid, StringComparison.OrdinalIgnoreCase));
        if (refreshed is null || refreshed.IsAdministrator)
            return new(false, "Tài khoản vẫn còn quyền Administrator sau khi áp dụng.");

        try
        {
            var configuration = new AccountProtectionConfiguration(
                true,
                refreshed.Sid,
                refreshed.Name);

            var path = AccountProtectionConfiguration.DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(configuration));

            HardenConfiguration(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new(false, "Đã chuyển tài khoản về Standard User nhưng không lưu được cấu hình bảo vệ: " + exception.Message);
        }

        return new(
            true,
            $"Đã đặt {refreshed.Name} làm tài khoản trẻ Standard User. IObit/Revo và thao tác quản trị từ tài khoản này sẽ phải xin thông tin Administrator của phụ huynh.");
    }

    public static AccountProtectionOperationResult ConfigureChildWithElevation(
        string sid,
        IWin32Window? owner = null)
    {
        var snapshot = Snapshot();
        if (snapshot.CurrentProcessElevated)
            return ConfigureChild(sid);

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
            return new(false, "Không xác định được file chương trình để yêu cầu quyền Administrator.");

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = true,
                Verb = "runas",
                Arguments = $"--configure-child-sid \"{sid}\""
            });

            if (process is null)
                return new(false, "Không mở được cửa sổ xác nhận quyền Administrator.");

            process.WaitForExit();
            return process.ExitCode == 0
                ? new(true, "Đã áp dụng Account Protection Mode.")
                : new(false, "Chưa áp dụng được Account Protection Mode.");
        }
        catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return new(false, "Đã hủy yêu cầu quyền Administrator.");
        }
    }

    public static void OpenWindowsAccountsSettings()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "ms-settings:otherusers",
            UseShellExecute = true
        });
    }

    private static LocalWindowsAccount[] EnumerateLocalAccounts(string currentSid)
    {
        var adminSids = AdministratorsMemberSids();
        var accounts = new List<LocalWindowsAccount>();
        var resume = 0;
        IntPtr buffer = IntPtr.Zero;

        try
        {
            do
            {
                var result = NetUserEnum(
                    null,
                    1,
                    FilterNormalAccount,
                    out buffer,
                    MaxPreferredLength,
                    out var entriesRead,
                    out _,
                    ref resume);

                if (result != NerrSuccess && result != 234)
                    break;

                var size = Marshal.SizeOf<UserInfo1>();
                for (var index = 0; index < entriesRead; index++)
                {
                    var item = Marshal.PtrToStructure<UserInfo1>(
                        IntPtr.Add(buffer, index * size));
                    var name = Marshal.PtrToStringUni(item.Name);
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var sid = TryResolveLocalSid(name);
                    if (string.IsNullOrWhiteSpace(sid)) continue;

                    accounts.Add(new LocalWindowsAccount(
                        name,
                        sid,
                        (item.Flags & UfAccountDisable) == 0,
                        adminSids.Contains(sid),
                        string.Equals(sid, currentSid, StringComparison.OrdinalIgnoreCase),
                        sid.EndsWith("-500", StringComparison.Ordinal)));
                }

                if (buffer != IntPtr.Zero)
                {
                    _ = NetApiBufferFree(buffer);
                    buffer = IntPtr.Zero;
                }

                if (result == NerrSuccess) break;
            }
            while (true);
        }
        finally
        {
            if (buffer != IntPtr.Zero)
                _ = NetApiBufferFree(buffer);
        }

        return accounts
            .OrderByDescending(account => account.IsCurrent)
            .ThenBy(account => account.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static string? TryResolveLocalSid(string name)
    {
        try
        {
            var account = new NTAccount(Environment.MachineName, name);
            return ((SecurityIdentifier)account.Translate(typeof(SecurityIdentifier))).Value;
        }
        catch (IdentityNotMappedException)
        {
            try
            {
                var account = new NTAccount(name);
                return ((SecurityIdentifier)account.Translate(typeof(SecurityIdentifier))).Value;
            }
            catch (IdentityNotMappedException)
            {
                return null;
            }
        }
    }

    private static bool IsSidInAdministrators(string sid) =>
        AdministratorsMemberSids().Contains(sid);

    private static HashSet<string> AdministratorsMemberSids()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var group = AdministratorsGroupName();
        IntPtr buffer = IntPtr.Zero;
        IntPtr resume = IntPtr.Zero;

        try
        {
            var status = NetLocalGroupGetMembers(
                null,
                group,
                2,
                out buffer,
                MaxPreferredLength,
                out var entriesRead,
                out _,
                ref resume);

            if (status != NerrSuccess && status != 234)
                return result;

            var size = Marshal.SizeOf<LocalGroupMembersInfo2>();
            for (var index = 0; index < entriesRead; index++)
            {
                var item = Marshal.PtrToStructure<LocalGroupMembersInfo2>(
                    IntPtr.Add(buffer, index * size));
                if (item.Sid == IntPtr.Zero) continue;
                try
                {
                    result.Add(new SecurityIdentifier(item.Sid).Value);
                }
                catch (ArgumentException)
                {
                }
            }
        }
        finally
        {
            if (buffer != IntPtr.Zero)
                _ = NetApiBufferFree(buffer);
        }

        return result;
    }

    private static string AdministratorsGroupName()
    {
        var sid = new SecurityIdentifier(
            WellKnownSidType.BuiltinAdministratorsSid,
            null);
        var account = (NTAccount)sid.Translate(typeof(NTAccount));
        var value = account.Value;
        var separator = value.LastIndexOf('\\');
        return separator >= 0 ? value[(separator + 1)..] : value;
    }

    private static bool ReadUacEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
            return Convert.ToInt32(key?.GetValue("EnableLUA", 0), CultureInfo.InvariantCulture) == 1;
        }
        catch
        {
            return false;
        }
    }

    private static void HardenConfiguration(string path)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "icacls.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments =
                $"\"{path}\" /inheritance:r /grant:r *S-1-5-18:(F) *S-1-5-32-544:(F) *S-1-5-32-545:(R) /C"
        }) ?? throw new InvalidOperationException("Không thể chạy icacls.");

        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("Không thể khóa quyền cấu hình Account Protection.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UserInfo1
    {
        public IntPtr Name;
        public IntPtr Password;
        public uint PasswordAge;
        public uint Priv;
        public IntPtr HomeDir;
        public IntPtr Comment;
        public uint Flags;
        public IntPtr ScriptPath;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LocalGroupMembersInfo2
    {
        public IntPtr Sid;
        public int SidUsage;
        public IntPtr DomainAndName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LocalGroupMembersInfo3
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string DomainAndName;
    }

    [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetUserEnum(
        string? serverName,
        int level,
        int filter,
        out IntPtr buffer,
        int preferredMaximumLength,
        out int entriesRead,
        out int totalEntries,
        ref int resumeHandle);

    [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetLocalGroupGetMembers(
        string? serverName,
        string localGroupName,
        int level,
        out IntPtr buffer,
        int preferredMaximumLength,
        out int entriesRead,
        out int totalEntries,
        ref IntPtr resumeHandle);

    [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetLocalGroupDelMembers(
        string? serverName,
        string localGroupName,
        int level,
        ref LocalGroupMembersInfo3 buffer,
        int totalEntries);

    [DllImport("Netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);
}
