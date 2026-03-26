using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/embedding-explorer. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class EmbeddingExplorerClient : IEmbeddingExplorerClient
  {
    private readonly IBackendClient _backend;

    public EmbeddingExplorerClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<EmbeddingVector[]?> GetEmbeddingsAsync(CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, EmbeddingVector[]>(
          "/api/embedding-explorer/embeddings",
          null,
          HttpMethod.Get,
          cancellationToken);

    /// <inheritdoc />
    public Task<EmbeddingVector?> ExtractEmbeddingAsync(string audioId, string? voiceProfileId, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, EmbeddingVector>(
          "/api/embedding-explorer/extract",
          new { audio_id = audioId, voice_profile_id = voiceProfileId, method = "default" },
          HttpMethod.Post,
          cancellationToken);

    /// <inheritdoc />
    public Task DeleteEmbeddingAsync(string embeddingId, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, object>(
          $"/api/embedding-explorer/embeddings/{Uri.EscapeDataString(embeddingId)}",
          null,
          HttpMethod.Delete,
          cancellationToken);

    /// <inheritdoc />
    public Task<EmbeddingSimilarity?> CompareEmbeddingsAsync(string embeddingId1, string embeddingId2, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, EmbeddingSimilarity>(
          "/api/embedding-explorer/compare",
          new { embedding_id_1 = embeddingId1, embedding_id_2 = embeddingId2 },
          HttpMethod.Post,
          cancellationToken);

    /// <inheritdoc />
    public Task<EmbeddingVisualization[]?> VisualizeEmbeddingsAsync(IEnumerable<string> embeddingIds, string method, int dimensions, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, EmbeddingVisualization[]>(
          $"/api/embedding-explorer/visualize?method={Uri.EscapeDataString(method)}&dimensions={dimensions}",
          embeddingIds.ToList(),
          HttpMethod.Post,
          cancellationToken);

    /// <inheritdoc />
    public Task<EmbeddingCluster[]?> ClusterEmbeddingsAsync(IEnumerable<string> embeddingIds, int numClusters, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, EmbeddingCluster[]>(
          $"/api/embedding-explorer/cluster?num_clusters={numClusters}&method=kmeans",
          embeddingIds.ToList(),
          HttpMethod.Post,
          cancellationToken);
  }
}
