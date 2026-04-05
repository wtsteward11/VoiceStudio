using System;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for Video Edit API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class VideoEditClient : IVideoEditClient
  {
    private readonly IBackendClient _backend;

    public VideoEditClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<VideoInfo> GetVideoInfoAsync(string videoPath, CancellationToken cancellationToken = default)
    {
      return _backend.GetVideoInfoAsync(videoPath, cancellationToken);
    }

    /// <inheritdoc />
    public Task<VideoEditResponse> EditVideoAsync(VideoEditRequest request, CancellationToken cancellationToken = default)
    {
      return _backend.EditVideoAsync(request, cancellationToken);
    }
  }
}
