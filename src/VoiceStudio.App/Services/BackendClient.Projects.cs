using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Exceptions;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Projects domain methods for BackendClient (partial).
  /// Uses RequestCoordinator for single-flight, TTL, and invalidation (same pattern as Profiles).
  /// </summary>
  public partial class BackendClient
  {
    public async Task<List<Project>> GetProjectsAsync(CancellationToken cancellationToken = default)
    {
      var list = await _requestCoordinator.GetOrCreateAsync(
        "projects:list",
        async ct => await GetProjectsCoreAsync(ct).ConfigureAwait(false),
        TimeSpan.FromSeconds(30),
        cancellationToken).ConfigureAwait(false);
      return list ?? new List<Project>();
    }

    private async Task<List<Project>> GetProjectsCoreAsync(CancellationToken cancellationToken)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync("/api/projects", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        // Backend returns paginated response: {"items": [...], "pagination": {...}}
        // Extract the items array from the wrapper
        var jsonString = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = System.Text.Json.JsonDocument.Parse(jsonString);

        if (doc.RootElement.TryGetProperty("items", out var itemsElement))
        {
          return System.Text.Json.JsonSerializer.Deserialize<List<Project>>(itemsElement.GetRawText(), _jsonOptions)
                    ?? new List<Project>();
        }

        // Fallback: try parsing as direct array for backward compatibility
        return System.Text.Json.JsonSerializer.Deserialize<List<Project>>(jsonString, _jsonOptions)
                  ?? new List<Project>();
      });
    }

    private void InvalidateProjectsCache()
    {
      _requestCoordinator.Invalidate("projects:list");
    }

    public async Task<Project> GetProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/projects/{projectId}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<Project>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize project");
      });
    }

    public async Task<Project> CreateProjectAsync(
        string name,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
      var result = await ExecuteWithRetryAsync(async () =>
      {
        var request = new
        {
          name,
          description
        };

        var response = await _httpClient.PostAsJsonAsync("/api/projects", request, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<Project>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize project");
      });
      InvalidateProjectsCache();
      return result;
    }

    public async Task<Project> UpdateProjectAsync(
        string projectId,
        string? name = null,
        string? description = null,
        List<string>? voiceProfileIds = null,
        CancellationToken cancellationToken = default)
    {
      var result = await ExecuteWithRetryAsync(async () =>
      {
        var request = new Dictionary<string, object?>();
        if (name != null) request["name"] = name;
        if (description != null) request["description"] = description;
        if (voiceProfileIds != null) request["voice_profile_ids"] = voiceProfileIds;

        var response = await _httpClient.PutAsJsonAsync(
                  $"/api/projects/{projectId}",
                  request,
                  _jsonOptions,
                  cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<Project>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize project");
      });
      InvalidateProjectsCache();
      return result;
    }

    public async Task<bool> DeleteProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
      var result = await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.DeleteAsync($"/api/projects/{projectId}", cancellationToken);
        return response.IsSuccessStatusCode;
      });
      if (result)
        InvalidateProjectsCache();
      return result;
    }
  }
}
