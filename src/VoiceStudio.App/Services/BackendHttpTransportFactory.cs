using System;
using System.Net.Http;
using System.Text.Json;
using Polly.CircuitBreaker;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Builds the shared <see cref="HttpClient"/> handler chain and <see cref="BackendClientHttpPipeline"/>
  /// for production and test ctor paths (ADR-051 / GAP-022).
  /// </summary>
  internal static class BackendHttpTransportFactory
  {
    internal static (HttpClient HttpClient, BackendClientHttpPipeline Pipeline) Create(
        BackendClientConfig config,
        JsonSerializerOptions jsonOptions,
        ICorrelationIdProvider? correlationProvider,
        IRequestMetricsService? requestMetrics,
        GracefulDegradationService? gracefulDegradation,
        HttpMessageHandler? innerHandler)
    {
      var stateProvider = new CircuitBreakerStateProvider();
      var resiliencePipeline = BackendHttpResiliencePolicies.CreatePipeline(stateProvider, config.RequestTimeout);

      var httpHandler = innerHandler ?? new HttpClientHandler();
      var correlationHandler = correlationProvider != null
        ? new CorrelationIdHandler(httpHandler, correlationProvider)
        : new CorrelationIdHandler(httpHandler);
      var metricsOrCorrelation = requestMetrics != null
        ? new RequestMetricsHandler(requestMetrics, correlationHandler)
        : (HttpMessageHandler)correlationHandler;

      var resilienceHandler = new ResiliencePipelineDelegatingHandler(metricsOrCorrelation, resiliencePipeline);
      var rootHandler = new DegradedModeClearHandler(gracefulDegradation, resilienceHandler);

      var httpClient = new HttpClient(rootHandler)
      {
        BaseAddress = new Uri(config.BaseUrl),
        Timeout = config.RequestTimeout,
      };

      var pipeline = new BackendClientHttpPipeline(httpClient, jsonOptions, stateProvider);
      return (httpClient, pipeline);
    }
  }
}
