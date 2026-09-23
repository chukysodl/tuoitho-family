using TuoiTho.Core.Policy;
using System.Globalization;

namespace TuoiTho.Parent;

public sealed class RemotePairingPanel : UserControl
{
    private readonly ParentDesktopController controller;
    private readonly Label result = new() { AutoSize = true, ForeColor = Color.DarkSlateGray, MaximumSize = new Size(760, 0) };
    private readonly Label code = new() { AutoSize = true, Font = new Font("Segoe UI", 24, FontStyle.Bold), ForeColor = Color.DarkBlue, Padding = new Padding(0, 12, 0, 12) };
    private readonly Label mode = new() { AutoSize = true, Font = new Font("Segoe UI", 12, FontStyle.Bold) };
    private readonly Label expiry = new() { AutoSize = true, Font = new Font("Segoe UI", 11, FontStyle.Bold) };
    private readonly Label runtime = new() { AutoSize = true, MaximumSize = new Size(760, 0) };
    private readonly System.Windows.Forms.Timer expiryTimer = new() { Interval = 1000 };
    private DateTimeOffset? expiresAt;

    public RemotePairingPanel(ParentDesktopController controller)
    {
        this.controller = controller;
        Dock = DockStyle.Fill;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28), ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(new Label { Text = "ĐIỀU KHIỂN TỪ XA", AutoSize = true, Font = new Font("Segoe UI", 20, FontStyle.Bold) }, 0, 0);
        root.Controls.Add(new Label { Text = "Ghép nối điện thoại với thiết bị này. Mã chỉ dùng một lần và hết hạn sau 5 phút.", AutoSize = true, Padding = new Padding(0, 12, 0, 12) }, 0, 1);
        var generate = new Button { Text = "Tạo mã ghép nối", AutoSize = true, MinimumSize = new Size(220, 48), Font = new Font("Segoe UI", 12, FontStyle.Bold) };
        generate.Click += async (_, _) => await GenerateAsync();
        root.Controls.Add(generate, 0, 2);
        root.Controls.Add(code, 0, 3);
        var details = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        details.Controls.Add(mode);
        details.Controls.Add(new Label { Text = "M5 an toàn: cấu hình này không thay đổi TestMode hay chính sách cục bộ.", AutoSize = true, MaximumSize = new Size(760, 0) });
        details.Controls.Add(expiry);
        details.Controls.Add(runtime);
        details.Controls.Add(result);
        details.Controls.Add(new Label { Text = "Trên điện thoại, mở trang Tuổi Thơ Remote, đăng nhập tài khoản phụ huynh và nhập mã trước khi đồng hồ về 00:00. Máy này vẫn tự áp dụng chính sách khi ngoại tuyến.", AutoSize = true, MaximumSize = new Size(760, 0), Padding = new Padding(0, 16, 0, 0) });
        root.Controls.Add(details, 0, 4);
        Controls.Add(root);
        expiryTimer.Tick += (_, _) => UpdateExpiry();
        Disposed += (_, _) => expiryTimer.Dispose();
        controller.StatusUpdated += UpdateStatus;
        Disposed += (_, _) => controller.StatusUpdated -= UpdateStatus;
    }

    public event EventHandler? ManualActionStarting;
    public event EventHandler? ManualActionCompleted;

    public async Task GenerateAsync(CancellationToken token = default)
    {
        ManualActionStarting?.Invoke(this, EventArgs.Empty);
        try
        {
            code.Text = string.Empty;
            expiry.Text = string.Empty;
            expiresAt = null;
            expiryTimer.Stop();
            var response = await controller.CreateRemotePairingAsync(token);
            if (response.Status is not null) UpdateStatus(response.Status);
            result.Text = response.Message;
            result.ForeColor = response.Success ? Color.DarkGreen : Color.Firebrick;
            if (response.Success && response.Message?.Contains(':') == true)
            {
                code.Text = response.Message[(response.Message.LastIndexOf(':') + 1)..].Trim();
                result.Text = "Mã chỉ dùng một lần; ghép nối ngay trên điện thoại.";
                expiresAt = DateTimeOffset.Now.AddMinutes(5);
                expiryTimer.Start();
                UpdateExpiry();
            }
        }
        finally { ManualActionCompleted?.Invoke(this, EventArgs.Empty); }
    }

    public void UpdateStatus(ParentControlStatus status)
    {
        mode.Text = status.TestMode ? "CHẾ ĐỘ THỬ NGHIỆM – KHÔNG KHÓA WINDOWS THẬT" : "CHẾ ĐỘ THỰC – KHÓA CÓ THỂ TÁC ĐỘNG THẬT";
        mode.ForeColor = status.TestMode ? Color.DarkGoldenrod : Color.Firebrick;
        if (status.RemoteControl is not { } remote)
        {
            runtime.Text = "Tình trạng dịch vụ từ xa: chưa có dữ liệu kiểm tra.";
            return;
        }
        runtime.Text = $"Remote: {(remote.Enabled ? "BẬT" : "TẮT")} · Thiết bị: {(remote.DeviceIdentityReady ? "SẴN SÀNG" : "CHƯA TẠO")} · Poll: {remote.LastCommandPollAtUtc?.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture) ?? "chưa có"} · Gửi trạng thái: {remote.LastStatusPublishedAtUtc?.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture) ?? "chưa có"} · {remote.LastErrorCode ?? "OK"}";
    }

    private void UpdateExpiry()
    {
        if (expiresAt is not { } end) return;
        var left = end - DateTimeOffset.Now;
        if (left <= TimeSpan.Zero)
        {
            expiry.Text = "MÃ ĐÃ HẾT HẠN — hãy tạo mã mới.";
            code.Text = string.Empty;
            expiresAt = null;
            expiryTimer.Stop();
            return;
        }
        expiry.Text = $"Còn hiệu lực: {Math.Max(0, (int)left.TotalMinutes):00}:{left.Seconds:00} · Chỉ dùng một lần.";
    }
}
