using Microsoft.Extensions.Logging.Console;

using TuoiTho.SessionAgent;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffzzz";
});

builder.Services.AddHostedService<Worker>();

using var host = builder.Build();
host.Run();
