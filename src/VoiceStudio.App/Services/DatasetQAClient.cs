using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/dataset. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class DatasetQAClient : IDatasetQAClient
  {
    private readonly IBackendClient _backend;

    public DatasetQAClient(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<List<TrainingDataset>> GetTrainingDatasetsAsync(CancellationToken cancellationToken = default)
      => _backend.GetTrainingDatasetsAsync(cancellationToken);

    /// <inheritdoc />
    public Task<TrainingDataset> GetTrainingDatasetAsync(string datasetId, CancellationToken cancellationToken = default)
      => _backend.GetTrainingDatasetAsync(datasetId, cancellationToken);

    /// <inheritdoc />
    public Task<JsonElement[]?> ScoreDatasetAsync(Dictionary<string, object> request, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<Dictionary<string, object>, JsonElement[]>(
          "/api/dataset/score",
          request,
          HttpMethod.Post,
          cancellationToken);

    /// <inheritdoc />
    public async Task CullLowQualityAsync(Dictionary<string, object> request, CancellationToken cancellationToken = default)
      => await _backend.SendRequestAsync<Dictionary<string, object>, object>(
          "/api/dataset/cull",
          request,
          HttpMethod.Post,
          cancellationToken);
  }
}
