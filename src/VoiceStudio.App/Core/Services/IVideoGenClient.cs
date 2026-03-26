using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for Video Generation API (/api/video).
  /// Use instead of IBackendClient for VideoGen panel.
  /// </summary>
  public interface IVideoGenClient
  {
    Task<List<string>> ListVideoEnginesAsync(CancellationToken cancellationToken = default);

    Task<VideoGenerateResponse> GenerateVideoAsync(VideoGenerateRequest request, CancellationToken cancellationToken = default);

    Task<VideoUpscaleResponse> UpscaleVideoAsync(VideoUpscaleRequest request, CancellationToken cancellationToken = default);

    Task<VideoQualityMetricsResponse?> GetVideoQualityMetricsAsync(string videoId, CancellationToken cancellationToken = default);
  }
}
