using System.Globalization;
using TuoiTho.Core.Policy;

namespace TuoiTho.Parent;

/// <summary>Editable local-only daily quota and up-to-two-windows-per-day schedule editor.</summary>
public sealed class TimePolicyPanel : UserControl
{
    private readonly ParentDesktopController controller;
    private readonly NumericUpDown hours = new() { Minimum = 0, Maximum = 24, Width = 72 };
    private readonly NumericUpDown minutes = new() { Minimum = 0, Maximum = 59, Width = 72, Increment = 5 };
    private readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect
    };
    private readonly Label message = new() { AutoSize = true, Padding = new Padding(4) };
    private bool updating;

    public event EventHandler? ManualActionStarting;
    public event EventHandler? ManualActionCompleted;
    public event EventHandler<ParentUiResult>? PolicySaved;

    public TimePolicyPanel(ParentDesktopController controller)
    {
        this.controller = controller;
        Dock = DockStyle.Fill;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label { Text = "THỜI GIAN SỬ DỤNG", Font = new Font("Segoe UI", 16, FontStyle.Bold), AutoSize = true }, 0, 0);
        var quota = new FlowLayoutPanel { AutoSize = true, Padding = new Padding(0, 12, 0, 12) };
        quota.Controls.Add(new Label { Text = "HẠN MỨC HÔM NAY", AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold), Margin = new Padding(0, 7, 14, 0) });
        quota.Controls.Add(hours); quota.Controls.Add(new Label { Text = "giờ", AutoSize = true, Margin = new Padding(4, 7, 10, 0) });
        quota.Controls.Add(minutes); quota.Controls.Add(new Label { Text = "phút", AutoSize = true, Margin = new Padding(4, 7, 0, 0) });
        root.Controls.Add(quota, 0, 1);
        ConfigureGrid(); root.Controls.Add(grid, 0, 2);
        var footer = new FlowLayoutPanel { AutoSize = true, Padding = new Padding(0, 12, 0, 0) };
        var save = new Button { Text = "Lưu thời gian sử dụng", AutoSize = true, MinimumSize = new Size(190, 42) };
        save.Click += async (_, _) => await SaveAsync();
        footer.Controls.Add(save); footer.Controls.Add(message); root.Controls.Add(footer, 0, 3);
        Controls.Add(root);
    }

    public void Update(ParentControlStatus status)
    {
        if (updating) return;
        updating = true;
        try
        {
            var quota = Math.Max(0, status.QuotaMinutes);
            hours.Value = Math.Min(hours.Maximum, quota / 60);
            minutes.Value = Math.Min(minutes.Maximum, quota % 60);
            var windows = status.Windows ?? [];
            foreach (DataGridViewRow row in grid.Rows)
            {
                var day = (DayOfWeek)row.Tag!;
                var slot = (int)row.Cells["Slot"].Value!;
                var window = windows.Where(item => item.Day == day).OrderBy(item => item.Start).ElementAtOrDefault(slot);
                row.Cells["Enabled"].Value = window is not null;
                row.Cells["Start"].Value = window?.Start.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "";
                row.Cells["End"].Value = window?.End.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "";
            }
        }
        finally { updating = false; }
    }

    public static bool TryBuild(int quotaMinutes, IEnumerable<AllowedUsageWindow> windows, out string? error)
    {
        if (quotaMinutes is < 1 or > 1440) { error = "Hạn mức mỗi ngày phải từ 1 đến 1440 phút."; return false; }
        error = WeeklyScheduleValidator.Validate(windows.ToArray());
        return error is null;
    }

    private void ConfigureGrid()
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Day", HeaderText = "Ngày", ReadOnly = true, Width = 130 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Slot", HeaderText = "Khung", ReadOnly = true, Width = 56 });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "Dùng", Width = 52 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Start", HeaderText = "Bắt đầu (HH:mm)", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "End", HeaderText = "Kết thúc (HH:mm)", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday })
            for (var slot = 0; slot < WeeklyScheduleValidator.MaximumWindowsPerDay; slot++)
            {
                var index = grid.Rows.Add(WeeklyScheduleValidator.VietnameseDay(day), slot + 1, false, "", "");
                grid.Rows[index].Tag = day;
            }
    }

    private async Task SaveAsync()
    {
        if (updating) return;
        var windows = new List<AllowedUsageWindow>();
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.Cells["Enabled"].Value is not true) continue;
            if (!TimeOnly.TryParse(row.Cells["Start"].Value?.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) || !TimeOnly.TryParse(row.Cells["End"].Value?.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
            {
                message.ForeColor = Color.Firebrick; message.Text = "Giờ phải theo định dạng HH:mm."; return;
            }
            windows.Add(new AllowedUsageWindow((DayOfWeek)row.Tag!, start, end));
        }
        var quota = checked((int)hours.Value * 60 + (int)minutes.Value);
        if (!TryBuild(quota, windows, out var error)) { message.ForeColor = Color.Firebrick; message.Text = error; return; }
        ManualActionStarting?.Invoke(this, EventArgs.Empty);
        try
        {
            var result = await controller.SaveTimePolicyAsync(quota, windows);
            message.ForeColor = result.Success ? Color.DarkGreen : Color.Firebrick;
            message.Text = result.Message;
            PolicySaved?.Invoke(this, result);
        }
        finally { ManualActionCompleted?.Invoke(this, EventArgs.Empty); }
    }
}