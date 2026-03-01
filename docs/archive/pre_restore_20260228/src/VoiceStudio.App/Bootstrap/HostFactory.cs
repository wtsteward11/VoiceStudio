using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace VoiceStudio.App.Hosting;

/// <summary>
/// Builds a Generic Host with IConfiguration, ILogging, and the application service container.
/// The host's IServiceProvider is passed to AppServices.Initialize() to bridge with the
/// existing static service locator pattern.
/// </summary>
public static class HostFactory
{
    public static IHost BuildHost(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
#if DEBUG
                config.AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false);
#endif
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .ConfigureServices((context, services) =>
            {
                Services.AppServices.RegisterServices(services, context.Configuration);
            })
            .Build();
    }
}
