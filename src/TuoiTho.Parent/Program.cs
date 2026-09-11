var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffzzz";
});

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    Component = "TuoiTho.Parent",
    Status = "ready"
}));

app.MapGet("/health", () => Results.Ok(new
{
    Status = "ok"
}));

app.Run();
