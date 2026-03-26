using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for plugin health dashboard and metrics.
  /// Use instead of IBackendClient for plugin health panel.
  /// </summary>
  public interface IPluginHealthClient
  {
    /// <summary>
    /// Gets the plugin health dashboard data.
    /// </summary>
    Task<PluginHealthDashboardResponse?> GetDashboardAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets metrics for a specific plugin.
    /// </summary>
    Task<PluginMetricsResponse?> GetMetricsAsync(string pluginId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports plugin metrics in the specified format.
    /// </summary>
    Task<string> ExportMetricsAsync(string format = "json", CancellationToken cancellationToken = default);
  }
}
