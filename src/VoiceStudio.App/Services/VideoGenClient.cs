using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Exceptions;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/video (generation, upscale). PR-16: owns HTTP via BackendClientHttpPipeline; no IBackendClient delegation.
  /// </summary>
  public sealed class VideoGenClient : IVideoGenClient
  {
    private readonly BackendClientHttpPipeline _pipeline;

    /// <summary>
    /// For DI: use BackendHttpContext.Pipeline. Tests use this ctor with pipeline from CreateVideoGenClient.
    /// </summary>
    internal VideoGenClient(BackendClientHttpPipeline pipeline)
    {
      _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    /// <inheritdoc />
    public async Task<List<string>> ListVideoEnginesAsync(CancellationToken cancellationToken = default)
    {
      var result = await _pipeline.GetAsync<VideoEnginesListResponse>("/api/video/engines/list", cancellationToken)
          .ConfigureAwait(false);
      return result == null
          ? throw new BackendDeserializationException("Failed to deserialize video engines list")
          : (result.Engines ?? new List<string>());
    }

    /// <inheritdoc />
    public Task<VideoGenerateResponse> GenerateVideoAsync(VideoGenerateRequest request, CancellationToken cancellationToken = default)
    {
      return _pipeline.PostAsync<VideoGenerateRequest, VideoGenerateResponse>("/api/video/generate", request, cancellationToken);
    }

    /// <inheritdoc />
    public Task<VideoUpscaleResponse> UpscaleVideoAsync(VideoUpscaleRequest request, CancellationToken cancellationToken = default)
    {
      return _pipeline.PostAsync<VideoUpscaleRequest, VideoUpscaleResponse>("/api/video/upscale", request, cancellationToken);
    }

    /// <inheritdoc />
    public Task<VideoQualityMetricsResponse?> GetVideoQualityMetricsAsync(string videoId, CancellationToken cancellationToken = default)
    {
      var endpoint = $"/api/video/{Uri.EscapeDataString(videoId)}/quality";
      return _pipeline.SendRequestAsync<object, VideoQualityMetricsResponse>(endpoint, null, HttpMethod.Get, cancellationToken);
    }
  }
}
