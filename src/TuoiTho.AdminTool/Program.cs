using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using TuoiTho.Core.Security;

ApplicationConfiguration.Initialize();

var path = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "TuoiTho",
    "parent-auth.json");

if (args.Contains("--set-password", StringComparer.OrdinalIgnoreCase))
{
    if (!IsAdministrator())
    {
        RelaunchElevated(args);
        return;
    }

    Environment.ExitCode = SetPassword(path) ? 0 : 5;
    return;
}

if (args.Contains("--verify-password", StringComparer.OrdinalIgnoreCase) ||
    args.Contains("--authorize-uninstall", StringComparer.OrdinalIgnoreCase))
{
    Environment.ExitCode = VerifyPassword(path) ? 0 : 5;
    return;
}

MessageBox.Show(
    "Công cụ này được bộ cài Quản lý thời gian sử dụng để thiết lập hoặc xác nhận mật khẩu phụ huynh.",
    "Quản lý thời gian",
    MessageBoxButtons.OK,
    MessageBoxIcon.Information);

static bool SetPassword(string path)
{
    ParentPasswordRecord? existing = ReadRecord(path);
    if (existing is not null && !PromptAndVerify(existing, "Nhập mật khẩu phụ huynh hiện tại"))
    {
        MessageBox.Show("Mật khẩu hiện tại không đúng.", "Quản lý thời gian", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return false;
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

static bool VerifyPassword(string path)
{
    var record = ReadRecord(path);
    if (record is null)
    {
        MessageBox.Show(
            "Chưa có mật khẩu phụ huynh. Hãy chạy thiết lập/repair với quyền quản trị.",
            "Quản lý thời gian",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        return false;
    }

    return PromptAndVerify(record, "Nhập mật khẩu phụ huynh");
}

static bool PromptAndVerify(ParentPasswordRecord record, string title)
{
    using var dialog = new VerifyPasswordForm(title);
    return dialog.ShowDialog() == DialogResult.OK &&
           ParentPasswordHasher.Verify(dialog.Password, record);
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

static string Quote(string value) => """ + value.Replace(""", "\\"", StringComparison.Ordinal) + """;

sealed class SetPasswordForm : Form
{
    private readonly TextBox first = PasswordBox();
    private readonly TextBox second = PasswordBox();

    public SetPasswordForm()
    {
        Text = "Thiết lập mật khẩu phụ huynh";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(410, 205);
        Font = new Font("Segoe UI", 10);

        var title = new Label { Text = "Mật khẩu dùng để thay đổi hoặc gỡ bảo vệ", AutoSize = true, Location = new Point(24, 18), Font = new Font("Segoe UI", 10, FontStyle.Bold) };
        var label1 = new Label { Text = "Mật khẩu (ít nhất 6 ký tự)", AutoSize = true, Location = new Point(24, 52) };
        first.Location = new Point(24, 74);
        var label2 = new Label { Text = "Nhập lại mật khẩu", AutoSize = true, Location = new Point(24, 108) };
        second.Location = new Point(24, 130);
        var ok = new Button { Text = "Lưu mật khẩu", DialogResult = DialogResult.OK, Location = new Point(262, 163), Size = new Size(120, 32) };
        Controls.AddRange([title, label1, first, label2, second, ok]);
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
