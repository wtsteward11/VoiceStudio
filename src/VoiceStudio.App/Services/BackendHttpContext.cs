using System.Net.Http;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Shared HTTP client and pipeline for backend API. PR-3: enables PluginHealthClient to use
  /// the same retry/circuit policy as BackendClient without delegating through IBackendClient.
  /// </summary>
  internal sealed class BackendHttpContext
  {
    public HttpClient HttpClient { get; }
    public BackendClientHttpPipeline Pipeline { get; }

    public BackendHttpContext(
      BackendClientConfig config,
      ICorrelationIdProvider? correlationProvider,
      IRequestMetricsService? requestMetrics,
      GracefulDegradationService? gracefulDegradation,
      HttpMessageHandler? innerHandler = null)
    {
      var jsonOptions = JsonSerializerOptionsFactory.BackendApi;
      (HttpClient, Pipeline) = BackendHttpTransportFactory.Create(
        config,
        jsonOptions,
        correlationProvider,
        requestMetrics,
        gracefulDegradation,
        innerHandler);
    }
  }
}
