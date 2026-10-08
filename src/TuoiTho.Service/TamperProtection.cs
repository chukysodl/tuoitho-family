using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TuoiTho.Core.Security;

namespace TuoiTho.Service;

public interface ITamperProtectionPlatform
{
    Task ApplyHardenedStateAsync(CancellationToken cancellationToken);
    Task ApplyMaintenanceStateAsync(CancellationToken cancellationToken);
}

public sealed class WindowsTamperProtectionPlatform(
    ILogger<WindowsTamperProtectionPlatform> logger) : ITamperProtectionPlatform
{
    internal const string ServiceName = "TuoiTho.Service";
    internal const string HardenedServiceSddl =
        "D:(A;;GA;;;SY)(A;;GRRP;;;BA)(A;;GR;;;AU)";
    internal const string MaintenanceServiceSddl =
        "D:(A;;GA;;;SY)(A;;GA;;;BA)(A;;GR;;;AU)";

    public async Task ApplyHardenedStateAsync(CancellationToken cancellationToken)
    {
        var installDirectory = GetInstallDirectory();
        if (Directory.Exists(installDirectory))
        {
            await RunAsync(
                "icacls.exe",
                [
                    installDirectory,
                    "/inheritance:r",
                    "/grant:r",
                    "*S-1-5-18:(OI)(CI)(F)",
                    "*S-1-5-32-544:(OI)(CI)(RX)",
                    "*S-1-5-32-545:(OI)(CI)(RX)",
                    "/T",
                    "/C",
                    "/Q"
                ],
                cancellationToken);
        }

        var authPath = GetParentAuthPath();
        if (File.Exists(authPath))
        {
            await RunAsync(
                "icacls.exe",
                [
                    authPath,
                    "/inheritance:r",
                    "/grant:r",
                    "*S-1-5-18:(F)",
                    "*S-1-5-32-544:(R)",
                    "/C",
                    "/Q"
                ],
                cancellationToken);
        }

        await RunAsync(
            "sc.exe",
            ["sdset", ServiceName, HardenedServiceSddl],
            cancellationToken);

        TamperLog.Hardened(logger);
    }

    public async Task ApplyMaintenanceStateAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            "sc.exe",
            ["sdset", ServiceName, MaintenanceServiceSddl],
            cancellationToken);

        var installDirectory = GetInstallDirectory();
        if (Directory.Exists(installDirectory))
        {
            await RunAsync(
                "icacls.exe",
                [
                    installDirectory,
                    "/inheritance:r",
                    "/grant:r",
                    "*S-1-5-18:(OI)(CI)(F)",
                    "*S-1-5-32-544:(OI)(CI)(F)",
                    "*S-1-5-32-545:(OI)(CI)(RX)",
                    "/T",
                    "/C",
                    "/Q"
                ],
                cancellationToken);
        }

        var authPath = GetParentAuthPath();
        if (File.Exists(authPath))
        {
            await RunAsync(
                "icacls.exe",
                [
                    authPath,
                    "/inheritance:r",
                    "/grant:r",
                    "*S-1-5-18:(F)",
                    "*S-1-5-32-544:(F)",
                    "/C",
                    "/Q"
                ],
                cancellationToken);
        }

        TamperLog.MaintenanceOpened(logger);
    }

    internal static string GetInstallDirectory()
    {
        var serviceDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        return serviceDirectory.Parent?.FullName ?? AppContext.BaseDirectory;
    }

    internal static string GetParentAuthPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "TuoiTho",
            "parent-auth.json");
    }

    private static async Task RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"Could not start {fileName}.");

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        var output = await standardOutput;
        var error = await standardError;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{fileName} exited with code {process.ExitCode}. {output} {error}".Trim());
        }
    }
}

public sealed class TamperMaintenanceCoordinator(
    IOptions<ProductionProtectionOptions> protectionOptions,
    ITamperProtectionPlatform platform,
    TimeProvider timeProvider,
    ILogger<TamperMaintenanceCoordinator> logger) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool hardened;
    private DateTimeOffset? maintenanceUntil;

    public DateTimeOffset? MaintenanceUntil => maintenanceUntil;

    public async Task EnsureExpectedStateAsync(CancellationToken cancellationToken)
    {
        if (!protectionOptions.Value.Enabled) return;
        if (!File.Exists(WindowsTamperProtectionPlatform.GetParentAuthPath())) return;

        await gate.WaitAsync(cancellationToken);
        try
        {
            var now = timeProvider.GetUtcNow();
            if (maintenanceUntil is not null && now < maintenanceUntil.Value)
            {
                return;
            }

            maintenanceUntil = null;
            if (hardened) return;

            await platform.ApplyHardenedStateAsync(cancellationToken);
            hardened = true;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ForceHardenAsync(CancellationToken cancellationToken)
    {
        if (!protectionOptions.Value.Enabled) return;
        if (!File.Exists(WindowsTamperProtectionPlatform.GetParentAuthPath())) return;

        await gate.WaitAsync(cancellationToken);
        try
        {
            await platform.ApplyHardenedStateAsync(cancellationToken);
            maintenanceUntil = null;
            hardened = true;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<TamperMaintenanceResponse> AuthorizeAsync(
        TamperMaintenanceRequest request,
        CancellationToken cancellationToken)
    {
        if (!protectionOptions.Value.Enabled)
        {
            return new TamperMaintenanceResponse(false, "Production protection is disabled.");
        }

        if (string.Equals(
                request.Command,
                TamperMaintenanceProtocol.CloseCommand,
                StringComparison.Ordinal))
        {
            await ForceHardenAsync(cancellationToken);
            TamperLog.MaintenanceClosed(logger);
            return new TamperMaintenanceResponse(true, "Maintenance window closed.");
        }

        if (!string.Equals(
                request.Command,
                TamperMaintenanceProtocol.AuthorizeCommand,
                StringComparison.Ordinal))
        {
            return new TamperMaintenanceResponse(false, "Unsupported maintenance command.");
        }

        if (string.IsNullOrEmpty(request.Password) ||
            request.Password.Length > TamperMaintenanceProtocol.MaximumPasswordLength)
        {
            return new TamperMaintenanceResponse(false, "Invalid parent password.");
        }

        var record = ReadPasswordRecord(WindowsTamperProtectionPlatform.GetParentAuthPath());
        if (record is null || !ParentPasswordHasher.Verify(request.Password, record))
        {
            TamperLog.AuthorizationRejected(logger);
            return new TamperMaintenanceResponse(false, "Parent password was not accepted.");
        }

        var requestedSeconds = request.DurationSeconds <= 0
            ? TamperMaintenanceProtocol.DefaultDurationSeconds
            : request.DurationSeconds;
        var durationSeconds = Math.Clamp(
            requestedSeconds,
            TamperMaintenanceProtocol.MinimumDurationSeconds,
            TamperMaintenanceProtocol.MaximumDurationSeconds);
        var expires = timeProvider.GetUtcNow().AddSeconds(durationSeconds);

        await gate.WaitAsync(cancellationToken);
        try
        {
            await platform.ApplyMaintenanceStateAsync(cancellationToken);
            maintenanceUntil = expires;
            hardened = false;
        }
        finally
        {
            gate.Release();
        }

        TamperLog.AuthorizationAccepted(logger, expires);
        return new TamperMaintenanceResponse(true, "Maintenance window opened.", expires);
    }

    public void Dispose() => gate.Dispose();

    private static ParentPasswordRecord? ReadPasswordRecord(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<ParentPasswordRecord>(File.ReadAllText(path))
                : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}

public sealed class TamperProtectionWorker(
    IOptions<ProductionProtectionOptions> protectionOptions,
    TamperMaintenanceCoordinator coordinator,
    ILogger<TamperProtectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!protectionOptions.Value.Enabled) return;

        TamperLog.WorkerStarted(logger);

        var guardTask = RunGuardLoopAsync(stoppingToken);
        var pipeTask = RunPipeLoopAsync(stoppingToken);
        await Task.WhenAll(guardTask, pipeTask);
    }

    private async Task RunGuardLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        do
        {
            try
            {
                await coordinator.EnsureExpectedStateAsync(token);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                TamperLog.ReconcileFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(token));
    }

    private async Task RunPipeLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                TamperMaintenanceProtocol.PipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            await pipe.WaitForConnectionAsync(token);

            try
            {
                await HandleClientAsync(pipe, token);
            }
            catch (Exception exception) when (
                exception is IOException or JsonException or InvalidOperationException)
            {
                TamperLog.ClientFailed(logger, exception);
            }
        }
    }

    private async Task HandleClientAsync(Stream stream, CancellationToken token)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, leaveOpen: true);
        using var writer = new StreamWriter(stream, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };

        var line = await reader.ReadLineAsync(token);
        if (string.IsNullOrWhiteSpace(line) || line.Length > 4096)
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(
                new TamperMaintenanceResponse(false, "Invalid maintenance request.")));
            return;
        }

        var request = JsonSerializer.Deserialize<TamperMaintenanceRequest>(line);
        if (request is null)
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(
                new TamperMaintenanceResponse(false, "Invalid maintenance request.")));
            return;
        }

        var response = await coordinator.AuthorizeAsync(request, token);
        await writer.WriteLineAsync(JsonSerializer.Serialize(response));
    }
}

internal static partial class TamperLog
{
    [LoggerMessage(EventId = 2850, Level = LogLevel.Information, Message = "Tamper protection worker started.")]
    public static partial void WorkerStarted(ILogger logger);

    [LoggerMessage(EventId = 2851, Level = LogLevel.Information, Message = "Tamper protection hardened service and install files.")]
    public static partial void Hardened(ILogger logger);

    [LoggerMessage(EventId = 2852, Level = LogLevel.Information, Message = "Tamper protection maintenance ACLs applied.")]
    public static partial void MaintenanceOpened(ILogger logger);

    [LoggerMessage(EventId = 2853, Level = LogLevel.Warning, Message = "Tamper maintenance authorization rejected.")]
    public static partial void AuthorizationRejected(ILogger logger);

    [LoggerMessage(EventId = 2854, Level = LogLevel.Information, Message = "Tamper maintenance authorized until {ExpiresUtc}.")]
    public static partial void AuthorizationAccepted(ILogger logger, DateTimeOffset expiresUtc);

    [LoggerMessage(EventId = 2855, Level = LogLevel.Warning, Message = "Tamper protection reconciliation failed.")]
    public static partial void ReconcileFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2856, Level = LogLevel.Warning, Message = "Tamper maintenance client request failed.")]
    public static partial void ClientFailed(ILogger logger, Exception exception);
}
