using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

using TuoiTho.Core.Time;
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
builder.Services.AddSingleton<IWindowsIdleTimeProvider, WindowsIdleTimeProvider>();
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
builder.Services.AddHostedService<Worker>();

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