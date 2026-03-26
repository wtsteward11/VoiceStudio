using System;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Exceptions;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/video/edit. PR-16: owns HTTP via BackendClientHttpPipeline; no IBackendClient delegation.
  /// </summary>
  public sealed class VideoEditClient : IVideoEditClient
  {
    private readonly BackendClientHttpPipeline _pipeline;

    /// <summary>
    /// For DI: use BackendHttpContext.Pipeline. Tests use this ctor with pipeline from CreateVideoEditClient.
    /// </summary>
    internal VideoEditClient(BackendClientHttpPipeline pipeline)
    {
      _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    /// <inheritdoc />
    public async Task<VideoInfo> GetVideoInfoAsync(string videoPath, CancellationToken cancellationToken = default)
    {
      var endpoint = $"/api/video/edit/info?path={Uri.EscapeDataString(videoPath)}";
      var result = await _pipeline.GetAsync<VideoInfo>(endpoint, cancellationToken).ConfigureAwait(false);
      return result ?? throw new BackendDeserializationException("Failed to deserialize video info");
    }

    /// <inheritdoc />
    public Task<VideoEditResponse> EditVideoAsync(VideoEditRequest request, CancellationToken cancellationToken = default)
    {
      return _pipeline.PostAsync<VideoEditRequest, VideoEditResponse>("/api/video/edit", request, cancellationToken);
    }
  }
}
