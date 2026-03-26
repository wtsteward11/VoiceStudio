using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/dataset-editor. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class TrainingDatasetEditorClient : ITrainingDatasetEditorClient
  {
    private readonly IBackendClient _backend;

    public TrainingDatasetEditorClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<DatasetDetail?> GetDatasetDetailAsync(string datasetId, CancellationToken ct = default)
      => _backend.SendRequestAsync<object, DatasetDetail>(
          $"/api/dataset-editor/{Uri.EscapeDataString(datasetId)}",
          null,
          HttpMethod.Get,
          ct);

    /// <inheritdoc />
    public Task<DatasetDetail?> AddAudioAsync(string datasetId, string audioId, string? transcript, int? order, CancellationToken ct = default)
      => _backend.SendRequestAsync<object, DatasetDetail>(
          $"/api/dataset-editor/{Uri.EscapeDataString(datasetId)}/audio",
          new { audio_id = audioId, transcript, order },
          HttpMethod.Post,
          ct);

    /// <inheritdoc />
    public Task<DatasetDetail?> UpdateAudioAsync(string datasetId, string audioFileId, string? transcript, int? order, CancellationToken ct = default)
      => _backend.SendRequestAsync<object, DatasetDetail>(
          $"/api/dataset-editor/{Uri.EscapeDataString(datasetId)}/audio/{Uri.EscapeDataString(audioFileId)}",
          new { transcript, order },
          HttpMethod.Put,
          ct);

    /// <inheritdoc />
    public Task<DatasetDetail?> RemoveAudioAsync(string datasetId, string audioFileId, CancellationToken ct = default)
      => _backend.SendRequestAsync<object, DatasetDetail>(
          $"/api/dataset-editor/{Uri.EscapeDataString(datasetId)}/audio/{Uri.EscapeDataString(audioFileId)}",
          null,
          HttpMethod.Delete,
          ct);

    /// <inheritdoc />
    public Task<DatasetValidateResponse?> ValidateDatasetAsync(string datasetId, CancellationToken ct = default)
      => _backend.SendRequestAsync<object, DatasetValidateResponse>(
          $"/api/dataset-editor/{Uri.EscapeDataString(datasetId)}/validate",
          null,
          HttpMethod.Post,
          ct);
  }
}
