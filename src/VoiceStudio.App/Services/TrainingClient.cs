using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/training. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class TrainingClient : ITrainingClient
  {
    private readonly IBackendClient _backend;

    public TrainingClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<TrainingDataset> CreateDatasetAsync(string name, string? description = null, List<string>? audioFiles = null, CancellationToken ct = default)
      => _backend.CreateDatasetAsync(name, description, audioFiles, ct);

    /// <inheritdoc />
    public Task<List<TrainingDataset>> ListDatasetsAsync(CancellationToken ct = default)
      => _backend.ListDatasetsAsync(ct);

    /// <inheritdoc />
    public Task<TrainingDataset> GetDatasetAsync(string datasetId, CancellationToken ct = default)
      => _backend.GetDatasetAsync(datasetId, ct);

    /// <inheritdoc />
    public Task<bool> DeleteDatasetAsync(string datasetId, CancellationToken ct = default)
      => _backend.DeleteDatasetAsync(datasetId, ct);

    /// <inheritdoc />
    public Task<TrainingStatus> StartTrainingAsync(TrainingRequest request, CancellationToken ct = default)
      => _backend.StartTrainingAsync(request, ct);

    /// <inheritdoc />
    public Task<TrainingStatus> GetTrainingStatusAsync(string trainingId, CancellationToken ct = default)
      => _backend.GetTrainingStatusAsync(trainingId, ct);

    /// <inheritdoc />
    public Task<List<TrainingStatus>> ListTrainingJobsAsync(string? profileId = null, string? status = null, CancellationToken ct = default)
      => _backend.ListTrainingJobsAsync(profileId, status, ct);

    /// <inheritdoc />
    public Task<bool> CancelTrainingAsync(string trainingId, CancellationToken ct = default)
      => _backend.CancelTrainingAsync(trainingId, ct);

    /// <inheritdoc />
    public Task<bool> DeleteTrainingJobAsync(string trainingId, CancellationToken ct = default)
      => _backend.DeleteTrainingJobAsync(trainingId, ct);

    /// <inheritdoc />
    public Task<List<TrainingLogEntry>> GetTrainingLogsAsync(string trainingId, int? limit = null, CancellationToken ct = default)
      => _backend.GetTrainingLogsAsync(trainingId, limit, ct);

    /// <inheritdoc />
    public Task<List<TrainingQualityMetrics>> GetTrainingQualityHistoryAsync(string trainingId, int? limit = null, CancellationToken ct = default)
      => _backend.GetTrainingQualityHistoryAsync(trainingId, limit, ct);
  }
}
