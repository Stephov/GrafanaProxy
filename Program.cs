using System.Net;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using GrafanaProxy.Services;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(5011, listenOptions => listenOptions.UseHttps());
});

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddControllers();
builder.Services.AddSingleton<SessionCookieStore>();
builder.Services.AddHttpClient();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();

app.MapControllers();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

// Auto-open browser
try
{
    var psi = new ProcessStartInfo
    {
        FileName = "https://localhost:5011/",
        UseShellExecute = true
    };
    Process.Start(psi);
}
catch { }

app.Run();