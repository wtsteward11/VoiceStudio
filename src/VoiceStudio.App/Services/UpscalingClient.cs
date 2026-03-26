using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/upscaling (image and video upscaling).
  /// Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class UpscalingClient : IUpscalingClient
  {
    private readonly IBackendClient _backend;

    public UpscalingClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<UpscalingEngineResponse[]?> GetEnginesAsync(CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, UpscalingEngineResponse[]>(
        "/api/upscaling/engines",
        null,
        HttpMethod.Get,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<UpscalingJobResponse?> UploadAndUpscaleAsync(
      string filePath,
      UpscalingUpscaleRequest request,
      IProgress<double>? progress = null,
      CancellationToken cancellationToken = default)
    {
      var requestData = new
      {
        media_type = request.MediaType,
        engine = request.Engine,
        scale_factor = request.ScaleFactor,
        output_format = request.OutputFormat
      };
      var requestJson = JsonSerializer.Serialize(requestData);
      var additionalData = new Dictionary<string, string> { { "request", requestJson } };

      return _backend.UploadFileWithProgressAsync<UpscalingJobResponse>(
        "/api/upscaling/upscale",
        filePath,
        "file",
        additionalData,
        progress,
        TimeSpan.FromMinutes(30),
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<UpscalingJobResponse[]?> GetJobsAsync(CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, UpscalingJobResponse[]>(
        "/api/upscaling/jobs",
        null,
        HttpMethod.Get,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, object>(
        $"/api/upscaling/jobs/{Uri.EscapeDataString(jobId)}",
        null,
        HttpMethod.Delete,
        cancellationToken);
    }
  }
}
