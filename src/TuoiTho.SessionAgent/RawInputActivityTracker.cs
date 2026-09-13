using System.Runtime.InteropServices;

namespace TuoiTho.SessionAgent;

public sealed record RawInputActivityStatus(bool IsAvailable, DateTimeOffset? LastDeviceInputUtc, string? Diagnostic);

public interface IRawInputActivityTracker : IDisposable
{
    void Start();
    RawInputActivityStatus GetStatus();
}

/// <summary>
/// Receives only WM_INPUT occurrence notifications in the interactive SessionAgent session.
/// Raw payloads are deliberately never read, retained, logged, or transmitted.
/// </summary>
public sealed class RawInputActivityTracker(TimeProvider timeProvider) : IRawInputActivityTracker
{
    private const uint WmInput = 0x00FF;
    private const uint WmClose = 0x0010;
    private const uint WmDestroy = 0x0002;
    private const uint WmQuit = 0x0012;
    private const uint RidevInputSink = 0x00000100;
    private static readonly IntPtr HwndMessage = new(-3);
    private const string WindowClassName = "TuoiTho.RawInputActivityWindow";
    private static readonly object WindowClassGate = new();
    private static readonly Dictionary<IntPtr, RawInputActivityTracker> Trackers = [];
    private static readonly WindowProcedureDelegate WindowProcedure = DispatchWindowProcedure;
    private static ushort windowClass;

    private readonly object gate = new();
    private readonly ManualResetEventSlim initialized = new(false);
    private Thread? thread;
    private IntPtr window;
    private uint threadId;
    private bool started;
    private bool disposed;
    private bool available;
    private DateTimeOffset? lastDeviceInputUtc;
    private string? diagnostic;

    public RawInputActivityTracker() : this(TimeProvider.System) { }

    public void Start()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (started)
            {
                return;
            }

            started = true;
            thread = new Thread(RunMessageLoop)
            {
                IsBackground = true,
                Name = "TuoiTho Raw Input Activity"
            };
            thread.Start();
        }

        if (!initialized.Wait(TimeSpan.FromSeconds(5)))
        {
            lock (gate)
            {
                available = false;
                diagnostic = "Raw Input initialization timed out.";
            }
        }
    }

    public RawInputActivityStatus GetStatus()
    {
        lock (gate)
        {
            return new RawInputActivityStatus(available, lastDeviceInputUtc, diagnostic);
        }
    }

    public void Dispose()
    {
        Thread? worker;
        IntPtr handle;
        uint id;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            available = false;
            worker = thread;
            handle = window;
            id = threadId;
        }

        if (handle != IntPtr.Zero)
        {
            PostMessage(handle, WmClose, IntPtr.Zero, IntPtr.Zero);
        }
        else if (id != 0)
        {
            PostThreadMessage(id, WmQuit, IntPtr.Zero, IntPtr.Zero);
        }

        if (worker is not null && worker != Thread.CurrentThread)
        {
            worker.Join(TimeSpan.FromSeconds(2));
        }

        initialized.Dispose();
    }

    private void RunMessageLoop()
    {
        IntPtr createdWindow = IntPtr.Zero;
        try
        {
            EnsureWindowClass();
            createdWindow = CreateWindowEx(0, WindowClassName, string.Empty, 0, 0, 0, 0, 0, HwndMessage, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
            if (createdWindow == IntPtr.Zero)
            {
                throw new InvalidOperationException($"CreateWindowEx for Raw Input failed: {Marshal.GetLastWin32Error()}.");
            }

            lock (WindowClassGate)
            {
                Trackers[createdWindow] = this;
            }

            var devices = new[]
            {
                new RawInputDevice { UsagePage = 0x01, Usage = 0x02, Flags = RidevInputSink, Target = createdWindow }, // mouse
                new RawInputDevice { UsagePage = 0x01, Usage = 0x06, Flags = RidevInputSink, Target = createdWindow }  // keyboard
            };
            if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>()))
            {
                throw new InvalidOperationException($"RegisterRawInputDevices failed: {Marshal.GetLastWin32Error()}.");
            }

            lock (gate)
            {
                window = createdWindow;
                threadId = GetCurrentThreadId();
                lastDeviceInputUtc = timeProvider.GetUtcNow();
                available = true;
                diagnostic = null;
            }
            initialized.Set();

            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        catch (Exception exception)
        {
            lock (gate)
            {
                available = false;
                diagnostic = exception.Message;
            }
        }
        finally
        {
            initialized.Set();
            if (createdWindow != IntPtr.Zero)
            {
                lock (WindowClassGate)
                {
                    Trackers.Remove(createdWindow);
                }

                DestroyWindow(createdWindow);
            }
        }
    }

    private void RecordDeviceInput()
    {
        lock (gate)
        {
            if (available && !disposed)
            {
                lastDeviceInputUtc = timeProvider.GetUtcNow();
            }
        }
    }

    private static void EnsureWindowClass()
    {
        lock (WindowClassGate)
        {
            if (windowClass != 0)
            {
                return;
            }

            var definition = new WindowClassEx
            {
                Size = (uint)Marshal.SizeOf<WindowClassEx>(),
                WindowProcedure = WindowProcedure,
                Instance = GetModuleHandle(null),
                ClassName = WindowClassName
            };
            windowClass = RegisterClassEx(ref definition);
            if (windowClass == 0 && Marshal.GetLastWin32Error() != 1410) // ERROR_CLASS_ALREADY_EXISTS
            {
                throw new InvalidOperationException($"RegisterClassEx for Raw Input failed: {Marshal.GetLastWin32Error()}.");
            }
        }
    }

    private static IntPtr DispatchWindowProcedure(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmInput)
        {
            lock (WindowClassGate)
            {
                if (Trackers.TryGetValue(handle, out var tracker))
                {
                    // WM_INPUT proves a device event occurred. Do not inspect payload bytes.
                    tracker.RecordDeviceInput();
                }
            }
        }
        else if (message == WmClose)
        {
            DestroyWindow(handle);
            return IntPtr.Zero;
        }
        else if (message == WmDestroy)
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }

        return DefWindowProc(handle, message, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

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
    private struct Message
    {
        public IntPtr Handle;
        public uint MessageId;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Point;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WindowProcedureDelegate(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClassEx windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint style, string className, string windowName, uint windowStyle, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices([In] RawInputDevice[] devices, uint deviceCount, uint size);
    [DllImport("user32.dll")]
    private static extern int GetMessage(out Message message, IntPtr handle, uint minimumFilter, uint maximumFilter);
    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr handle);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}