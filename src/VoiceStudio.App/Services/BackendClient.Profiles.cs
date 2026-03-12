using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Exceptions;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Profiles domain methods for BackendClient (partial).
  /// </summary>
  public partial class BackendClient
  {
    public async Task<List<VoiceProfile>> GetProfilesAsync(CancellationToken cancellationToken = default)
    {
      var list = await _requestCoordinator.GetOrCreateAsync(
        "profiles:list",
        async ct => await GetProfilesCoreAsync(ct).ConfigureAwait(false),
        TimeSpan.FromSeconds(30),
        cancellationToken).ConfigureAwait(false);
      return list ?? new List<VoiceProfile>();
    }

    private async Task<List<VoiceProfile>> GetProfilesCoreAsync(CancellationToken cancellationToken)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync("/api/profiles", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        var jsonString = await response.Content.ReadAsStringAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(jsonString))
        {
          throw new BackendDeserializationException(
            "Backend returned empty response for profiles. Verify backend is running.");
        }

        if (jsonString.TrimStart().StartsWith("<"))
        {
          var preview = jsonString.Substring(0, Math.Min(200, jsonString.Length));
          throw new BackendDeserializationException(
            $"Backend returned HTML instead of JSON. This typically means the backend server is not running or returned an error page. Preview: {preview}");
        }

        try
        {
          using var doc = System.Text.Json.JsonDocument.Parse(jsonString);

          if (doc.RootElement.TryGetProperty("items", out var itemsElement))
          {
            return System.Text.Json.JsonSerializer.Deserialize<List<VoiceProfile>>(itemsElement.GetRawText(), _jsonOptions)
                      ?? new List<VoiceProfile>();
          }

          return System.Text.Json.JsonSerializer.Deserialize<List<VoiceProfile>>(jsonString, _jsonOptions)
                    ?? new List<VoiceProfile>();
        }
        catch (System.Text.Json.JsonException ex)
        {
          var preview = jsonString.Substring(0, Math.Min(500, jsonString.Length));
          throw new BackendDeserializationException(
            $"Failed to parse profiles response. Ensure backend API is returning valid JSON. Content preview: {preview}", ex);
        }
      });
    }

    private void InvalidateProfilesCache()
    {
      _requestCoordinator.Invalidate("profiles:list");
    }

    public async Task<VoiceProfile> GetProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/profiles/{profileId}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<VoiceProfile>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize profile");
      });
    }

    public async Task<VoiceProfile> CreateProfileAsync(
        string name,
        string language = "en",
        string? emotion = null,
        List<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
      var result = await ExecuteWithRetryAsync(async () =>
      {
        var request = new
        {
          name,
          language,
          emotion,
          tags = tags ?? new List<string>()
        };

        var response = await _httpClient.PostAsJsonAsync("/api/profiles", request, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<VoiceProfile>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize profile");
      });
      InvalidateProfilesCache();
      return result;
    }

    public async Task<VoiceProfile> UpdateProfileAsync(
        string profileId,
        string? name = null,
        string? language = null,
        string? emotion = null,
        List<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
      var result = await ExecuteWithRetryAsync(async () =>
      {
        var request = new Dictionary<string, object?>();
        if (name != null) request["name"] = name;
        if (language != null) request["language"] = language;
        if (emotion != null) request["emotion"] = emotion;
        if (tags != null) request["tags"] = tags;

        var response = await _httpClient.PutAsJsonAsync(
                  $"/api/profiles/{profileId}",
                  request,
                  _jsonOptions,
                  cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<VoiceProfile>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize profile");
      });
      InvalidateProfilesCache();
      return result;
    }

    public async Task<bool> DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
      var result = await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.DeleteAsync($"/api/profiles/{profileId}", cancellationToken);
        return response.IsSuccessStatusCode;
      });
      if (result)
        InvalidateProfilesCache();
      return result;
    }
  }
}
