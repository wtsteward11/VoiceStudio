using System;
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
      var httpHandler = innerHandler ?? new HttpClientHandler();
      var correlationHandler = correlationProvider != null
        ? new CorrelationIdHandler(httpHandler, correlationProvider)
        : new CorrelationIdHandler(httpHandler);
      var metricsOrCorrelation = requestMetrics != null
        ? new RequestMetricsHandler(requestMetrics, correlationHandler)
        : (HttpMessageHandler)correlationHandler;
      var rootHandler = new DegradedModeClearHandler(gracefulDegradation, metricsOrCorrelation);

      HttpClient = new HttpClient(rootHandler)
      {
        BaseAddress = new Uri(config.BaseUrl),
        Timeout = config.RequestTimeout
      };

      var jsonOptions = JsonSerializerOptionsFactory.BackendApi;
      Pipeline = new BackendClientHttpPipeline(HttpClient, jsonOptions);
    }
  }
}
