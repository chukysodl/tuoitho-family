using System.Globalization;
using TuoiTho.Core.Policy;

namespace TuoiTho.Parent;

public sealed class ParentControlForm : Form
{
    private readonly ParentDesktopController controller;
    private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 1000 };
    private readonly Dictionary<string, Label> values = [];
    private readonly Label message = new() { AutoSize = true, Font = new Font("Segoe UI", 11, FontStyle.Bold), Padding = new Padding(8) };
    private readonly Label countdown = new() { AutoSize = true, Text = "00:00:00", Font = new Font("Segoe UI", 30, FontStyle.Bold), ForeColor = Color.DarkBlue, Padding = new Padding(8) };
    private Button? resetButton;
    private bool refreshing;

    public ParentControlForm(ParentDesktopController controller)
    {
        this.controller = controller;
        Text = "Tuổi Thơ – Điều khiển phụ huynh (M1)";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 640);
        Font = new Font("Segoe UI", 10);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 2, AutoScroll = true, RowCount = 0 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        Controls.Add(root);
        AddWide(root, new Label { Text = "Tuổi Thơ – Điều khiển phụ huynh", AutoSize = true, Font = new Font("Segoe UI", 16, FontStyle.Bold) });
        AddWide(root, new Label { Text = "THỜI GIAN CÒN LẠI", AutoSize = true, Font = new Font("Segoe UI", 13, FontStyle.Bold), ForeColor = Color.DarkBlue, Padding = new Padding(8, 16, 8, 0) });
        AddWide(root, countdown);

        foreach (var item in new[]
                 {
                     ("Hồ sơ", "Profile"), ("Phiên Windows", "Session"), ("Chế độ an toàn", "TestMode"), ("Đã dùng hôm nay", "Used"), ("Hạn mức mỗi ngày", "Quota"), ("Tổng phút được cộng", "Grants"), ("Còn lại (phút)", "Remaining"), ("Trạng thái", "State"),
                     ("SessionAgent", "Agent"), ("Hoạt động", "Activity"), ("Nguồn hoạt động", "ActivitySource"), ("Nhàn rỗi thiết bị", "Idle"), ("Nhàn rỗi Windows", "WindowsIdle"), ("Tuổi mẫu", "SampleAge"), ("Phiên theo dõi", "TrackedSession"), ("Đã ghi hôm nay", "RecordedSeconds"), ("Lần ghi cuối", "Checkpoint"), ("Khóa rõ ràng", "LockLatch"), ("WTS kết nối", "WtsConnection"), ("WTS SessionFlags", "WtsFlags"), ("Thông báo phiên", "Notifications"), ("Lỗi thông báo", "NotificationError"), ("Ngưỡng nhàn rỗi", "IdleThreshold")
                 })
        {
            AddStatus(root, item.Item1, item.Item2);
        }

        AddWide(root, message);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0) };
        AddWide(root, buttons);
        AddButton(buttons, "Làm mới trạng thái", RefreshAsync);
        AddButton(buttons, "+15 phút", () => RunAsync(ParentControlAction.GrantMinutes, 15));
        AddButton(buttons, "+30 phút", () => RunAsync(ParentControlAction.GrantMinutes, 30));
        AddButton(buttons, "+60 phút", () => RunAsync(ParentControlAction.GrantMinutes, 60));
        AddButton(buttons, "Cộng phút tùy chọn", CustomGrantAsync);
        AddButton(buttons, "Bật Override khẩn cấp", () => RunAsync(ParentControlAction.EmergencyOverride));
        AddButton(buttons, "Tắt Override", () => RunAsync(ParentControlAction.ClearOverride));
        AddButton(buttons, "Khóa bởi phụ huynh", () => RunAsync(ParentControlAction.SetParentLock));
        AddButton(buttons, "Bỏ khóa phụ huynh", () => RunAsync(ParentControlAction.ClearParentLock));
        resetButton = AddButton(buttons, "Đặt lại thử nghiệm M1", () => RunAsync(ParentControlAction.ResetM1));
        resetButton.Visible = false;
        AddButton(buttons, "Thoát", () => { Close(); return Task.CompletedTask; });

        refreshTimer.Tick += async (_, _) => await RefreshAsync();
        Shown += async (_, _) => { await RefreshAsync(); refreshTimer.Start(); };
        FormClosed += (_, _) => { refreshTimer.Stop(); refreshTimer.Dispose(); };
    }

    public static string FormatCountdown(double remainingSeconds)
    {
        var totalSeconds = Math.Max(0, (long)Math.Floor(remainingSeconds));
        return $"{totalSeconds / 3600:D2}:{totalSeconds % 3600 / 60:D2}:{totalSeconds % 60:D2}";
    }

    private static void AddWide(TableLayoutPanel panel, Control control)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(control, 0, row);
        panel.SetColumnSpan(control, 2);
    }

    private void AddStatus(TableLayoutPanel panel, string label, string key)
    {
        var name = new Label { Text = label, AutoSize = true, Padding = new Padding(4) };
        var value = new Label { Text = "—", AutoSize = true, Padding = new Padding(4), Font = new Font("Segoe UI", 10, FontStyle.Bold) };
        values[key] = value;
        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(name, 0, row);
        panel.Controls.Add(value, 1, row);
    }

    private static Button AddButton(FlowLayoutPanel panel, string label, Func<Task> action)
    {
        var button = new Button { Text = label, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(150, 46), Margin = new Padding(5), UseVisualStyleBackColor = true };
        button.Click += async (_, _) => await action();
        panel.Controls.Add(button);
        return button;
    }

    private async Task CustomGrantAsync()
    {
        using var dialog = new Form { Text = "Cộng phút tùy chọn", StartPosition = FormStartPosition.CenterParent, Size = new Size(330, 155), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var input = new NumericUpDown { Minimum = 1, Maximum = 1440, Value = 15, Location = new Point(20, 20), Width = 130 };
        var ok = new Button { Text = "Cộng phút", DialogResult = DialogResult.OK, Location = new Point(170, 18) };
        dialog.Controls.AddRange([new Label { Text = "Số phút:", Location = new Point(20, 0), AutoSize = true }, input, ok]);
        dialog.AcceptButton = ok;
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            await RunAsync(ParentControlAction.GrantMinutes, (int)input.Value);
        }
    }

    private async Task RunAsync(ParentControlAction action, int? minutes = null) => await DisplayAsync(await controller.SendAsync(action, minutes));

    private async Task RefreshAsync()
    {
        if (refreshing)
        {
            return;
        }

        refreshing = true;
        try
        {
            await DisplayAsync(await controller.RefreshAsync());
        }
        finally
        {
            refreshing = false;
        }
    }

    private Task DisplayAsync(ParentUiResult result)
    {
        message.Text = result.Message;
        message.ForeColor = result.Success ? Color.DarkGreen : Color.Firebrick;
        if (result.Status is not { } status)
        {
            return Task.CompletedTask;
        }

        countdown.Text = FormatCountdown(status.RemainingSeconds);
        values["Profile"].Text = status.ProfileId;
        values["Session"].Text = status.ManagedSessionId.ToString(CultureInfo.CurrentCulture);
        values["TestMode"].Text = status.TestMode ? "BẬT (không khóa thật)" : "TẮT";
        values["Used"].Text = $"{status.UsedMinutes} phút";
        values["Quota"].Text = $"{status.QuotaMinutes} phút";
        values["Grants"].Text = $"{status.GrantMinutes} phút";
        values["Remaining"].Text = $"{status.RemainingMinutes} phút";
        values["State"].Text = status.State;
        if (status.Diagnostics is { } diagnostics)
        {
            values["Agent"].Text = diagnostics.SessionAgentConnected ? "KẾT NỐI" : "MẤT KẾT NỐI";
            values["Activity"].Text = diagnostics.ActivityState;
            values["ActivitySource"].Text = diagnostics.ActivitySource;
            values["Idle"].Text = diagnostics.IdleSeconds is { } idle ? $"{idle:F1} giây" : "—";
            values["WindowsIdle"].Text = diagnostics.WindowsIdleSeconds is { } windowsIdle ? $"{windowsIdle:F1} giây" : "—";
            values["SampleAge"].Text = diagnostics.SampleAgeSeconds is { } age ? $"{age:F1} giây" : "—";
            values["TrackedSession"].Text = diagnostics.TrackedSessionId.ToString(CultureInfo.CurrentCulture);
            values["RecordedSeconds"].Text = $"{diagnostics.RecordedTodaySeconds:F1} giây";
            values["Checkpoint"].Text = diagnostics.LastCheckpointAtUtc?.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture) ?? "—";
            values["LockLatch"].Text = diagnostics.ExplicitLockLatched ? "LOCKED" : "UNLOCKED";
            values["WtsConnection"].Text = diagnostics.WtsConnectionState?.ToString(CultureInfo.CurrentCulture) ?? "—";
            values["WtsFlags"].Text = diagnostics.WtsSessionFlags?.ToString(CultureInfo.CurrentCulture) ?? "—";
            values["Notifications"].Text = diagnostics.SessionNotificationsAvailable ? "AVAILABLE" : "DEGRADED";
            values["NotificationError"].Text = diagnostics.NotificationError ?? "—";
            values["IdleThreshold"].Text = $"{diagnostics.IdleThresholdMinutes} phút";
        }

        if (resetButton is not null)
        {
            resetButton.Visible = status.TestMode && status.ProfileId == "m1-child";
        }

        return Task.CompletedTask;
    }
}