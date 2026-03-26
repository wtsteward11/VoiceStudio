using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Exceptions;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/search. PR-4: owns HTTP via BackendClientHttpPipeline; no IBackendClient delegation.
  /// </summary>
  public sealed class SearchClient : ISearchClient
  {
    private readonly BackendClientHttpPipeline _pipeline;

    /// <summary>
    /// For DI: use BackendHttpContext.Pipeline. Tests use this ctor with mock pipeline.
    /// </summary>
    internal SearchClient(BackendClientHttpPipeline pipeline)
    {
      _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    /// <inheritdoc />
    public async Task<SearchResponse> SearchAsync(string query, string? types = null, int limit = 50, CancellationToken cancellationToken = default)
    {
      var queryParams = new List<string> { $"q={Uri.EscapeDataString(query)}", $"limit={limit}" };
      if (!string.IsNullOrEmpty(types))
        queryParams.Add($"types={Uri.EscapeDataString(types)}");
      var url = $"/api/search?{string.Join("&", queryParams)}";
      var result = await _pipeline.GetAsync<SearchResponse>(url, cancellationToken);
      return result ?? throw new BackendDeserializationException("Failed to deserialize search response");
    }
  }
}
