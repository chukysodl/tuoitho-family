using System.Security.Principal;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using TuoiTho.SessionAgent;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => { options.IncludeScopes = true; options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffzzz"; });
builder.Services.Configure<SessionAgentOptions>(builder.Configuration.GetSection(SessionAgentOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IChildWarningSink, DialogWarningSink>();
builder.Services.AddSingleton<IChildSoftLockController>(services =>
{
    var options = services.GetRequiredService<IOptions<SessionAgentOptions>>().Value;
    return new ChildSoftLockController(new WinFormsChildSoftLockView(), options.ProfileId, options.M1TestMode, new M1ParentControlLauncher(options.ParentExecutablePath));
});
builder.Services.AddSingleton<IRawInputActivityTracker, RawInputActivityTracker>();
builder.Services.AddSingleton<IWindowsIdleTimeDiagnostics, WindowsIdleTimeDiagnostics>();
builder.Services.AddSingleton<IInteractiveActivitySampler, InteractiveActivitySampler>();
builder.Services.AddSingleton<LocalActivityReporter>();
builder.Services.AddSingleton<LocalWarningListener>(services =>
{
    var options = services.GetRequiredService<IOptions<SessionAgentOptions>>().Value;
    SecurityIdentifier? publisher = null;
    if (options.M1TestMode && !string.IsNullOrWhiteSpace(options.M1WarningPublisherSid)) publisher = new SecurityIdentifier(options.M1WarningPublisherSid);
    return new LocalWarningListener(options.ProfileId, System.Diagnostics.Process.GetCurrentProcess().SessionId, services.GetRequiredService<IChildWarningSink>(), services.GetRequiredService<ILogger<LocalWarningListener>>(), publisher, services.GetRequiredService<IChildSoftLockController>());
});
builder.Services.AddHostedService<Worker>();
using var host = builder.Build();
host.Run();