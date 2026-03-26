using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/ensemble. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class EnsembleSynthesisClient : IEnsembleSynthesisClient
  {
    private readonly IBackendClient _backend;

    public EnsembleSynthesisClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<List<string>> GetEnginesAsync(CancellationToken cancellationToken = default)
      => _backend.GetEnginesAsync(cancellationToken);

    /// <inheritdoc />
    public Task<EnsembleSynthesisResponse?> CreateSynthesisAsync(EnsembleSynthesisRequest request, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<EnsembleSynthesisRequest, EnsembleSynthesisResponse>(
          "/api/ensemble",
          request,
          HttpMethod.Post,
          cancellationToken);

    /// <inheritdoc />
    public async Task<EnsembleJobStatus[]?> ListJobsAsync(string? projectId = null, CancellationToken cancellationToken = default)
    {
      var url = "/api/ensemble";
      if (!string.IsNullOrEmpty(projectId))
        url += $"?project_id={Uri.EscapeDataString(projectId)}";
      return await _backend.SendRequestAsync<object, EnsembleJobStatus[]>(
          url,
          null,
          HttpMethod.Get,
          cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
      await _backend.SendRequestAsync<object, object>(
          $"/api/ensemble/{Uri.EscapeDataString(jobId)}",
          null,
          HttpMethod.Delete,
          cancellationToken);
    }
  }
}
