using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Serilog;

var logDir = @"C:\VoiceStudio\logs";
System.IO.Directory.CreateDirectory(logDir);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.File(System.IO.Path.Combine(logDir, "core.log"),
                  rollingInterval: RollingInterval.Day,
                  retainedFileCountLimit: 7,
                  shared: true)
    .WriteTo.Console()
    .CreateLogger();

try
{
    Log.Information("Booting VoiceStudio.Core...");
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();
    builder.Services.AddGrpc();

    var app = builder.Build();
    app.MapGrpcService<CoreService>();
    app.MapGet("/", () => "OK");
    Log.Information("Starting web host...");
    app.Run("http://127.0.0.1:5071");
}
catch (Exception ex)
{
    Log.Fatal(ex, "Core crashed during startup");
}
finally
{
    Log.CloseAndFlush();
}
