using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using TuoiTho.Core.Policy;
using TuoiTho.Core.Time;

namespace TuoiTho.Service;

public sealed record DiscoveredApplication(AppIdentity Identity, AppClassification Classification);
public sealed record AppDiscoveryResult(DateTimeOffset LastScanAtUtc, int ProcessesExamined, int AppsDiscovered, int AppsSkippedInaccessible, string? DiscoveryError, IReadOnlyList<DiscoveredApplication> Applications, int UserApplications = 0, int BackgroundHelpers = 0, int SystemProtected = 0)
{
    public AppDiscoveryResult(DateTimeOffset lastScanAtUtc, int processesExamined, int appsDiscovered, int appsSkippedInaccessible, string? discoveryError, IReadOnlyList<AppIdentity> applications)
        : this(lastScanAtUtc, processesExamined, appsDiscovered, appsSkippedInaccessible, discoveryError, applications.Select(identity => new DiscoveredApplication(identity, AppClassification.UserApplication)).ToArray(), applications.Count, 0, 0)
    {
    }
}

public interface IManagedSessionAppDiscovery { AppDiscoveryResult Discover(int sessionId); }

public sealed class WindowsManagedSessionAppDiscovery(IClock clock) : IManagedSessionAppDiscovery
{
    private static readonly HashSet<string> SystemProtectedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "sihost.exe", "taskhostw.exe", "svchost.exe", "winlogon.exe", "csrss.exe", "dwm.exe",
        "services.exe", "lsass.exe", "smss.exe", "fontdrvhost.exe", "ctfmon.exe"
    };

    private static readonly HashSet<string> KnownInteractiveUserApplicationFileNames = new(StringComparer.OrdinalIgnoreCase) { "calculatorapp.exe" };

    public static bool IsManagedSession(int processSessionId, int managedSessionId) => processSessionId == managedSessionId;

    public static AppClassification Classify(string? executablePath, int processSessionId, int managedSessionId, bool hasVisibleTopLevelWindow)
    {
        if (!IsManagedSession(processSessionId, managedSessionId)) return AppClassification.SystemProtected;
        var fileName = Path.GetFileName(executablePath ?? string.Empty);
        if (SystemProtectedFileNames.Contains(fileName)) return AppClassification.SystemProtected;
        if (hasVisibleTopLevelWindow || KnownInteractiveUserApplicationFileNames.Contains(fileName)) return AppClassification.UserApplication;
        return AppClassification.BackgroundHelper;
    }

    public AppDiscoveryResult Discover(int sessionId)
    {
        var applications = new Dictionary<string, DiscoveredApplication>(StringComparer.OrdinalIgnoreCase);
        var examined = 0;
        var skipped = 0;
        string? error = null;
        var visibleProcessIds = GetVisibleTopLevelProcessIds();

        try
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (!IsManagedSession(process.SessionId, sessionId)) continue;
                    examined++;
                    var path = process.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        skipped++;
                        continue;
                    }

                    var info = FileVersionInfo.GetVersionInfo(path);
                    var identity = AppIdentity.FromExecutablePath(path, info.FileDescription, info.CompanyName, info.ProductName);
                    var classification = Classify(path, process.SessionId, sessionId, visibleProcessIds.Contains(process.Id));
                    var discovered = new DiscoveredApplication(identity, classification);
                    if (applications.TryGetValue(identity.NormalizedExecutablePath, out var current)) applications[identity.NormalizedExecutablePath] = MoreVisible(current, discovered); else applications.Add(identity.NormalizedExecutablePath, discovered);
                }
                catch (Exception)
                {
                    skipped++;
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            error = exception.Message;
        }

        var values = applications.Values.ToArray();
        return new(
            clock.UtcNow,
            examined,
            values.Length,
            skipped,
            error,
            values,
            values.Count(application => application.Classification == AppClassification.UserApplication),
            values.Count(application => application.Classification == AppClassification.BackgroundHelper),
            values.Count(application => application.Classification == AppClassification.SystemProtected));
    }

    private static DiscoveredApplication MoreVisible(DiscoveredApplication current, DiscoveredApplication candidate) =>
        candidate.Classification == AppClassification.UserApplication ? candidate : current;

    private static HashSet<int> GetVisibleTopLevelProcessIds()
    {
        var processIds = new HashSet<int>();
        if (!OperatingSystem.IsWindows()) return processIds;

        try
        {
            EnumWindows((window, _) =>
            {
                if (!IsWindowVisible(window)) return true;
                var threadId = GetWindowThreadProcessId(window, out var processId);
                if (threadId != 0 && processId != 0) processIds.Add(unchecked((int)processId));
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception)
        {
            // Discovery remains best-effort. Without window metadata, processes are conservatively background helpers.
        }

        return processIds;
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}

public sealed class AppDiscoveryService(IDeviceTimePolicyStore timePolicies, IAppPolicyStore appPolicies, IManagedSessionAppDiscovery discovery, ILogger<AppDiscoveryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var profile = Environment.GetEnvironmentVariable("TuoiTho__ProfileId") ?? "m1-child";
                var policy = await timePolicies.LoadAsync(profile, stoppingToken);
                if (policy is not null)
                {
                    var result = discovery.Discover(policy.ManagedSessionId);
                    foreach (var app in result.Applications)
                    {
                        await appPolicies.RecordObservationAsync(new ObservedApp(policy.ProfileId, policy.ManagedSessionId, app.Identity, result.LastScanAtUtc, result.LastScanAtUtc, app.Classification), stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                AppDiscoveryLog.Failed(logger, exception);
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}

internal static partial class AppDiscoveryLog
{
    [LoggerMessage(EventId = 2400, Level = LogLevel.Warning, Message = "M2 app discovery failed; no process is controlled.")]
    public static partial void Failed(ILogger logger, Exception exception);
}