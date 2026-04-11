using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;
using VoiceStudio.Core.Exceptions;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Applies a Polly <see cref="ResiliencePipeline{HttpResponseMessage}"/> around outgoing HTTP sends (ADR-051).
  /// </summary>
  internal sealed class ResiliencePipelineDelegatingHandler : DelegatingHandler
  {
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    public ResiliencePipelineDelegatingHandler(
        HttpMessageHandler inner,
        ResiliencePipeline<HttpResponseMessage> pipeline)
      : base(inner)
    {
      _pipeline = pipeline ?? throw new System.ArgumentNullException(nameof(pipeline));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
      try
      {
        return await _pipeline
            .ExecuteAsync(
                async ct => await base.SendAsync(request, ct).ConfigureAwait(false),
                cancellationToken)
            .ConfigureAwait(false);
      }
      catch (BrokenCircuitException ex)
      {
        throw new BackendUnavailableException(
            "Service is temporarily unavailable. Please try again in a moment.",
            ex);
      }
      catch (TimeoutRejectedException ex)
      {
        throw new BackendTimeoutException(
            "The request timed out. Please check your network connection and try again.",
            ex);
      }
    }
  }
}
