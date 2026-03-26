using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/engines/configure. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class EngineParameterTuningClient : IEngineParameterTuningClient
  {
    private readonly IBackendClient _backend;

    public EngineParameterTuningClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task ConfigureEngineAsync(
      string engineId,
      IReadOnlyDictionary<string, object> parameters,
      CancellationToken cancellationToken = default)
    {
      var request = new
      {
        engine_id = engineId,
        parameters
      };
      return _backend.SendRequestAsync<object, object>(
        "/api/engines/configure",
        request,
        HttpMethod.Post,
        cancellationToken);
    }
  }
}
