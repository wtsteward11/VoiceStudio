using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Core.Models;
using VoiceStudio.App.Services;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for recording API (/api/recording). Use instead of IBackendClient for recording workflows.
  /// </summary>
  public interface IRecordingClient
  {
    Task<AudioUploadResponse> UploadAudioFileAsync(string filePath, CancellationToken cancellationToken = default);
    Task<RecordingDevicesResponse?> GetRecordingDevicesAsync(CancellationToken cancellationToken = default);
  }
}
