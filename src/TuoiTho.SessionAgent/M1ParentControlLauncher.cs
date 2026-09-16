using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TuoiTho.SessionAgent;

/// <summary>Launches or foregrounds only the local Parent UI for the M1 recovery panel.</summary>
public sealed class M1ParentControlLauncher(string? configuredParentExecutable = null) : IM1ParentControlLauncher
{
    private const int SwRestore = 9;

    public bool TryOpenParentControl()
    {
        try
        {
            var sessionId = Process.GetCurrentProcess().SessionId;
            foreach (var process in Process.GetProcessesByName("TuoiTho.Parent"))
            {
                try
                {
                    if (process.HasExited || process.SessionId != sessionId || process.MainWindowHandle == IntPtr.Zero)
                    {
                        continue;
                    }

                    ShowWindow(process.MainWindowHandle, SwRestore);
                    SetForegroundWindow(process.MainWindowHandle);
                    return true;
                }
                catch (Exception)
                {
                    // Try another matching local Parent process, then the executable fallback.
                }
                finally
                {
                    process.Dispose();
                }
            }

            var executable = FindParentExecutable();
            if (executable is null)
            {
                return false;
            }

            Process.Start(new ProcessStartInfo(executable)
            {
                WorkingDirectory = Path.GetDirectoryName(executable)!,
                UseShellExecute = false
            });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private string? FindParentExecutable()
    {
        var candidates = new[]
        {
            configuredParentExecutable,
            Path.Combine(AppContext.BaseDirectory, "TuoiTho.Parent.exe"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "TuoiTho.Parent", "bin", "Release", "net10.0-windows", "TuoiTho.Parent.exe"))
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}