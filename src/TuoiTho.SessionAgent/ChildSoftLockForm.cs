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
    private readonly Label profile = CenteredLabel(14, Color.White);
    private readonly Label reason = CenteredLabel(16, Color.Gold, FontStyle.Bold);
    private readonly Label remaining = CenteredLabel(28, Color.White, FontStyle.Bold);
    private readonly TableLayoutPanel recoveryPanel = new() { AutoSize = true, Anchor = AnchorStyles.None, ColumnCount = 1, BackColor = Color.FromArgb(35, 67, 108), Padding = new Padding(20), Margin = new Padding(12) };
    private readonly Button openParent = new() { Text = "Mở điều khiển phụ huynh", AutoSize = true, MinimumSize = new Size(270, 46), Margin = new Padding(6) };
    private readonly Button emergencyExit = new() { Text = "Thoát màn hình thử nghiệm", AutoSize = true, MinimumSize = new Size(270, 46), Margin = new Padding(6) };
    private bool allowClose;

    public ChildSoftLockForm(Action emergencyExitRequested, Action parentControlRequested)
    {
        Text = "Tuổi Thơ";
        BackColor = Color.FromArgb(20, 43, 75);
        ForeColor = Color.White;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        WindowState = FormWindowState.Maximized;
        TopMost = true;
        ShowInTaskbar = false;
        ControlBox = false;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Dpi;

        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, BackColor = BackColor, Padding = new Padding(24) };
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
        panel.Controls.Add(CenteredLabel("HẾT THỜI GIAN SỬ DỤNG", 26, FontStyle.Bold), 0, 1);
        panel.Controls.Add(CenteredLabel("Thời gian sử dụng máy hôm nay đã hết.", 16), 0, 2);
        panel.Controls.Add(CenteredLabel("Vui lòng nhờ phụ huynh cộng thêm thời gian.", 14), 0, 3);
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
        Controls.Add(panel);

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
            Hide();
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