using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for plugin health dashboard and metrics. PR-3: owns HTTP implementation;
  /// uses shared BackendClientHttpPipeline (retry, circuit breaker) — no IBackendClient delegation.
  /// </summary>
  public sealed class PluginHealthClient : IPluginHealthClient
  {
    private readonly BackendClientHttpPipeline _pipeline;

    internal PluginHealthClient(BackendClientHttpPipeline pipeline)
    {
      _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    /// <inheritdoc />
    public Task<PluginHealthDashboardResponse?> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
      return _pipeline.SendRequestAsync<object?, PluginHealthDashboardResponse>(
        "/api/plugins/health/dashboard",
        null,
        HttpMethod.Get,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<PluginMetricsResponse?> GetMetricsAsync(string pluginId, CancellationToken cancellationToken = default)
    {
      var encodedPluginId = Uri.EscapeDataString(pluginId);
      return _pipeline.SendRequestAsync<object?, PluginMetricsResponse>(
        $"/api/plugins/{encodedPluginId}/metrics",
        null,
        HttpMethod.Get,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<string> ExportMetricsAsync(string format = "json", CancellationToken cancellationToken = default)
    {
      return _pipeline.GetStringAsync($"/api/plugins/metrics/export?format={Uri.EscapeDataString(format)}", cancellationToken);
    }
  }
}
