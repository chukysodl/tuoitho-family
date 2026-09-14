using System.Runtime.InteropServices;

namespace TuoiTho.SessionAgent;

public static class ChildWarningDialog
{
    // Warning dialogs are intentionally asynchronous so a visible 15/5/1-minute warning cannot delay policy-state IPC.
    public static void Show(string text)
    {
        var thread = new Thread(() =>
        {
            var result = MessageBox(IntPtr.Zero, text, "Tuổi Thơ", 0x40 | 0x1000);
            GC.KeepAlive(result);
        }) { IsBackground = true, Name = "TuoiTho Child Warning" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}