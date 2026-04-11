using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Polly v8 pipeline for backend <see cref="HttpClient"/> sends (ADR-051, GAP-022).
  /// </summary>
  internal static class BackendHttpResiliencePolicies
  {
    internal const int MaxRetryAttempts = 3;
    internal static readonly TimeSpan RetryBaseDelay = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan RetryMaxDelay = TimeSpan.FromSeconds(10);
    internal const double CircuitFailureRatio = 0.5;
    internal const int CircuitMinimumThroughput = 5;
    internal static readonly TimeSpan CircuitSamplingDuration = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan CircuitBreakDuration = TimeSpan.FromSeconds(30);

    internal static bool IsRetryableHttpResponse(HttpResponseMessage? response)
    {
      if (response is null)
      {
        return false;
      }

      var code = (int)response.StatusCode;
      if (code == 429)
      {
        return true;
      }

      return code is >= 500 and <= 504;
    }

    internal static ResiliencePipeline<HttpResponseMessage> CreatePipeline(
        CircuitBreakerStateProvider circuitStateProvider,
        TimeSpan perAttemptTimeout)
    {
      var shouldHandle = new PredicateBuilder<HttpResponseMessage>()
          .HandleResult(IsRetryableHttpResponse)
          .Handle<HttpRequestException>()
          .Handle<TimeoutException>()
          .Handle<TaskCanceledException>(static ex => !ex.CancellationToken.IsCancellationRequested);

      var retry = new RetryStrategyOptions<HttpResponseMessage>
      {
        MaxRetryAttempts = MaxRetryAttempts,
        Delay = RetryBaseDelay,
        MaxDelay = RetryMaxDelay,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        ShouldHandle = shouldHandle,
        OnRetry = static args =>
        {
          args.Outcome.Result?.Dispose();
          return default;
        },
      };

      var circuitBreaker = new CircuitBreakerStrategyOptions<HttpResponseMessage>
      {
        FailureRatio = CircuitFailureRatio,
        MinimumThroughput = CircuitMinimumThroughput,
        SamplingDuration = CircuitSamplingDuration,
        BreakDuration = CircuitBreakDuration,
        ShouldHandle = shouldHandle,
        StateProvider = circuitStateProvider,
      };

      var timeout = new TimeoutStrategyOptions
      {
        Timeout = perAttemptTimeout,
      };

      return new ResiliencePipelineBuilder<HttpResponseMessage>()
          .AddCircuitBreaker(circuitBreaker)
          .AddRetry(retry)
          .AddTimeout(timeout)
          .Build();
    }
  }
}
