using TuoiTho.Core.Policy;

namespace TuoiTho.SessionAgent;

public sealed class WinFormsChildSoftLockView : IChildSoftLockView
{
    private readonly object gate = new();
    private readonly ManualResetEventSlim initialized = new(false);
    private Thread? uiThread;
    private ChildSoftLockForm? form;
    private bool disposed;

    public event Action? EmergencyExitRequested;
    public event Action? ParentControlRequested;

    public void Show(ChildSoftLockState state)
    {
        EnsureStarted();
        Dispatch(() =>
        {
            form!.Apply(state);
            form.ShowBlocker();
        });
    }

    public void Hide()
    {
        if (!initialized.IsSet)
        {
            return;
        }

        Dispatch(() => form?.HideBlocker());
    }

    public void Dispose()
    {
        ChildSoftLockForm? current;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            current = form;
        }

        if (current is not null && current.IsHandleCreated)
        {
            Dispatch(() => current.CloseForAgentShutdown());
        }

        if (uiThread is not null && uiThread != Thread.CurrentThread)
        {
            uiThread.Join(TimeSpan.FromSeconds(2));
        }

        initialized.Dispose();
    }

    private void EnsureStarted()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (uiThread is not null)
            {
                return;
            }

            uiThread = new Thread(() =>
            {
                using var created = new ChildSoftLockForm(
                    () => EmergencyExitRequested?.Invoke(),
                    () => ParentControlRequested?.Invoke());
                form = created;
                _ = created.Handle;
                initialized.Set();
                Application.Run();
                form = null;
            })
            {
                IsBackground = true,
                Name = "TuoiTho M1 Soft Lock"
            };
            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.Start();
        }

        if (!initialized.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new InvalidOperationException("The M1 soft-lock UI did not initialize.");
        }
    }

    private void Dispatch(Action action)
    {
        var current = form;
        if (current is null || current.IsDisposed)
        {
            return;
        }

        if (current.InvokeRequired)
        {
            current.BeginInvoke(action);
        }
        else
        {
            action();
        }
    }
}

public sealed class ChildSoftLockForm : Form
{
    private readonly Label headline = CenteredLabel("", 32, FontStyle.Bold);
    private readonly Label message = CenteredLabel("", 18);
    private readonly Label profile = CenteredLabel(14, Color.White);
    private readonly Label reason = CenteredLabel(16, Color.FromArgb(255, 205, 94), FontStyle.Bold);
    private readonly Label remaining = CenteredLabel(30, Color.FromArgb(194, 247, 239), FontStyle.Bold);
    private readonly TableLayoutPanel recoveryPanel = new() { AutoSize = true, Anchor = AnchorStyles.None, ColumnCount = 1, BackColor = Color.FromArgb(28, 79, 103), Padding = new Padding(22), Margin = new Padding(14) };
    private readonly Button openParent = new() { Text = "Mở điều khiển phụ huynh", AutoSize = true, MinimumSize = new Size(280, 48), Margin = new Padding(6), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(24,151,143), ForeColor = Color.White, Font = new Font("Segoe UI Semibold",10), Cursor = Cursors.Hand };
    private readonly Button emergencyExit = new() { Text = "Thoát màn hình thử nghiệm", AutoSize = true, MinimumSize = new Size(280, 48), Margin = new Padding(6), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(205,139,36), ForeColor = Color.White, Font = new Font("Segoe UI Semibold",10), Cursor = Cursors.Hand };
    private bool allowClose;
    private bool realLockActive;

    public ChildSoftLockForm(Action emergencyExitRequested, Action parentControlRequested)
    {
        Text = "Tuổi Thơ";
        BackColor = Color.FromArgb(20, 50, 82);
        ForeColor = Color.White;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        WindowState = FormWindowState.Maximized;
        TopMost = true;
        ShowInTaskbar = false;
        ControlBox = false;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        openParent.FlatAppearance.BorderSize = 0;
        emergencyExit.FlatAppearance.BorderSize = 0;

        var backdrop = new LockBackdropPanel { Dock = DockStyle.Fill };
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, BackColor = Color.Transparent, Padding = new Padding(28) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 12));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 12));
        panel.Controls.Add(new Label { Text = "✦  QUẢN LÝ THỜI GIAN", AutoSize = true, Anchor = AnchorStyles.None, ForeColor = Color.FromArgb(196,247,239), Font = new Font("Segoe UI Semibold",14), Margin = new Padding(8,8,8,10) }, 0, 0);
        panel.Controls.Add(headline, 0, 1);
        panel.Controls.Add(message, 0, 2);
        panel.Controls.Add(CenteredLabel("Vui lòng nhờ phụ huynh mở khóa hoặc cộng thêm thời gian.", 14), 0, 3);
        panel.Controls.Add(profile, 0, 4);
        panel.Controls.Add(reason, 0, 5);
        panel.Controls.Add(remaining, 0, 6);

        recoveryPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        recoveryPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        recoveryPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        recoveryPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        recoveryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        recoveryPanel.Controls.Add(CenteredLabel("CHẾ ĐỘ THỬ NGHIỆM M1", 14, FontStyle.Bold), 0, 0);
        recoveryPanel.Controls.Add(CenteredLabel("F12: thoát màn hình thử nghiệm", 11), 0, 1);
        recoveryPanel.Controls.Add(CenteredLabel("Không thay đổi quota khi thoát.", 11), 0, 2);
        var buttons = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.None, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
        buttons.Controls.Add(openParent);
        buttons.Controls.Add(emergencyExit);
        recoveryPanel.Controls.Add(buttons, 0, 3);
        panel.Controls.Add(recoveryPanel, 0, 7);
        backdrop.Controls.Add(panel);
        Controls.Add(backdrop);

        openParent.Click += (_, _) => parentControlRequested();
        emergencyExit.Click += (_, _) => emergencyExitRequested();
        KeyDown += (_, e) =>
        {
            if (!IsM1EmergencyExitKey(e.KeyCode))
            {
                return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
            emergencyExitRequested();
        };
    }

    public void Apply(ChildSoftLockState state)
    {
        realLockActive = state.IsRealLock;
        headline.Text = state.Reason == AccessDenyReason.ParentLock ? "MÁY ĐÃ ĐƯỢC PHỤ HUYNH KHÓA" : "HẾT THỜI GIAN SỬ DỤNG";
        message.Text = state.Reason == AccessDenyReason.ParentLock ? "Thiết bị đang tạm khóa theo yêu cầu của phụ huynh." : "Thời gian sử dụng máy hôm nay đã hết.";
        profile.Text = $"Hồ sơ: {state.ProfileId}";
        reason.Text = $"Lý do: {PolicyReasonText.ToDisplayText(state.Reason)}";
        remaining.Text = $"Thời gian còn lại: {state.Remaining}";
        recoveryPanel.Visible = state.ShowM1EmergencyExit;
        openParent.Visible = state.ShowM1EmergencyExit;
        emergencyExit.Visible = state.ShowM1EmergencyExit;
    }

    public static bool IsM1EmergencyExitKey(Keys key) => key == Keys.F12;

    public void ShowBlocker()
    {
        if (!Visible)
        {
            Show();
        }

        WindowState = FormWindowState.Maximized;
        TopMost = true;
        BringToFront();
        Activate();
    }

    public void HideBlocker() => Hide();

    public void CloseForAgentShutdown()
    {
        allowClose = true;
        Close();
        Application.ExitThread();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!allowClose)
        {
            e.Cancel = true;
            if (realLockActive)
            {
                BeginInvoke(ShowBlocker);
            }
            else
            {
                Hide();
            }
            return;
        }

        base.OnFormClosing(e);
    }

    private static Label CenteredLabel(string text, float size, FontStyle style = FontStyle.Regular) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.None,
        ForeColor = Color.White,
        Font = new Font("Segoe UI", size, style),
        TextAlign = ContentAlignment.MiddleCenter,
        Margin = new Padding(8, 6, 8, 6)
    };

    private static Label CenteredLabel(float size, Color color, FontStyle style = FontStyle.Regular) => new()
    {
        AutoSize = true,
        Anchor = AnchorStyles.None,
        ForeColor = color,
        Font = new Font("Segoe UI", size, style),
        TextAlign = ContentAlignment.MiddleCenter,
        Margin = new Padding(8, 4, 8, 4)
    };
}

public sealed class LockBackdropPanel : Panel
{
    public LockBackdropPanel()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(20, 50, 82);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var r = ClientRectangle;
        if (r.Width <= 0 || r.Height <= 0) return;

        using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
            r,
            Color.FromArgb(16, 43, 74),
            Color.FromArgb(18, 118, 119),
            18f);
        e.Graphics.FillRectangle(brush, r);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using var soft = new SolidBrush(Color.FromArgb(28, 255, 255, 255));
        e.Graphics.FillEllipse(soft, r.Width - 420, -180, 560, 560);
        e.Graphics.FillEllipse(soft, -190, r.Height - 310, 420, 420);

        using var accent = new SolidBrush(Color.FromArgb(44, 194, 247, 239));
        for (var i = 0; i < 8; i++)
        {
            var x = 42 + i * 36;
            var y = 46 + (i % 3) * 13;
            e.Graphics.FillEllipse(accent, x, y, 7, 7);
        }

        using var line = new Pen(Color.FromArgb(34, 255, 255, 255), 2f);
        e.Graphics.DrawArc(line, r.Width - 250, r.Height - 190, 180, 100, 190, 120);
        e.Graphics.DrawArc(line, r.Width - 295, r.Height - 220, 220, 130, 190, 120);
    }
}
