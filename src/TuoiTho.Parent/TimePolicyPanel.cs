using System.Globalization;
using TuoiTho.Core.Policy;

namespace TuoiTho.Parent;

/// <summary>Editable local-only quota, allowed schedule, post-boot limit, and blocked daily hours.</summary>
public sealed class TimePolicyPanel : UserControl
{
    private readonly ParentDesktopController controller;
    private readonly IParentAuthorizationGate? authorization;
    private readonly NumericUpDown hours = new() { Minimum = 0, Maximum = 24, Width = 72 };
    private readonly NumericUpDown minutes = new() { Minimum = 0, Maximum = 59, Width = 72, Increment = 5 };
    private readonly NumericUpDown startupLimitMinutes = new()
    {
        Minimum = 0,
        Maximum = 1440,
        Width = 86,
        Increment = 5
    };

    private readonly DataGridView grid = NewGrid();
    private readonly DataGridView blockedGrid = NewGrid();
    private readonly Label message = new() { AutoSize = true, Padding = new Padding(4) };
    private bool updating;

    public event EventHandler? ManualActionStarting;
    public event EventHandler? ManualActionCompleted;
    public event EventHandler<ParentUiResult>? PolicySaved;

    public TimePolicyPanel(ParentDesktopController controller, IParentAuthorizationGate? authorization = null)
    {
        this.controller = controller;
        this.authorization = authorization;
        Dock = DockStyle.Fill;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            RowCount = 8
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 56));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 44));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(new Label
        {
            Text = "THỜI GIAN SỬ DỤNG",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            AutoSize = true
        }, 0, 0);

        var quota = new FlowLayoutPanel { AutoSize = true, Padding = new Padding(0, 12, 0, 4) };
        quota.Controls.Add(new Label
        {
            Text = "HẠN MỨC MỖI NGÀY",
            AutoSize = true,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Margin = new Padding(0, 7, 14, 0)
        });
        quota.Controls.Add(hours);
        quota.Controls.Add(new Label { Text = "giờ", AutoSize = true, Margin = new Padding(4, 7, 10, 0) });
        quota.Controls.Add(minutes);
        quota.Controls.Add(new Label { Text = "phút", AutoSize = true, Margin = new Padding(4, 7, 0, 0) });
        root.Controls.Add(quota, 0, 1);

        var startup = new FlowLayoutPanel { AutoSize = true, Padding = new Padding(0, 4, 0, 10) };
        startup.Controls.Add(new Label
        {
            Text = "SAU KHI BẬT MÁY",
            AutoSize = true,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Margin = new Padding(0, 7, 14, 0)
        });
        startup.Controls.Add(startupLimitMinutes);
        startup.Controls.Add(new Label
        {
            Text = "phút được sử dụng  (0 = không giới hạn theo lần bật máy)",
            AutoSize = true,
            Margin = new Padding(4, 7, 0, 0)
        });
        root.Controls.Add(startup, 0, 2);

        root.Controls.Add(new Label
        {
            Text = "KHUNG GIỜ ĐƯỢC PHÉP THEO TỪNG NGÀY",
            AutoSize = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Padding = new Padding(0, 4, 0, 4)
        }, 0, 3);

        ConfigureAllowedGrid();
        root.Controls.Add(grid, 0, 4);

        root.Controls.Add(new Label
        {
            Text = "KHUNG GIỜ CẤM MỖI NGÀY  —  ví dụ 00:00–06:00, 18:00–21:00, 23:00–00:00",
            AutoSize = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Padding = new Padding(0, 8, 0, 4)
        }, 0, 5);

        ConfigureBlockedGrid();
        root.Controls.Add(blockedGrid, 0, 6);

        var footer = new FlowLayoutPanel { AutoSize = true, Padding = new Padding(0, 12, 0, 0) };
        var save = new Button
        {
            Text = "Lưu thời gian sử dụng",
            AutoSize = true,
            MinimumSize = new Size(190, 42)
        };
        save.Click += async (_, _) => await SaveAsync();
        footer.Controls.Add(save);
        footer.Controls.Add(message);
        root.Controls.Add(footer, 0, 7);

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
            startupLimitMinutes.Value = Math.Clamp(status.StartupLimitMinutes, 0, 1440);

            var windows = status.Windows ?? [];
            foreach (DataGridViewRow row in grid.Rows)
            {
                var day = (DayOfWeek)row.Tag!;
                var slot = (int)row.Cells["Slot"].Value!;
                var window = windows
                    .Where(item => item.Day == day)
                    .OrderBy(item => item.Start)
                    .ElementAtOrDefault(slot);

                row.Cells["Enabled"].Value = window is not null;
                row.Cells["Start"].Value = window?.Start.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "";
                row.Cells["End"].Value = window?.End.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "";
            }

            var blocked = (status.BlockedWindows ?? [])
                .OrderBy(item => item.Start)
                .ToArray();

            for (var index = 0; index < blockedGrid.Rows.Count; index++)
            {
                var row = blockedGrid.Rows[index];
                var window = blocked.ElementAtOrDefault(index);
                row.Cells["BlockedEnabled"].Value = window is not null;
                row.Cells["BlockedStart"].Value = window?.Start.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "";
                row.Cells["BlockedEnd"].Value = window?.End.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "";
            }
        }
        finally
        {
            updating = false;
        }
    }

    public static bool TryBuild(
        int quotaMinutes,
        IEnumerable<AllowedUsageWindow> windows,
        out string? error) =>
        TryBuild(quotaMinutes, 0, windows, [], out error);

    public static bool TryBuild(
        int quotaMinutes,
        int startupMinutes,
        IEnumerable<AllowedUsageWindow> windows,
        IEnumerable<BlockedUsageWindow> blockedWindows,
        out string? error)
    {
        if (quotaMinutes is < 1 or > 1440)
        {
            error = "Hạn mức mỗi ngày phải từ 1 đến 1440 phút.";
            return false;
        }

        if (startupMinutes is < 0 or > 1440)
        {
            error = "Thời gian sau khi bật máy phải từ 0 đến 1440 phút.";
            return false;
        }

        error = WeeklyScheduleValidator.Validate(windows.ToArray());
        if (error is not null) return false;

        error = BlockedScheduleValidator.Validate(blockedWindows.ToArray());
        return error is null;
    }

    private void ConfigureAllowedGrid()
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Day",
            HeaderText = "Ngày",
            ReadOnly = true,
            Width = 130
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Slot",
            HeaderText = "Khung",
            ReadOnly = true,
            Width = 56
        });
        grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Enabled",
            HeaderText = "Dùng",
            Width = 52
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Start",
            HeaderText = "Bắt đầu (HH:mm)",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "End",
            HeaderText = "Kết thúc (HH:mm)",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });

        foreach (var day in new[]
                 {
                     DayOfWeek.Monday,
                     DayOfWeek.Tuesday,
                     DayOfWeek.Wednesday,
                     DayOfWeek.Thursday,
                     DayOfWeek.Friday,
                     DayOfWeek.Saturday,
                     DayOfWeek.Sunday
                 })
        {
            for (var slot = 0; slot < WeeklyScheduleValidator.MaximumWindowsPerDay; slot++)
            {
                var index = grid.Rows.Add(
                    WeeklyScheduleValidator.VietnameseDay(day),
                    slot + 1,
                    false,
                    "",
                    "");
                grid.Rows[index].Tag = day;
            }
        }
    }

    private void ConfigureBlockedGrid()
    {
        blockedGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "BlockedSlot",
            HeaderText = "Khung",
            ReadOnly = true,
            Width = 64
        });
        blockedGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "BlockedEnabled",
            HeaderText = "Cấm",
            Width = 58
        });
        blockedGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "BlockedStart",
            HeaderText = "Bắt đầu (HH:mm)",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        blockedGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "BlockedEnd",
            HeaderText = "Kết thúc (HH:mm)",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });

        for (var slot = 0; slot < BlockedScheduleValidator.MaximumWindows; slot++)
            blockedGrid.Rows.Add(slot + 1, false, "", "");
    }

    private async Task SaveAsync()
    {
        if (updating) return;
        if (authorization is not null && !authorization.EnsureAuthorized(FindForm())) return;

        var windows = new List<AllowedUsageWindow>();
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.Cells["Enabled"].Value is not true) continue;

            if (!TryParseTime(row.Cells["Start"].Value, out var start) ||
                !TryParseTime(row.Cells["End"].Value, out var end))
            {
                ShowError("Giờ được phép phải theo định dạng HH:mm.");
                return;
            }

            windows.Add(new AllowedUsageWindow((DayOfWeek)row.Tag!, start, end));
        }

        var blockedWindows = new List<BlockedUsageWindow>();
        foreach (DataGridViewRow row in blockedGrid.Rows)
        {
            if (row.Cells["BlockedEnabled"].Value is not true) continue;

            if (!TryParseTime(row.Cells["BlockedStart"].Value, out var start) ||
                !TryParseTime(row.Cells["BlockedEnd"].Value, out var end))
            {
                ShowError("Khung giờ cấm phải theo định dạng HH:mm.");
                return;
            }

            blockedWindows.Add(new BlockedUsageWindow(start, end));
        }

        var quota = checked((int)hours.Value * 60 + (int)minutes.Value);
        var startup = (int)startupLimitMinutes.Value;

        if (!TryBuild(quota, startup, windows, blockedWindows, out var error))
        {
            ShowError(error ?? "Thiết lập thời gian không hợp lệ.");
            return;
        }

        ManualActionStarting?.Invoke(this, EventArgs.Empty);
        try
        {
            var result = await controller.SaveTimePolicyAsync(
                quota,
                windows,
                startup,
                blockedWindows);

            message.ForeColor = result.Success ? Color.DarkGreen : Color.Firebrick;
            message.Text = result.Message;
            PolicySaved?.Invoke(this, result);
        }
        finally
        {
            ManualActionCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private static bool TryParseTime(object? value, out TimeOnly time) =>
        TimeOnly.TryParse(
            value?.ToString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out time);

    private void ShowError(string text)
    {
        message.ForeColor = Color.Firebrick;
        message.Text = text;
    }

    private static DataGridView NewGrid() => new()
    {
        Dock = DockStyle.Fill,
        AutoGenerateColumns = false,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.CellSelect
    };
}
