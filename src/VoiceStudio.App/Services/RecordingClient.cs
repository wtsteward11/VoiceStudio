using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/recording. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class RecordingClient : IRecordingClient
  {
    private readonly IBackendClient _backend;

    public RecordingClient(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<AudioUploadResponse> UploadAudioFileAsync(string filePath, CancellationToken cancellationToken = default)
      => _backend.UploadAudioFileAsync(filePath, cancellationToken);

    /// <inheritdoc />
    public Task<RecordingDevicesResponse?> GetRecordingDevicesAsync(CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, RecordingDevicesResponse>(
          "/api/recording/devices",
          null,
          HttpMethod.Get,
          cancellationToken);
  }
}
