using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Core.Models;
using VoiceStudio.Core.Exceptions;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Engines domain methods for BackendClient (partial).
  /// </summary>
  public partial class BackendClient
  {
    public async Task<List<string>> GetEnginesAsync(CancellationToken cancellationToken = default)
    {
      var list = await _requestCoordinator.GetOrCreateAsync(
        "engines:list",
        async ct => await GetEnginesCoreAsync(ct).ConfigureAwait(false),
        TimeSpan.FromSeconds(60),
        cancellationToken).ConfigureAwait(false);
      return list ?? new List<string>();
    }

    private async Task<List<string>> GetEnginesCoreAsync(CancellationToken cancellationToken)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync("/api/engines/list", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        var result = await response.Content.ReadFromJsonAsync<EnginesListResponse>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize engines list");

        return result.EngineIds;
      });
    }

    public async Task<EngineRecommendationResponse> GetEngineRecommendationAsync(EngineRecommendationRequest request, CancellationToken cancellationToken = default)
    {
      const string url = "/api/engines/recommend";
      return await PostAsync<EngineRecommendationRequest, EngineRecommendationResponse>(url, request, cancellationToken)
          ?? throw new BackendDeserializationException("Failed to deserialize engine recommendation");
    }
  }
}
