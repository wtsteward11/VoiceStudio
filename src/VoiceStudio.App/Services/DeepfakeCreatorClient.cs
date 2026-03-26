using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for Deepfake Creator API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class DeepfakeCreatorClient : IDeepfakeCreatorClient
  {
    private readonly IBackendClient _backend;

    public DeepfakeCreatorClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<DeepfakeEngine[]?> GetEnginesAsync(CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, DeepfakeEngine[]>(
        "/api/deepfake-creator/engines",
        null,
        System.Net.Http.HttpMethod.Get,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<DeepfakeJob[]?> GetJobsAsync(CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, DeepfakeJob[]>(
        "/api/deepfake-creator/jobs",
        null,
        System.Net.Http.HttpMethod.Get,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
      var url = $"/api/deepfake-creator/jobs/{Uri.EscapeDataString(jobId ?? "")}";
      return _backend.SendRequestAsync<object, object>(
        url,
        null,
        System.Net.Http.HttpMethod.Delete,
        cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DeepfakeJobResponse?> CreateDeepfakeAsync(
      string sourceFacePath,
      string targetMediaPath,
      DeepfakeCreateRequest request,
      IProgress<double>? progress = null,
      CancellationToken cancellationToken = default)
    {
      var requestJson = System.Text.Json.JsonSerializer.Serialize(request);
      var additionalData = new Dictionary<string, string> { { "request", requestJson } };
      var files = new Dictionary<string, string>
      {
        { "source_face", sourceFacePath ?? "" },
        { "target_media", targetMediaPath ?? "" }
      };

      return await _backend.UploadFilesWithProgressAsync<DeepfakeJobResponse>(
        "/api/deepfake-creator/create",
        files,
        additionalData,
        progress,
        TimeSpan.FromMinutes(30),
        cancellationToken);
    }
  }
}
