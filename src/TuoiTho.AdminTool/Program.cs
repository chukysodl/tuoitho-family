using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using TuoiTho.Core.Security;

ApplicationConfiguration.Initialize();

var path = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "TuoiTho",
    "parent-auth.json");

var setPassword = args.Contains("--set-password", StringComparer.OrdinalIgnoreCase);
var repairAuth = args.Contains("--repair-auth", StringComparer.OrdinalIgnoreCase);
var authState = args.Contains("--auth-state", StringComparer.OrdinalIgnoreCase);
var verifyPassword = args.Contains("--verify-password", StringComparer.OrdinalIgnoreCase);
var authorizeMaintenance =
    args.Contains("--authorize-maintenance", StringComparer.OrdinalIgnoreCase) ||
    args.Contains("--authorize-uninstall", StringComparer.OrdinalIgnoreCase);

if (authState)
{
    Environment.ExitCode = ReadRecord(path) is not null
        ? 0
        : File.Exists(path) ? 11 : 10;
    return;
}

if (repairAuth)
{
    if (!IsAdministrator())
    {
        RelaunchElevated(args);
        return;
    }

    Environment.ExitCode = RepairAuth(path) ? 0 : 7;
    return;
}

if (setPassword)
{
    if (!IsAdministrator())
    {
        RelaunchElevated(args);
        return;
    }

    Environment.ExitCode = SetPassword(path) ? 0 : 5;
    return;
}

if (verifyPassword || authorizeMaintenance)
{
    if (authorizeMaintenance && !IsAdministrator())
    {
        RelaunchElevated(args);
        return;
    }

    var password = PromptVerifiedPassword(path, "Nhập mật khẩu phụ huynh");
    if (password is null)
    {
        Environment.ExitCode = 5;
        return;
    }

    if (authorizeMaintenance)
    {
        var maintenance = RequestMaintenance(password);
        if (maintenance == MaintenanceRequestResult.Rejected)
        {
            MessageBox.Show(
                "Service không chấp nhận mở chế độ bảo trì. Hãy kiểm tra mật khẩu và chạy lại.",
                "Quản lý thời gian",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Environment.ExitCode = 6;
            return;
        }
    }

    Environment.ExitCode = 0;
    return;
}

MessageBox.Show(
    "Công cụ này được bộ cài Quản lý thời gian sử dụng để thiết lập hoặc xác nhận mật khẩu phụ huynh.",
    "Quản lý thời gian",
    MessageBoxButtons.OK,
    MessageBoxIcon.Information);

static bool RepairAuth(string path)
{
    if (ReadRecord(path) is not null)
    {
        MessageBox.Show(
            "Mật khẩu phụ huynh hiện tại vẫn hợp lệ. Hãy dùng mật khẩu hiện tại để Repair.",
            "Quản lý thời gian",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        return false;
    }

    using var dialog = new SetPasswordForm("Khôi phục mật khẩu phụ huynh");
    if (dialog.ShowDialog() != DialogResult.OK) return false;

    try
    {
        ParentPasswordHasher.ValidatePassword(dialog.Password);
        if (!string.Equals(dialog.Password, dialog.ConfirmPassword, StringComparison.Ordinal))
        {
            MessageBox.Show(
                "Hai lần nhập mật khẩu không khớp.",
                "Quản lý thời gian",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        var record = ParentPasswordHasher.Create(dialog.Password);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        if (File.Exists(path))
        {
            try { File.Delete(path); }
            catch (UnauthorizedAccessException)
            {
                RunAcl(directory, grantAdminFull: true);
                File.Delete(path);
            }
        }

        var temp = path + ".repair.tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(record));
        File.Move(temp, path, true);
        HardenAuthFile(path);

        var maintenance = RequestMaintenance(dialog.Password);
        if (maintenance == MaintenanceRequestResult.Rejected)
        {
            MessageBox.Show(
                "Đã khôi phục mật khẩu nhưng Service chưa chấp nhận chế độ bảo trì. Hãy chạy Setup lại một lần nữa.",
                "Quản lý thời gian",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        MessageBox.Show(
            "Đã khôi phục mật khẩu phụ huynh. Setup sẽ tiếp tục.",
            "Quản lý thời gian",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        return true;
    }
    catch (Exception exception) when (
        exception is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
    {
        MessageBox.Show(
            "Không thể khôi phục mật khẩu: " + exception.Message,
            "Quản lý thời gian",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
        return false;
    }
}

static void HardenAuthFile(string path)
{
    using var process = Process.Start(new ProcessStartInfo
    {
        FileName = "icacls.exe",
        UseShellExecute = false,
        CreateNoWindow = true,
        Arguments = "\"" + path + "\" /inheritance:r /grant:r *S-1-5-18:(F) *S-1-5-32-544:(R) /C"
    }) ?? throw new InvalidOperationException("Không thể chạy icacls.");
    process.WaitForExit();
    if (process.ExitCode != 0)
        throw new InvalidOperationException("Không thể khóa quyền file mật khẩu.");
}

static void RunAcl(string directory, bool grantAdminFull)
{
    var admin = grantAdminFull ? "(OI)(CI)(F)" : "(OI)(CI)(RX)";
    using var process = Process.Start(new ProcessStartInfo
    {
        FileName = "icacls.exe",
        UseShellExecute = false,
        CreateNoWindow = true,
        Arguments = "\"" + directory + "\" /grant:r *S-1-5-32-544:" + admin + " /C"
    }) ?? throw new InvalidOperationException("Không thể chạy icacls.");
    process.WaitForExit();
}

static bool SetPassword(string path)
{
    ParentPasswordRecord? existing = ReadRecord(path);
    if (existing is not null)
    {
        var currentPassword = PromptVerifiedPasswordRecord(existing, "Nhập mật khẩu phụ huynh hiện tại");
        if (currentPassword is null)
        {
            MessageBox.Show("Mật khẩu hiện tại không đúng hoặc thao tác đã bị hủy.", "Quản lý thời gian", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        var maintenance = RequestMaintenance(currentPassword);
        if (maintenance == MaintenanceRequestResult.Rejected)
        {
            MessageBox.Show(
                "Không thể mở chế độ bảo trì để đổi mật khẩu.",
                "Quản lý thời gian",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }
    }

    using var dialog = new SetPasswordForm();
    if (dialog.ShowDialog() != DialogResult.OK) return false;

    try
    {
        ParentPasswordHasher.ValidatePassword(dialog.Password);
        if (!string.Equals(dialog.Password, dialog.ConfirmPassword, StringComparison.Ordinal))
        {
            MessageBox.Show("Hai lần nhập mật khẩu không khớp.", "Quản lý thời gian", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        var record = ParentPasswordHasher.Create(dialog.Password);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(record));
        File.Move(temp, path, true);
        MessageBox.Show("Đã lưu mật khẩu phụ huynh.", "Quản lý thời gian", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return true;
    }
    catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
    {
        MessageBox.Show(exception.Message, "Quản lý thời gian", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return false;
    }
}

static string? PromptVerifiedPassword(string path, string title)
{
    var record = ReadRecord(path);
    if (record is null)
    {
        MessageBox.Show(
            "Chưa có mật khẩu phụ huynh. Hãy chạy thiết lập/repair với quyền quản trị.",
            "Quản lý thời gian",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        return null;
    }

    return PromptVerifiedPasswordRecord(record, title);
}

static string? PromptVerifiedPasswordRecord(ParentPasswordRecord record, string title)
{
    using var dialog = new VerifyPasswordForm(title);
    if (dialog.ShowDialog() != DialogResult.OK)
    {
        return null;
    }

    return ParentPasswordHasher.Verify(dialog.Password, record)
        ? dialog.Password
        : null;
}

static ParentPasswordRecord? ReadRecord(string path)
{
    try
    {
        return File.Exists(path)
            ? JsonSerializer.Deserialize<ParentPasswordRecord>(File.ReadAllText(path))
            : null;
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
    {
        return null;
    }
}

static MaintenanceRequestResult RequestMaintenance(string password)
{
    try
    {
        using var pipe = new NamedPipeClientStream(
            ".",
            TamperMaintenanceProtocol.PipeName,
            PipeDirection.InOut,
            PipeOptions.None);

        pipe.Connect(2500);

        using var reader = new StreamReader(pipe, Encoding.UTF8, true, 1024, leaveOpen: true);
        using var writer = new StreamWriter(pipe, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };

        var request = new TamperMaintenanceRequest(
            TamperMaintenanceProtocol.AuthorizeCommand,
            password,
            TamperMaintenanceProtocol.DefaultDurationSeconds);

        writer.WriteLine(JsonSerializer.Serialize(request));
        var responseLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(responseLine))
        {
            return MaintenanceRequestResult.Rejected;
        }

        var response = JsonSerializer.Deserialize<TamperMaintenanceResponse>(responseLine);
        return response?.Authorized == true
            ? MaintenanceRequestResult.Authorized
            : MaintenanceRequestResult.Rejected;
    }
    catch (Exception exception) when (
        exception is IOException or TimeoutException or UnauthorizedAccessException or JsonException)
    {
        // Compatibility path for an older/unprotected service or a partially removed install.
        // The caller still had to pass the parent password before reaching this point.
        return MaintenanceRequestResult.ServiceUnavailable;
    }
}

static bool IsAdministrator()
{
    using var identity = WindowsIdentity.GetCurrent();
    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
}

static void RelaunchElevated(string[] currentArgs)
{
    var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path is unavailable.");
    var start = new ProcessStartInfo
    {
        FileName = executable,
        UseShellExecute = true,
        Verb = "runas",
        Arguments = string.Join(" ", currentArgs.Select(Quote))
    };
    Process.Start(start);
}

static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

enum MaintenanceRequestResult
{
    Authorized,
    ServiceUnavailable,
    Rejected
}

sealed class SetPasswordForm : Form
{
    private readonly TextBox first = PasswordBox();
    private readonly TextBox second = PasswordBox();

    public SetPasswordForm(string title = "Thiết lập mật khẩu phụ huynh")
    {
        Text = title;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(410, 205);
        Font = new Font("Segoe UI", 10);

        var heading = new Label { Text = "Mật khẩu dùng để thay đổi hoặc gỡ bảo vệ", AutoSize = true, Location = new Point(24, 18), Font = new Font("Segoe UI", 10, FontStyle.Bold) };
        var label1 = new Label { Text = "Mật khẩu (ít nhất 6 ký tự)", AutoSize = true, Location = new Point(24, 52) };
        first.Location = new Point(24, 74);
        var label2 = new Label { Text = "Nhập lại mật khẩu", AutoSize = true, Location = new Point(24, 108) };
        second.Location = new Point(24, 130);
        var ok = new Button { Text = "Lưu mật khẩu", DialogResult = DialogResult.OK, Location = new Point(262, 163), Size = new Size(120, 32) };
        Controls.AddRange([heading, label1, first, label2, second, ok]);
        AcceptButton = ok;
    }

    public string Password => first.Text;
    public string ConfirmPassword => second.Text;
    private static TextBox PasswordBox() => new() { Width = 358, UseSystemPasswordChar = true };
}

sealed class VerifyPasswordForm : Form
{
    private readonly TextBox password = new() { Width = 300, UseSystemPasswordChar = true };

    public VerifyPasswordForm(string prompt)
    {
        Text = "Xác nhận phụ huynh";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(360, 145);
        Font = new Font("Segoe UI", 10);

        Controls.Add(new Label { Text = prompt, AutoSize = true, Location = new Point(24, 18) });
        password.Location = new Point(24, 47);
        var ok = new Button { Text = "Xác nhận", DialogResult = DialogResult.OK, Location = new Point(166, 94), Size = new Size(92, 32) };
        var cancel = new Button { Text = "Hủy", DialogResult = DialogResult.Cancel, Location = new Point(266, 94), Size = new Size(70, 32) };
        Controls.AddRange([password, ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public string Password => password.Text;
}
