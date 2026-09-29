using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace TuoiTho.Service;

public sealed class WindowsInteractiveSessionAgentRuntime : IInteractiveSessionAgentRuntime
{
    private const uint NoActiveConsoleSession = uint.MaxValue;
    private const uint MaximumAllowed = 0x02000000;
    private const int SecurityImpersonation = 2;
    private const int TokenPrimary = 1;
    private const uint CreateUnicodeEnvironment = 0x00000400;

    public ActiveInteractiveSession? GetActiveSession()
    {
        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == NoActiveConsoleSession) return null;
        if (!WTSQueryUserToken(sessionId, out var token)) return null;

        try
        {
            using var identity = new WindowsIdentity(token);
            var sid = identity.User?.Value;
            return string.IsNullOrWhiteSpace(sid)
                ? null
                : new ActiveInteractiveSession(checked((int)sessionId), sid);
        }
        finally
        {
            CloseHandle(token);
        }
    }

    public bool IsAgentRunning(int sessionId, string executablePath)
    {
        var expected = Path.GetFullPath(executablePath);
        var processName = Path.GetFileNameWithoutExtension(expected);
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                try
                {
                    if (process.SessionId != sessionId) continue;
                    var actual = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(actual) &&
                        string.Equals(Path.GetFullPath(actual), expected, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // A disappearing process is treated as absent and will be recreated.
                }
            }
        }

        return false;
    }

    public void LaunchAgent(
        ActiveInteractiveSession session,
        string executablePath,
        string profileId,
        string? parentExecutablePath)
    {
        var executable = Path.GetFullPath(executablePath);
        if (!File.Exists(executable)) throw new FileNotFoundException("SessionAgent executable was not found.", executable);
        ValidateArgument(profileId);
        if (parentExecutablePath is not null) ValidateArgument(parentExecutablePath);

        if (!WTSQueryUserToken(checked((uint)session.SessionId), out var userToken))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "WTSQueryUserToken failed.");

        IntPtr primaryToken = IntPtr.Zero;
        IntPtr environment = IntPtr.Zero;
        try
        {
            if (!DuplicateTokenEx(userToken, MaximumAllowed, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out primaryToken))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "DuplicateTokenEx failed.");

            if (!CreateEnvironmentBlock(out environment, primaryToken, false))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "CreateEnvironmentBlock failed.");

            var command = new StringBuilder();
            command.Append(Quote(executable))
                .Append(" --profile-id ").Append(Quote(profileId))
                .Append(" --publisher-sid S-1-5-18 --real");

            if (!string.IsNullOrWhiteSpace(parentExecutablePath))
            {
                command.Append(" --parent-exe ").Append(Quote(Path.GetFullPath(parentExecutablePath)));
            }

            var startup = new StartupInfo
            {
                Size = Marshal.SizeOf<StartupInfo>(),
                Desktop = @"winsta0\default"
            };

            if (!CreateProcessAsUser(
                    primaryToken,
                    executable,
                    command,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    CreateUnicodeEnvironment,
                    environment,
                    Path.GetDirectoryName(executable),
                    ref startup,
                    out var processInfo))
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessAsUser failed.");
            }

            CloseHandle(processInfo.Thread);
            CloseHandle(processInfo.Process);
        }
        finally
        {
            if (environment != IntPtr.Zero) DestroyEnvironmentBlock(environment);
            if (primaryToken != IntPtr.Zero) CloseHandle(primaryToken);
            CloseHandle(userToken);
        }
    }

    private static void ValidateArgument(string value)
    {
        if (value.Contains('"')) throw new InvalidOperationException("Production launch arguments cannot contain quote characters.");
    }

    private static string Quote(string value) => '"' + value + '"';

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2;
        public IntPtr Reserved2Pointer;
        public IntPtr StdInput;
        public IntPtr StdOutput;
        public IntPtr StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public uint ProcessId;
        public uint ThreadId;
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(
        IntPtr existingToken,
        uint desiredAccess,
        IntPtr tokenAttributes,
        int impersonationLevel,
        int tokenType,
        out IntPtr newToken);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

    [DllImport("userenv.dll")]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessAsUser(
        IntPtr token,
        string applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
