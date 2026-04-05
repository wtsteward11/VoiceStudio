using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for Video Generation API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class VideoGenClient : IVideoGenClient
  {
    private readonly IBackendClient _backend;

    public VideoGenClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<List<string>> ListVideoEnginesAsync(CancellationToken cancellationToken = default)
    {
      return _backend.ListVideoEnginesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<VideoGenerateResponse> GenerateVideoAsync(VideoGenerateRequest request, CancellationToken cancellationToken = default)
    {
      return _backend.GenerateVideoAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public Task<VideoUpscaleResponse> UpscaleVideoAsync(VideoUpscaleRequest request, CancellationToken cancellationToken = default)
    {
      return _backend.UpscaleVideoAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public Task<VideoQualityMetricsResponse?> GetVideoQualityMetricsAsync(string videoId, CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, VideoQualityMetricsResponse>(
          $"/api/video/{Uri.EscapeDataString(videoId)}/quality",
          null,
          HttpMethod.Get,
          cancellationToken);
    }
  }
}
