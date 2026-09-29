using System.Text.Json;
using TuoiTho.Core.Security;

namespace TuoiTho.Parent;

public interface IParentAuthorizationGate
{
    bool EnsureAuthorized(IWin32Window? owner = null);
    void LockAuthorization();
}

public sealed class ParentAuthorizationGate : IParentAuthorizationGate
{
    private readonly string verifierPath;
    private readonly string productionConfigPath;
    private readonly TimeProvider timeProvider;
    private DateTimeOffset authorizedUntilUtc = DateTimeOffset.MinValue;

    public ParentAuthorizationGate(
        string? verifierPath = null,
        string? productionConfigPath = null,
        TimeProvider? timeProvider = null)
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var root = Path.Combine(programData, "TuoiTho");
        this.verifierPath = verifierPath ?? Path.Combine(root, "parent-auth.json");
        this.productionConfigPath = productionConfigPath ?? Path.Combine(root, "production.json");
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool EnsureAuthorized(IWin32Window? owner = null)
    {
        var now = timeProvider.GetUtcNow();
        if (now < authorizedUntilUtc) return true;

        if (!File.Exists(verifierPath))
        {
            if (!ProductionProtectionEnabled()) return true;

            MessageBox.Show(
                owner,
                "Không tìm thấy cấu hình mật khẩu phụ huynh. Các thay đổi đang bị khóa để bảo vệ thiết bị.",
                "Quản lý thời gian",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        ParentPasswordRecord? record;
        try
        {
            record = JsonSerializer.Deserialize<ParentPasswordRecord>(File.ReadAllText(verifierPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            record = null;
        }

        if (record is null)
        {
            MessageBox.Show(owner, "Cấu hình mật khẩu phụ huynh không hợp lệ.", "Quản lý thời gian", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        using var dialog = new ParentPasswordDialog();
        if (dialog.ShowDialog(owner) != DialogResult.OK) return false;

        if (!ParentPasswordHasher.Verify(dialog.Password, record))
        {
            MessageBox.Show(owner, "Mật khẩu phụ huynh không đúng.", "Quản lý thời gian", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        authorizedUntilUtc = now.AddMinutes(5);
        return true;
    }

    public void LockAuthorization() => authorizedUntilUtc = DateTimeOffset.MinValue;

    private bool ProductionProtectionEnabled()
    {
        if (!File.Exists(productionConfigPath)) return false;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(productionConfigPath));
            return document.RootElement.TryGetProperty("ProductionProtection", out var section)
                && section.ValueKind == JsonValueKind.Object
                && section.TryGetProperty("Enabled", out var enabled)
                && enabled.ValueKind is JsonValueKind.True or JsonValueKind.False
                && enabled.GetBoolean();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return true;
        }
    }
}

internal sealed class ParentPasswordDialog : Form
{
    private readonly TextBox password = new()
    {
        Width = 280,
        UseSystemPasswordChar = true,
        Font = new Font("Segoe UI", 11)
    };

    public ParentPasswordDialog()
    {
        Text = "Xác nhận phụ huynh";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(360, 155);
        Font = new Font("Segoe UI", 10);

        var label = new Label
        {
            Text = "Nhập mật khẩu phụ huynh để thay đổi cài đặt:",
            AutoSize = true,
            Location = new Point(24, 20)
        };
        password.Location = new Point(24, 50);

        var ok = new Button
        {
            Text = "Xác nhận",
            DialogResult = DialogResult.OK,
            Location = new Point(165, 100),
            Size = new Size(92, 34)
        };
        var cancel = new Button
        {
            Text = "Hủy",
            DialogResult = DialogResult.Cancel,
            Location = new Point(265, 100),
            Size = new Size(70, 34)
        };

        Controls.AddRange([label, password, ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public string Password => password.Text;
}
