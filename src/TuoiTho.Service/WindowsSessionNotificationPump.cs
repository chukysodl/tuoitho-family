using System.Collections.Concurrent;
using System.Runtime.InteropServices;

using Microsoft.Win32;

namespace TuoiTho.Service;

public sealed record WindowsSessionNotification(SessionSwitchReason Reason, int SessionId);

public interface IWindowsSessionNotificationSource : IDisposable
{
    event EventHandler<WindowsSessionNotification>? SessionChanged;

    void Start();
}

public sealed class WindowsSessionNotificationPump : IWindowsSessionNotificationSource
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(10);
    private const uint WmWtsSessionChange = 0x02B1;
    private const uint WmClose = 0x0010;
    private const uint WmNcCreate = 0x0081;
    private const uint WmNcDestroy = 0x0082;
    private const uint NotifyForAllSessions = 1;
    private static readonly ConcurrentDictionary<IntPtr, WindowsSessionNotificationPump> WindowOwners = new();
    private static readonly WindowProcedureDelegate WindowProcedure = ProcessWindowMessage;
    private readonly ManualResetEventSlim started = new();
    private readonly Thread thread;
    private Exception? startupException;
    private IntPtr windowHandle;
    private int disposed;

    public WindowsSessionNotificationPump()
    {
        thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "TuoiTho Windows session notification pump"
        };
    }

    public event EventHandler<WindowsSessionNotification>? SessionChanged;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (thread.ThreadState == ThreadState.Unstarted)
        {
            thread.Start();
        }

        if (!started.Wait(StartupTimeout))
        {
            Dispose();
            throw new TimeoutException("Windows session notifications did not start within the configured timeout.");
        }

        if (startupException is not null)
        {
            throw new InvalidOperationException("Could not start Windows session notifications.", startupException);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        var handle = Volatile.Read(ref windowHandle);
        if (handle != IntPtr.Zero)
        {
            PostMessage(handle, WmClose, IntPtr.Zero, IntPtr.Zero);
        }

    }

    private void Run()
    {
        try
        {
            windowHandle = CreateMessageWindow(this);
            if (Volatile.Read(ref disposed) != 0)
            {
                return;
            }

            if (windowHandle == IntPtr.Zero)
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }

            if (!WTSRegisterSessionNotification(windowHandle, NotifyForAllSessions))
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }

            if (Volatile.Read(ref disposed) != 0)
            {
                return;
            }

            started.Set();
            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        catch (Exception exception)
        {
            startupException = exception;
            started.Set();
        }
        finally
        {
            if (windowHandle != IntPtr.Zero)
            {
                WTSUnRegisterSessionNotification(windowHandle);
                DestroyWindow(windowHandle);
                windowHandle = IntPtr.Zero;
            }
        }
    }

    private static IntPtr CreateMessageWindow(WindowsSessionNotificationPump owner)
    {
        var instance = GetModuleHandle(null);
        var className = $"TuoiTho.SessionNotifications.{Environment.ProcessId}";
        var windowClass = new WindowClassEx
        {
            Size = (uint)Marshal.SizeOf<WindowClassEx>(),
            WindowProcedure = WindowProcedure,
            Instance = instance,
            ClassName = className
        };
        RegisterClassEx(ref windowClass);

        var ownerHandle = GCHandle.Alloc(owner);
        try
        {
            return CreateWindowEx(
                0,
                className,
                null,
                0,
                0,
                0,
                0,
                0,
                new IntPtr(-3),
                IntPtr.Zero,
                instance,
                GCHandle.ToIntPtr(ownerHandle));
        }
        finally
        {
            ownerHandle.Free();
        }
    }

    private static IntPtr ProcessWindowMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmNcCreate)
        {
            var create = Marshal.PtrToStructure<CreateStruct>(lParam);
            var handle = GCHandle.FromIntPtr(create.CreateParams);
            if (handle.Target is WindowsSessionNotificationPump owner)
            {
                WindowOwners[window] = owner;
            }
        }
        else if (message == WmWtsSessionChange && WindowOwners.TryGetValue(window, out var owner))
        {
            owner.SessionChanged?.Invoke(
                owner,
                new WindowsSessionNotification((SessionSwitchReason)wParam.ToInt32(), unchecked((int)lParam.ToInt64())));
        }
        else if (message == WmClose)
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }
        else if (message == WmNcDestroy)
        {
            WindowOwners.TryRemove(window, out _);
        }

        return DefWindowProc(window, message, wParam, lParam);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WindowProcedureDelegate(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClassEx
    {
        public uint Size;
        public uint Style;
        public WindowProcedureDelegate WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr IconSmall;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CreateStruct
    {
        public IntPtr CreateParams;
        public IntPtr Instance;
        public IntPtr Menu;
        public IntPtr Parent;
        public int Height;
        public int Width;
        public int Y;
        public int X;
        public int Style;
        public string? Name;
        public string? ClassName;
        public uint ExtendedStyle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window;
        public uint MessageId;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Point;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string? windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Message message, IntPtr window, uint minimumFilter, uint maximumFilter);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSRegisterSessionNotification(IntPtr window, uint flags);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSUnRegisterSessionNotification(IntPtr window);
}