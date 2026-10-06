namespace TuoiTho.Parent;

public sealed class AccountProtectionPanel : UserControl
{
    private readonly IParentAuthorizationGate authorization;
    private readonly Label overall = new()
    {
        AutoSize = true,
        Font = new Font("Segoe UI", 13, FontStyle.Bold),
        Padding = new Padding(0, 0, 0, 8)
    };
    private readonly Label details = new()
    {
        AutoSize = true,
        MaximumSize = new Size(900, 0),
        Padding = new Padding(0, 0, 0, 10)
    };
    private readonly DataGridView accounts = new()
    {
        Dock = DockStyle.Fill,
        AutoGenerateColumns = false,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        ReadOnly = true,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false
    };
    private readonly Label message = new() { AutoSize = true, Padding = new Padding(6) };

    public AccountProtectionPanel(
        IParentAuthorizationGate authorization)
    {
        this.authorization = authorization;
        Dock = DockStyle.Fill;

        accounts.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Account",
            HeaderText = "Tài khoản Windows",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        accounts.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Type",
            HeaderText = "Loại",
            Width = 140
        });
        accounts.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "State",
            HeaderText = "Trạng thái",
            Width = 150
        });
        accounts.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Managed",
            HeaderText = "Bảo vệ",
            Width = 150
        });

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            RowCount = 6,
            ColumnCount = 1
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(new Label
        {
            Text = "ACCOUNT PROTECTION MODE",
            AutoSize = true,
            Font = new Font("Segoe UI", 16, FontStyle.Bold)
        }, 0, 0);

        root.Controls.Add(overall, 0, 1);
        root.Controls.Add(details, 0, 2);
        root.Controls.Add(accounts, 0, 3);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(0, 10, 0, 0)
        };

        var apply = new Button
        {
            Text = "Đặt làm tài khoản trẻ",
            AutoSize = true,
            MinimumSize = new Size(190, 40)
        };
        apply.Click += (_, _) => ApplySelected();

        var refresh = new Button
        {
            Text = "Kiểm tra lại",
            AutoSize = true,
            MinimumSize = new Size(120, 40)
        };
        refresh.Click += (_, _) => RefreshState();

        var settings = new Button
        {
            Text = "Mở cài đặt tài khoản Windows",
            AutoSize = true,
            MinimumSize = new Size(210, 40)
        };
        settings.Click += (_, _) => AccountProtectionManager.OpenWindowsAccountsSettings();

        actions.Controls.AddRange([apply, refresh, settings]);
        root.Controls.Add(actions, 0, 4);
        root.Controls.Add(message, 0, 5);
        Controls.Add(root);

        VisibleChanged += (_, _) =>
        {
            if (Visible) RefreshState();
        };

        RefreshState();
    }

    public void RefreshState()
    {
        try
        {
            var snapshot = AccountProtectionManager.Snapshot();

            overall.Text = snapshot.Safe
                ? "BẢO VỆ TÀI KHOẢN: AN TOÀN"
                : "BẢO VỆ TÀI KHOẢN: CHƯA AN TOÀN";
            overall.ForeColor = snapshot.Safe ? Color.DarkGreen : Color.Firebrick;

            var managed = string.IsNullOrWhiteSpace(snapshot.ManagedChildName)
                ? "chưa chọn"
                : snapshot.ManagedChildName;

            details.Text =
                $"Tài khoản phụ huynh hiện tại: {snapshot.CurrentUser}\r\n" +
                $"UAC Windows: {(snapshot.UacEnabled ? "BẬT" : "TẮT")}\r\n" +
                $"Tài khoản trẻ đang quản lý: {managed}\r\n\r\n" +
                "Mục tiêu của chế độ này: tài khoản trẻ phải là Standard User. " +
                "Khi đó IObit, Revo, Services, Registry và các thao tác quản trị phải yêu cầu " +
                "thông tin Administrator của phụ huynh. Phần mềm không tuyên bố có thể ngăn " +
                "một Administrator đã được cấp đầy đủ quyền cưỡng bức gỡ.";

            accounts.Rows.Clear();
            foreach (var account in snapshot.Accounts)
            {
                var row = accounts.Rows.Add(
                    account.Name,
                    account.IsAdministrator ? "Administrator" : "Standard User",
                    account.Enabled ? "Đang bật" : "Vô hiệu hóa",
                    string.Equals(
                        account.Sid,
                        snapshot.ManagedChildSid,
                        StringComparison.OrdinalIgnoreCase)
                        ? "TÀI KHOẢN TRẺ"
                        : account.IsCurrent ? "Đang đăng nhập" : "");

                accounts.Rows[row].Tag = account;
                if (string.Equals(
                        account.Sid,
                        snapshot.ManagedChildSid,
                        StringComparison.OrdinalIgnoreCase))
                {
                    accounts.Rows[row].Selected = true;
                }
            }

            message.ForeColor = snapshot.Safe ? Color.DarkGreen : Color.Firebrick;
            message.Text = snapshot.Safe
                ? "Tài khoản trẻ đã tách khỏi nhóm Administrators."
                : BuildUnsafeReason(snapshot);
        }
        catch (Exception exception)
        {
            overall.Text = "BẢO VỆ TÀI KHOẢN: KHÔNG ĐỌC ĐƯỢC";
            overall.ForeColor = Color.Firebrick;
            message.ForeColor = Color.Firebrick;
            message.Text = exception.Message;
        }
    }

    private void ApplySelected()
    {
        if (accounts.SelectedRows.Count != 1 ||
            accounts.SelectedRows[0].Tag is not LocalWindowsAccount selected)
        {
            message.ForeColor = Color.Firebrick;
            message.Text = "Hãy chọn một tài khoản Windows dành cho trẻ.";
            return;
        }

        if (!authorization.EnsureAuthorized(FindForm()))
            return;

        var confirm = MessageBox.Show(
            FindForm(),
            $"Đặt tài khoản '{selected.Name}' làm tài khoản trẻ?\r\n\r\n" +
            "Nếu tài khoản này đang là Administrator, quyền Administrator sẽ bị gỡ. " +
            "Tài khoản phụ huynh hiện tại sẽ không bị thay đổi.",
            "Account Protection Mode",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (confirm != DialogResult.Yes)
            return;

        var result = AccountProtectionManager.ConfigureChildWithElevation(selected.Sid, FindForm());
        message.ForeColor = result.Success ? Color.DarkGreen : Color.Firebrick;
        message.Text = result.Message;
        RefreshState();
    }

    private static string BuildUnsafeReason(AccountProtectionSnapshot snapshot)
    {
        if (!snapshot.UacEnabled)
            return "UAC của Windows đang tắt. Cần bật UAC để Standard User không thể tự nâng quyền.";
        if (string.IsNullOrWhiteSpace(snapshot.ManagedChildSid))
            return "Chưa chọn tài khoản trẻ. Chọn một tài khoản khác tài khoản phụ huynh rồi bấm 'Đặt làm tài khoản trẻ'.";
        if (!snapshot.ManagedChildExists)
            return "Tài khoản trẻ đã cấu hình không còn tồn tại trên máy.";
        if (snapshot.ManagedChildIsAdministrator)
            return "Tài khoản trẻ vẫn còn quyền Administrator; IObit vẫn có thể dùng quyền cao để gỡ bảo vệ.";
        return "Cấu hình tài khoản chưa đạt điều kiện bảo vệ.";
    }
}
