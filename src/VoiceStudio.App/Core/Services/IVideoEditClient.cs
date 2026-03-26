using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for Video Edit API (/api/video/edit).
  /// Use instead of IBackendClient for VideoEdit panel.
  /// </summary>
  public interface IVideoEditClient
  {
    Task<VideoInfo> GetVideoInfoAsync(string videoPath, CancellationToken cancellationToken = default);

    Task<VideoEditResponse> EditVideoAsync(VideoEditRequest request, CancellationToken cancellationToken = default);
  }
}
