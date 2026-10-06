using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

using TuoiTho.Core.Time;
using TuoiTho.Core.Policy;
using TuoiTho.Core.Remote;
using TuoiTho.Service;
using TuoiTho.Storage;

var builder = Host.CreateApplicationBuilder(args);

// Production installer writes machine-wide runtime protection settings here.
var productionConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TuoiTho", "production.json");
var productionMachineSettings = LoadMachineSettings(productionConfigPath);
if (productionMachineSettings.Count > 0)
{
    builder.Configuration.AddInMemoryCollection(productionMachineSettings);
}

// M5 setup writes public project settings to a protected, machine-wide config file.
var remoteConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TuoiTho", "RemoteControl", "remote-control.json");
var remoteMachineSettings = LoadMachineSettings(remoteConfigPath);
if (remoteMachineSettings.Count > 0)
{
    builder.Configuration.AddInMemoryCollection(remoteMachineSettings);
}
var effectiveRemoteOptions = LoadEffectiveRemoteOptions(remoteConfigPath, builder.Configuration);

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
builder.Services.AddSingleton<IAppPolicyStore, SqliteAppPolicyStore>();
builder.Services.AddSingleton<IWebPolicyStore, SqliteWebPolicyStore>();
builder.Services.AddSingleton<IRemoteCommandStateStore, SqliteRemoteCommandStateStore>();
builder.Services.AddSingleton<IRemotePolicyStore, SqliteRemotePolicyStore>();
builder.Services.AddSingleton<IDeviceCredentialProtector, WindowsDeviceCredentialProtector>();
var remoteRuntimeStatus = new RemoteControlRuntimeStatusCache();
remoteRuntimeStatus.Configure(effectiveRemoteOptions);
remoteRuntimeStatus.Provenance(
    Environment.ProcessPath,
    Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
    remoteConfigPath,
    File.Exists(remoteConfigPath));
builder.Services.AddSingleton(remoteRuntimeStatus);
builder.Services.AddSingleton<IOptions<RemoteControlOptions>>(Options.Create(effectiveRemoteOptions));
builder.Services.AddSingleton<SupabaseRemoteTransport>();
builder.Services.AddSingleton<IRemoteTransport>(services => services.GetRequiredService<SupabaseRemoteTransport>());
builder.Services.AddSingleton<RemoteDeviceIdentityManager>();
builder.Services.AddSingleton<BrowserRuntimeStatusCache>();
builder.Services.AddSingleton<AppPolicyEngine>();
builder.Services.AddSingleton<IManagedSessionAppDiscovery, WindowsManagedSessionAppDiscovery>();
builder.Services.AddSingleton<AppEnforcementState>();
builder.Services.AddSingleton<AppEnforcementAuditTrail>();
builder.Services.AddSingleton<IAppEnforcementProcessSource, WindowsAppEnforcementProcessSource>();
builder.Services.AddSingleton<IAppEnforcementProcessController, WindowsAppEnforcementProcessController>();
builder.Services.Configure<ParentControlOptions>(builder.Configuration.GetSection(ParentControlOptions.SectionName));
builder.Services.Configure<M1BootstrapOptions>(builder.Configuration.GetSection(M1BootstrapOptions.SectionName));
builder.Services.Configure<ProductionProtectionOptions>(builder.Configuration.GetSection(ProductionProtectionOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PolicyChangeSignal>();
builder.Services.AddSingleton<ITamperProtectionPlatform, WindowsTamperProtectionPlatform>();
builder.Services.AddSingleton<TamperMaintenanceCoordinator>();
builder.Services.AddSingleton<ParentControlService>(services => new ParentControlService(services.GetRequiredService<IDeviceTimePolicyStore>(), services.GetRequiredService<ITimeUsageStore>(), services.GetRequiredService<IClock>(), services.GetRequiredService<DeviceTimePolicyEngine>(), services.GetRequiredService<PolicyChangeSignal>(), services.GetRequiredService<ActivitySampleCache>(), services.GetRequiredService<WindowsSessionEventSource>(), services.GetRequiredService<SessionTimeEngine>(), services.GetRequiredService<IAppPolicyStore>(), services.GetRequiredService<AppPolicyEngine>(), services.GetRequiredService<IManagedSessionAppDiscovery>(), services.GetRequiredService<AppEnforcementState>(), services.GetRequiredService<AppEnforcementAuditTrail>(), services.GetRequiredService<IWebPolicyStore>(), services.GetRequiredService<BrowserRuntimeStatusCache>(), services.GetRequiredService<RemoteDeviceIdentityManager>(), services.GetRequiredService<IRemotePolicyStore>(), services.GetRequiredService<RemoteControlRuntimeStatusCache>(), services.GetRequiredService<IWindowsBootTimeProvider>()));
builder.Services.AddSingleton<DeviceTimePolicyEngine>();
builder.Services.AddSingleton<LocalSessionWarningPublisher>();
builder.Services.AddSingleton<IPolicyWarningPublisher, LocalPolicyWarningPublisher>();
builder.Services.AddSingleton<DevicePolicyCoordinator>();
builder.Services.AddSingleton<IManagedSessionNativeApi, WindowsManagedSessionNativeApi>();
builder.Services.AddSingleton<SafeChildSessionEnforcer>();
builder.Services.AddSingleton<IInteractiveSessionAgentRuntime, WindowsInteractiveSessionAgentRuntime>();
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
builder.Services.AddHostedService<ProductionSessionAgentWatchdog>();
builder.Services.AddHostedService<TamperProtectionWorker>();
builder.Services.AddHostedService<RemoteControlWorker>();
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<ParentControlListener>();
builder.Services.AddHostedService<ActivitySampleListener>();
builder.Services.AddHostedService<AppDiscoveryService>();
builder.Services.AddHostedService<AppEnforcementService>();
builder.Services.AddHostedService<BrowserPolicyListener>();

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


static RemoteControlOptions LoadEffectiveRemoteOptions(string path, IConfiguration configuration)
{
    var fallback = new RemoteControlOptions
    {
        Enabled = configuration.GetValue<bool>("RemoteControl:Enabled"),
        SupabaseUrl = configuration["RemoteControl:SupabaseUrl"] ?? string.Empty,
        SupabaseAnonKey = configuration["RemoteControl:SupabaseAnonKey"] ?? string.Empty,
        DeviceName = configuration["RemoteControl:DeviceName"] ?? Environment.MachineName,
        PairingFunctionName = configuration["RemoteControl:PairingFunctionName"] ?? "device-gateway",
        ParentFunctionName = configuration["RemoteControl:ParentFunctionName"] ?? "parent-gateway",
        PollIntervalSeconds = configuration.GetValue<int?>("RemoteControl:PollIntervalSeconds") ?? 5,
        StatusIntervalSeconds = configuration.GetValue<int?>("RemoteControl:StatusIntervalSeconds") ?? 15
    };

    if (!File.Exists(path)) return fallback;

    using var document = JsonDocument.Parse(File.ReadAllText(path));
    if (!document.RootElement.TryGetProperty("RemoteControl", out var remote) || remote.ValueKind != JsonValueKind.Object)
        return fallback;

    static string ReadString(JsonElement element, string name, string fallbackValue)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallbackValue
            : fallbackValue;

    static int ReadInt(JsonElement element, string name, int fallbackValue)
        => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) ? parsed : fallbackValue;

    static bool ReadBool(JsonElement element, string name, bool fallbackValue)
        => element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallbackValue;

    return new RemoteControlOptions
    {
        Enabled = ReadBool(remote, "Enabled", fallback.Enabled),
        SupabaseUrl = ReadString(remote, "SupabaseUrl", fallback.SupabaseUrl),
        SupabaseAnonKey = ReadString(remote, "SupabaseAnonKey", fallback.SupabaseAnonKey),
        DeviceName = ReadString(remote, "DeviceName", fallback.DeviceName),
        PairingFunctionName = ReadString(remote, "PairingFunctionName", fallback.PairingFunctionName),
        ParentFunctionName = ReadString(remote, "ParentFunctionName", fallback.ParentFunctionName),
        PollIntervalSeconds = ReadInt(remote, "PollIntervalSeconds", fallback.PollIntervalSeconds),
        StatusIntervalSeconds = ReadInt(remote, "StatusIntervalSeconds", fallback.StatusIntervalSeconds)
    };
}

static Dictionary<string, string?> LoadMachineSettings(string path)
{
    var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    if (!File.Exists(path)) return values;

    using var document = JsonDocument.Parse(File.ReadAllText(path));
    Flatten(document.RootElement, string.Empty, values);
    return values;
}

static void Flatten(JsonElement element, string prefix, IDictionary<string, string?> values)
{
    switch (element.ValueKind)
    {
        case JsonValueKind.Object:
            foreach (var property in element.EnumerateObject())
            {
                var key = string.IsNullOrEmpty(prefix) ? property.Name : prefix + ":" + property.Name;
                Flatten(property.Value, key, values);
            }
            break;
        case JsonValueKind.Array:
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                Flatten(item, prefix + ":" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), values);
                index++;
            }
            break;
        case JsonValueKind.String:
            values[prefix] = element.GetString();
            break;
        case JsonValueKind.Number:
            values[prefix] = element.GetRawText();
            break;
        case JsonValueKind.True:
        case JsonValueKind.False:
            values[prefix] = element.GetBoolean().ToString(System.Globalization.CultureInfo.InvariantCulture);
            break;
        case JsonValueKind.Null:
            values[prefix] = null;
            break;
    }
}
