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
                using var created = new ChildSoftLockForm(() => EmergencyExitRequested?.Invoke());
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
    private readonly Label profile = new() { AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI", 14, FontStyle.Regular), TextAlign = ContentAlignment.MiddleCenter };
    private readonly Label reason = new() { AutoSize = true, ForeColor = Color.Gold, Font = new Font("Segoe UI", 16, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
    private readonly Label remaining = new() { AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI", 28, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
    private readonly Button emergencyExit = new() { Text = "Thoát màn hình thử nghiệm", AutoSize = true, MinimumSize = new Size(220, 42) };
    private bool allowClose;

    public ChildSoftLockForm(Action emergencyExitRequested)
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

        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, BackColor = BackColor, Padding = new Padding(32) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        panel.Controls.Add(new Label { Text = "HẾT THỜI GIAN SỬ DỤNG", AutoSize = true, Anchor = AnchorStyles.None, ForeColor = Color.White, Font = new Font("Segoe UI", 30, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter }, 0, 1);
        panel.Controls.Add(new Label { Text = "Thời gian sử dụng máy hôm nay đã hết.", AutoSize = true, Anchor = AnchorStyles.None, ForeColor = Color.White, Font = new Font("Segoe UI", 18), TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(8, 18, 8, 4) }, 0, 2);
        panel.Controls.Add(new Label { Text = "Vui lòng nhờ phụ huynh cộng thêm thời gian.", AutoSize = true, Anchor = AnchorStyles.None, ForeColor = Color.White, Font = new Font("Segoe UI", 15), TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(8, 4, 8, 18) }, 0, 3);
        panel.Controls.Add(profile, 0, 4);
        panel.Controls.Add(reason, 0, 5);
        panel.Controls.Add(remaining, 0, 6);
        emergencyExit.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        emergencyExit.Visible = false;
        emergencyExit.Click += (_, _) => emergencyExitRequested();
        Controls.Add(panel);
        Controls.Add(emergencyExit);
        Resize += (_, _) => emergencyExit.Location = new Point(ClientSize.Width - emergencyExit.Width - 20, ClientSize.Height - emergencyExit.Height - 20);
    }

    public void Apply(ChildSoftLockState state)
    {
        profile.Text = $"Hồ sơ: {state.ProfileId}";
        reason.Text = $"Lý do: {PolicyReasonText.ToDisplayText(state.Reason)}";
        remaining.Text = $"Thời gian còn lại: {state.Remaining}";
        emergencyExit.Visible = state.ShowM1EmergencyExit;
    }

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
}