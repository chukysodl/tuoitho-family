using TuoiTho.Core.Policy;

namespace TuoiTho.Parent;

public sealed class RemotePairingPanel : UserControl
{
    private readonly ParentDesktopController controller;
    private readonly Label result = new() { AutoSize = true, ForeColor = Color.DarkSlateGray, MaximumSize = new Size(760, 0) };
    private readonly Label code = new() { AutoSize = true, Font = new Font("Segoe UI", 24, FontStyle.Bold), ForeColor = Color.DarkBlue, Padding = new Padding(0, 12, 0, 12) };
    private readonly Label mode = new() { AutoSize = true, Font = new Font("Segoe UI", 12, FontStyle.Bold) };

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
        details.Controls.Add(result);
        details.Controls.Add(new Label { Text = "Mở trang Tuổi Thơ Remote trên điện thoại và nhập mã. Máy này vẫn tự áp dụng chính sách khi ngoại tuyến.", AutoSize = true, MaximumSize = new Size(760, 0), Padding = new Padding(0, 16, 0, 0) });
        root.Controls.Add(details, 0, 4);
        Controls.Add(root);
    }

    public event EventHandler? ManualActionStarting;
    public event EventHandler? ManualActionCompleted;

    public async Task GenerateAsync(CancellationToken token = default)
    {
        ManualActionStarting?.Invoke(this, EventArgs.Empty);
        try
        {
            code.Text = string.Empty;
            var response = await controller.CreateRemotePairingAsync(token);
            mode.Text = response.Status is null ? "" : response.Status.TestMode ? "CHẾ ĐỘ THỬ NGHIỆM — KHÓA THẬT ĐANG TẮT" : "CHẾ ĐỘ THỰC";
            result.Text = response.Message;
            result.ForeColor = response.Success ? Color.DarkGreen : Color.Firebrick;
            if (response.Success && response.Message.Contains(':')) code.Text = response.Message[(response.Message.LastIndexOf(':') + 1)..].Trim();
        }
        finally { ManualActionCompleted?.Invoke(this, EventArgs.Empty); }
    }
}
