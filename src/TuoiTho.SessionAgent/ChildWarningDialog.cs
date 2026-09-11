using System.Runtime.InteropServices;

namespace TuoiTho.SessionAgent;

public static class ChildWarningDialog
{
    public static void Show(string text)
    {
        var result = MessageBox(IntPtr.Zero, text, "Tuổi Thơ", 0x40 | 0x1000);
        GC.KeepAlive(result);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}