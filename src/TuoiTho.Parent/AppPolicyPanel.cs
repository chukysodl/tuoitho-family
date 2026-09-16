using System.ComponentModel;
using System.Globalization;
using TuoiTho.Core.Policy;

namespace TuoiTho.Parent;

public sealed class AppPolicyPanel : UserControl
{
    private readonly ParentDesktopController controller;
    private readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AutoGenerateColumns = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle
    };
    private readonly Label empty = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe UI", 11),
        Text = EmptyStateText,
        Visible = false
    };
    private readonly Label scan = new() { AutoSize = true, Padding = new Padding(4) };
    private readonly Label confirmation = new() { AutoSize = true, Padding = new Padding(4) };
    private readonly CheckBox showBackground = new() { Text = "Hiển thị ứng dụng nền", AutoSize = true };
    private readonly Label enforcementTitle = new() { Text = "DANH SÁCH CHO PHÉP: TẮT", AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold), Padding = new Padding(8, 4, 0, 0), Visible = false };
    private readonly Label enforcementNotice = new() { Text = "CHẾ ĐỘ M2:\r\nChỉ ứng dụng đã được phụ huynh cho phép mới được chạy.", AutoSize = true, ForeColor = Color.DarkOrange, Padding = new Padding(4), Visible = false };
    private readonly Button enableEnforcement = new() { Text = "Bật danh sách cho phép thử nghiệm", AutoSize = true, Visible = false };
    private readonly Button disableEnforcement = new() { Text = "Tắt danh sách cho phép thử nghiệm", AutoSize = true, Visible = false };
    private readonly Button bulkAllow = new() { Text = "Cho phép các ứng dụng đang mở", AutoSize = true, Visible = false };
    private readonly Label lease = new() { AutoSize = true, Padding = new Padding(4), ForeColor = Color.DarkOrange, Visible = false };
    private readonly Button allow = new() { Text = "✓ Cho phép", Enabled = false, AutoSize = true };
    private readonly Button block = new() { Text = "⛔ Chặn", Enabled = false, AutoSize = true };
    private readonly Button remove = new() { Text = "Xóa quy tắc", Enabled = false, AutoSize = true };
    private IReadOnlyList<ParentObservedApp> allObserved = [];
    private ParentObservedApp[] visibleObserved = [];
    private ParentAppControlStatus? currentStatus;

    public AppPolicyPanel(ParentDesktopController controller)
    {
        this.controller = controller;
        Dock = DockStyle.Fill;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), RowCount = 5, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label { Text = "QUẢN LÝ ỨNG DỤNG", AutoSize = true, Font = new Font("Segoe UI", 16, FontStyle.Bold) }, 0, 0);
        root.Controls.Add(new Label { Text = "Ứng dụng chưa được phụ huynh duyệt sẽ bị chặn mặc định.", AutoSize = true, Padding = new Padding(0, 4, 0, 12) }, 0, 1);

        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        var refresh = new Button { Text = "Làm mới danh sách", AutoSize = true };
        refresh.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        bulkAllow.Click += async (_, _) => await RunManualAsync(BulkAllowAsync);
        allow.Click += async (_, _) => await RunManualAsync(() => ApplyAsync(ParentControlAction.AllowApp, "Đã cho phép"));
        block.Click += async (_, _) => await RunManualAsync(() => ApplyAsync(ParentControlAction.BlockApp, "Đã chặn"));
        remove.Click += async (_, _) => await RunManualAsync(() => ApplyAsync(ParentControlAction.RemoveAppRule, "Đã xóa quy tắc của"));
        showBackground.CheckedChanged += (_, _) => Render();
        enableEnforcement.Click += async (_, _) => await RunManualAsync(() => SetEnforcementAsync(true));
        disableEnforcement.Click += async (_, _) => await RunManualAsync(() => SetEnforcementAsync(false));
        actions.Controls.AddRange([refresh, allow, block, remove, showBackground, bulkAllow, enforcementTitle, enableEnforcement, disableEnforcement, enforcementNotice, lease, scan, confirmation]);
        root.Controls.Add(actions, 0, 2);

        var holder = new Panel { Dock = DockStyle.Fill };
        holder.Controls.Add(grid);
        holder.Controls.Add(empty);
        root.Controls.Add(holder, 0, 3);
        Controls.Add(root);
        AddColumn("Ứng dụng", "Name");
        AddColumn("Tệp chạy", "Path");
        AddColumn("Quy tắc", "Rule");
        AddColumn("Kết quả mô phỏng", "Simulation");
        AddColumn("Thực thi", "Enforcement");
        AddColumn("Lần thấy gần nhất", "Seen");
        grid.SelectionChanged += (_, _) => SetButtons();
    }

    public event EventHandler? RefreshRequested;
    public event EventHandler? ManualActionStarting;
    public event EventHandler? ManualActionCompleted;
    public static string EmptyStateText => "Chưa phát hiện ứng dụng nào.\r\n\r\nBạn hãy mở một ứng dụng trên máy, sau đó bấm ‘Làm mới danh sách’.";
    public bool ActionsEnabled => allow.Enabled && block.Enabled && remove.Enabled;
    public int VisibleAppCount => visibleObserved.Length;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowBackgroundHelpers { get => showBackground.Checked; set => showBackground.Checked = value; }

    public static IReadOnlyList<ParentObservedApp> DeduplicateByExecutableIdentity(IEnumerable<ParentObservedApp> apps) => apps
        .GroupBy(app => app.Identity.NormalizedExecutablePath, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.OrderByDescending(app => app.LastSeenUtc).First()).ToArray();

    public static string RuleText(ParentObservedApp app) => app.ExplicitRule switch
    {
        AppRuleDecision.Allow => "CHO PHÉP",
        AppRuleDecision.Block => "CHẶN",
        _ => "CHƯA DUYỆT"
    };

    public static string SimulationText(AppSimulationDecision decision) => decision switch
    {
        AppSimulationDecision.WouldAllow => "ĐƯỢC PHÉP",
        AppSimulationDecision.WouldBlock => "SẼ BỊ CHẶN",
        AppSimulationDecision.BlockUnknown => "CHƯA DUYỆT — SẼ BỊ CHẶN",
        AppSimulationDecision.BackgroundAllowed => "ỨNG DỤNG NỀN — ĐƯỢC PHÉP",
        AppSimulationDecision.SystemAllowed => "HỆ THỐNG — ĐƯỢC BẢO VỆ",
        _ => "CHƯA DUYỆT"
    };

    public void SelectRow(int index)
    {
        if (index < 0 || index >= grid.Rows.Count)
        {
            grid.ClearSelection();
            SetButtons();
            return;
        }

        grid.ClearSelection();
        grid.Rows[index].Selected = true;
        grid.CurrentCell = grid.Rows[index].Cells[0];
        SetButtons();
    }

    public void Update(ParentAppControlStatus? status)
    {
        currentStatus = status;
        allObserved = status?.ObservedApps ?? [];
        Render();
    }

    private void Render()
    {
        var selected = Selected()?.Identity.NormalizedExecutablePath;
        visibleObserved = allObserved
            .Where(app => app.Classification != AppClassification.SystemProtected)
            .Where(app => app.Classification == AppClassification.UserApplication || showBackground.Checked)
            .OrderBy(app => app.Identity.DisplayLabel, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        grid.Rows.Clear();
        foreach (var app in visibleObserved)
        {
            grid.Rows.Add(
                app.Identity.DisplayLabel,
                app.Identity.NormalizedExecutablePath,
                RuleText(app),
                SimulationText(app.Decision),
                app.EnforcementStatus ?? string.Empty,
                app.LastSeenUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture));
        }

        var discovery = currentStatus?.Discovery;
        empty.Text = discovery?.DiscoveryError is { Length: > 0 }
            ? "Không thể quét ứng dụng. Xem Chẩn đoán M1."
            : visibleObserved.Length == 0
                ? "Không tìm thấy ứng dụng người dùng trong phiên này."
                : EmptyStateText;
        empty.Visible = visibleObserved.Length == 0;
        var canUseM2Arming = currentStatus?.TestMode == true;
        var armed = currentStatus?.Enforcement?.Armed == true;
        var allowlist = currentStatus?.Enforcement?.Mode == AppEnforcementMode.AllowlistProduction;
        enforcementNotice.Visible = canUseM2Arming;
        enforcementTitle.Visible = canUseM2Arming;
        enableEnforcement.Visible = canUseM2Arming;
        disableEnforcement.Visible = canUseM2Arming;
        bulkAllow.Visible = canUseM2Arming;
        enableEnforcement.Enabled = canUseM2Arming && !armed;
        disableEnforcement.Enabled = canUseM2Arming && armed;
        enforcementTitle.Text = armed && allowlist ? "DANH SÁCH CHO PHÉP: ĐANG BẬT" : "DANH SÁCH CHO PHÉP: TẮT";
        lease.Visible = armed && allowlist;
        lease.Text = lease.Visible ? $"Thời gian an toàn còn lại: {TimeSpan.FromSeconds(currentStatus?.Enforcement?.LeaseRemainingSeconds ?? 0):mm\\:ss}" : string.Empty;
        scan.Text = discovery is { }
            ? $"Đã quét: {discovery.ProcessesExamined} process; ứng dụng: {discovery.UserApplications}; nền: {discovery.BackgroundHelpers}; hệ thống: {discovery.SystemProtected}; bỏ qua: {discovery.AppsSkippedInaccessible}"
            : string.Empty;

        grid.ClearSelection();
        grid.CurrentCell = null;
        if (selected is not null)
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (!string.Equals((string?)row.Cells["Path"].Value, selected, StringComparison.OrdinalIgnoreCase)) continue;
                row.Selected = true;
                grid.CurrentCell = row.Cells[0];
                break;
            }
        }
        SetButtons();
    }

    private void AddColumn(string header, string name) => grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = header, Name = name, DataPropertyName = name });
    private ParentObservedApp? Selected() => grid.SelectedRows.Count == 1 && grid.SelectedRows[0].Index is var index && index >= 0 && index < visibleObserved.Length ? visibleObserved[index] : null;
    private void SetButtons()
    {
        var hasSelection = Selected() is not null;
        allow.Enabled = block.Enabled = remove.Enabled = hasSelection;
    }

    private async Task RunManualAsync(Func<Task> action)
    {
        ManualActionStarting?.Invoke(this, EventArgs.Empty);
        try { await action(); }
        finally { ManualActionCompleted?.Invoke(this, EventArgs.Empty); }
    }

    private async Task SetEnforcementAsync(bool enabled)
    {
        var result = await controller.SendAsync(enabled ? ParentControlAction.EnableM2AllowlistEnforcement : ParentControlAction.DisableM2AllowlistEnforcement);
        confirmation.Text = result.Success ? result.Message : result.Message;
        confirmation.ForeColor = result.Success ? Color.DarkGreen : Color.Firebrick;
        Update(result.Status?.Apps);
    }
    private async Task BulkAllowAsync()
    {
        var result = await controller.SendAsync(ParentControlAction.BulkAllowRunningApps);
        confirmation.Text = result.Message;
        confirmation.ForeColor = result.Success ? Color.DarkGreen : Color.Firebrick;
        Update(result.Status?.Apps);
    }

    private async Task ApplyAsync(ParentControlAction action, string verb)
    {
        var app = Selected();
        if (app is null) return;
        var result = await controller.SendAppAsync(action, app.Identity);
        confirmation.Text = result.Success ? $"{verb} {app.Identity.DisplayLabel}." : result.Message;
        confirmation.ForeColor = result.Success ? Color.DarkGreen : Color.Firebrick;
        Update(result.Status?.Apps);
    }
}