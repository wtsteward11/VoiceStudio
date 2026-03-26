using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for dataset QA API (/api/dataset). Use instead of IBackendClient for dataset QA workflows.
  /// </summary>
  public interface IDatasetQAClient
  {
    Task<List<TrainingDataset>> GetTrainingDatasetsAsync(CancellationToken cancellationToken = default);
    Task<TrainingDataset> GetTrainingDatasetAsync(string datasetId, CancellationToken cancellationToken = default);
    Task<JsonElement[]?> ScoreDatasetAsync(Dictionary<string, object> request, CancellationToken cancellationToken = default);
    Task CullLowQualityAsync(Dictionary<string, object> request, CancellationToken cancellationToken = default);
  }
}
