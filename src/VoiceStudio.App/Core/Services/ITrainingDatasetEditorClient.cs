using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for dataset-editor API (/api/dataset-editor).
  /// Use instead of IBackendClient for GetDataset, AddAudio, UpdateAudio, RemoveAudio, Validate.
  /// </summary>
  public interface ITrainingDatasetEditorClient
  {
    Task<DatasetDetail?> GetDatasetDetailAsync(string datasetId, CancellationToken ct = default);
    Task<DatasetDetail?> AddAudioAsync(string datasetId, string audioId, string? transcript, int? order, CancellationToken ct = default);
    Task<DatasetDetail?> UpdateAudioAsync(string datasetId, string audioFileId, string? transcript, int? order, CancellationToken ct = default);
    Task<DatasetDetail?> RemoveAudioAsync(string datasetId, string audioFileId, CancellationToken ct = default);
    Task<DatasetValidateResponse?> ValidateDatasetAsync(string datasetId, CancellationToken ct = default);
  }
}
