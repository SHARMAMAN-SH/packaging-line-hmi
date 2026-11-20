var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<LineMonitor>();
builder.Services.AddHostedService(services => services.GetRequiredService<LineMonitor>());

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/api/line", (LineMonitor monitor, HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return monitor.Current;
});
var port = builder.Configuration.GetValue("HmiPort", 5070);
app.Run($"http://127.0.0.1:{port}");
