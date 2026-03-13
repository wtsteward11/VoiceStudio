using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for training API (/api/training).
  /// Use instead of IBackendClient for datasets, jobs, logs, quality history.
  /// </summary>
  public interface ITrainingClient
  {
    Task<TrainingDataset> CreateDatasetAsync(string name, string? description = null, List<string>? audioFiles = null, CancellationToken ct = default);
    Task<List<TrainingDataset>> ListDatasetsAsync(CancellationToken ct = default);
    Task<TrainingDataset> GetDatasetAsync(string datasetId, CancellationToken ct = default);
    Task<bool> DeleteDatasetAsync(string datasetId, CancellationToken ct = default);
    Task<TrainingStatus> StartTrainingAsync(TrainingRequest request, CancellationToken ct = default);
    Task<TrainingStatus> GetTrainingStatusAsync(string trainingId, CancellationToken ct = default);
    Task<List<TrainingStatus>> ListTrainingJobsAsync(string? profileId = null, string? status = null, CancellationToken ct = default);
    Task<bool> CancelTrainingAsync(string trainingId, CancellationToken ct = default);
    Task<bool> DeleteTrainingJobAsync(string trainingId, CancellationToken ct = default);
    Task<List<TrainingLogEntry>> GetTrainingLogsAsync(string trainingId, int? limit = null, CancellationToken ct = default);
    Task<List<TrainingQualityMetrics>> GetTrainingQualityHistoryAsync(string trainingId, int? limit = null, CancellationToken ct = default);
  }
}
