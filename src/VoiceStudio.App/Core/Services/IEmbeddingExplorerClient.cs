using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for embedding explorer API (/api/embedding-explorer).
  /// Use instead of IBackendClient for embeddings list, extract, delete, compare, visualize, cluster.
  /// </summary>
  public interface IEmbeddingExplorerClient
  {
    Task<EmbeddingVector[]?> GetEmbeddingsAsync(CancellationToken cancellationToken = default);
    Task<EmbeddingVector?> ExtractEmbeddingAsync(string audioId, string? voiceProfileId, CancellationToken cancellationToken = default);
    Task DeleteEmbeddingAsync(string embeddingId, CancellationToken cancellationToken = default);
    Task<EmbeddingSimilarity?> CompareEmbeddingsAsync(string embeddingId1, string embeddingId2, CancellationToken cancellationToken = default);
    Task<EmbeddingVisualization[]?> VisualizeEmbeddingsAsync(IEnumerable<string> embeddingIds, string method, int dimensions, CancellationToken cancellationToken = default);
    Task<EmbeddingCluster[]?> ClusterEmbeddingsAsync(IEnumerable<string> embeddingIds, int numClusters, CancellationToken cancellationToken = default);
  }
}
