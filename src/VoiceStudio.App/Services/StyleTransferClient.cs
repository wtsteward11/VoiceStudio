using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/style-transfer (job-based style transfer).
  /// Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class StyleTransferClient : IStyleTransferClient
  {
    private readonly IBackendClient _backend;

    public StyleTransferClient(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<StyleTransferPresetResponse[]?> GetPresetsAsync(CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, StyleTransferPresetResponse[]>(
        "/api/style-transfer/presets",
        null,
        HttpMethod.Get,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<StyleTransferJobResponse?> CreateTransferAsync(StyleTransferCreateRequest request, CancellationToken cancellationToken = default)
    {
      var body = new
      {
        source_audio_id = request.SourceAudioId,
        target_style_id = request.TargetStyleId,
        transfer_strength = request.TransferStrength,
        preserve_content = request.PreserveContent,
        preserve_emotion = request.PreserveEmotion,
        output_format = request.OutputFormat
      };
      return _backend.SendRequestAsync<object, StyleTransferJobResponse>(
        "/api/style-transfer/transfer",
        body,
        HttpMethod.Post,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<StyleTransferJobResponse[]?> GetJobsAsync(CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, StyleTransferJobResponse[]>(
        "/api/style-transfer/jobs",
        null,
        HttpMethod.Get,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, object>(
        $"/api/style-transfer/jobs/{System.Uri.EscapeDataString(jobId)}",
        null,
        HttpMethod.Delete,
        cancellationToken);
    }
  }
}
