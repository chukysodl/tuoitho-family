using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

using TuoiTho.Core.Time;
using TuoiTho.Core.Policy;
using TuoiTho.Service;
using TuoiTho.Storage;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "TuoiTho.Service";
});

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffzzz";
    options.JsonWriterOptions = new System.Text.Json.JsonWriterOptions
    {
        Indented = false
    };
});

builder.Services.Configure<WindowsTimeTrackingOptions>(
    builder.Configuration.GetSection(WindowsTimeTrackingOptions.SectionName));
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<SqliteDatabase>(_ => new SqliteDatabase(GetDatabasePath()));
builder.Services.AddSingleton<ITimeUsageStore, SqliteTimeUsageStore>();
builder.Services.AddSingleton<IDeviceTimePolicyStore, SqliteDeviceTimePolicyStore>();
builder.Services.Configure<ParentControlOptions>(builder.Configuration.GetSection(ParentControlOptions.SectionName));
builder.Services.Configure<M1BootstrapOptions>(builder.Configuration.GetSection(M1BootstrapOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PolicyChangeSignal>();
builder.Services.AddSingleton<ParentControlService>(services => new ParentControlService(services.GetRequiredService<IDeviceTimePolicyStore>(), services.GetRequiredService<ITimeUsageStore>(), services.GetRequiredService<IClock>(), services.GetRequiredService<DeviceTimePolicyEngine>(), services.GetRequiredService<PolicyChangeSignal>(), services.GetRequiredService<ActivitySampleCache>(), services.GetRequiredService<WindowsSessionEventSource>()));
builder.Services.AddSingleton<DeviceTimePolicyEngine>();
builder.Services.AddSingleton<LocalSessionWarningPublisher>();
builder.Services.AddSingleton<IPolicyWarningPublisher, LocalPolicyWarningPublisher>();
builder.Services.AddSingleton<DevicePolicyCoordinator>();
builder.Services.AddSingleton<IManagedSessionNativeApi, WindowsManagedSessionNativeApi>();
builder.Services.AddSingleton<SafeChildSessionEnforcer>();
builder.Services.AddSingleton<WtsSessionActivityProvider>();
builder.Services.AddSingleton<ActivitySampleCache>();
builder.Services.AddSingleton<IWindowsSessionActivityProvider, AgentReportedSessionActivityProvider>();
builder.Services.AddSingleton<IWindowsBootTimeProvider, WindowsBootTimeProvider>();

builder.Services.AddSingleton<IWindowsSessionNotificationSource, WindowsSessionNotificationPump>();
builder.Services.AddSingleton<WindowsSessionEventSource>();
builder.Services.AddSingleton<SessionTimeEngine>(services =>
{
    var options = services.GetRequiredService<IOptions<WindowsTimeTrackingOptions>>().Value;
    options.Validate();
    return new SessionTimeEngine(
        services.GetRequiredService<ITimeUsageStore>(),
        services.GetRequiredService<IClock>(),
        options.ProfileId);
});
builder.Services.AddHostedService<M1Bootstrapper>();
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<ParentControlListener>();
builder.Services.AddHostedService<ActivitySampleListener>();

using var host = builder.Build();
host.Run();

static string GetDatabasePath()
{
    var commonApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    var baseDirectory = string.IsNullOrWhiteSpace(commonApplicationData)
        ? AppContext.BaseDirectory
        : commonApplicationData;
    return Path.Combine(baseDirectory, "TuoiTho", "tuoitho.db");
}