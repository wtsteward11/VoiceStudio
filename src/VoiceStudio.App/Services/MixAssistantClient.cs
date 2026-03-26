using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/mix-assistant. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class MixAssistantClient : IMixAssistantClient
  {
    private readonly IBackendClient _backend;

    public MixAssistantClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<MixSuggestion[]?> AnalyzeMixAsync(
      string projectId,
      bool analyzeLevels,
      bool analyzeFrequency,
      bool analyzeStereo,
      bool analyzeDynamics,
      CancellationToken cancellationToken = default)
    {
      var request = new
      {
        project_id = projectId,
        analyze_levels = analyzeLevels,
        analyze_frequency = analyzeFrequency,
        analyze_stereo = analyzeStereo,
        analyze_dynamics = analyzeDynamics
      };
      return _backend.SendRequestAsync<object, MixSuggestion[]>(
        "/api/mix-assistant/analyze",
        request,
        HttpMethod.Post,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task ApplySuggestionsAsync(
      string[] suggestionIds,
      bool applyAll,
      CancellationToken cancellationToken = default)
    {
      var request = new { suggestion_ids = suggestionIds, apply_all = applyAll };
      return _backend.SendRequestAsync<object, object>(
        "/api/mix-assistant/apply",
        request,
        HttpMethod.Post,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task DismissSuggestionAsync(string suggestionId, CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, object>(
        $"/api/mix-assistant/suggestions/{Uri.EscapeDataString(suggestionId)}",
        null,
        HttpMethod.Delete,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<MixPreset?> GeneratePresetAsync(
      string projectId,
      string name,
      string? genre,
      CancellationToken cancellationToken = default)
    {
      var url = $"/api/mix-assistant/presets/generate?project_id={Uri.EscapeDataString(projectId)}&name={Uri.EscapeDataString(name)}&genre={Uri.EscapeDataString(genre ?? "")}";
      return _backend.SendRequestAsync<object, MixPreset>(url, null, HttpMethod.Post, cancellationToken);
    }

    /// <inheritdoc />
    public Task<MixSuggestion[]?> GetSuggestionsAsync(
      string? projectId = null,
      string? category = null,
      string? priority = null,
      CancellationToken cancellationToken = default)
    {
      var queryParams = new System.Collections.Generic.List<string>();
      if (!string.IsNullOrEmpty(projectId))
        queryParams.Add($"project_id={Uri.EscapeDataString(projectId)}");
      if (!string.IsNullOrEmpty(category) && category != "all")
        queryParams.Add($"category={Uri.EscapeDataString(category)}");
      if (!string.IsNullOrEmpty(priority) && priority != "all")
        queryParams.Add($"priority={Uri.EscapeDataString(priority)}");

      var url = "/api/mix-assistant/suggestions";
      if (queryParams.Count > 0)
        url += "?" + string.Join("&", queryParams);

      return _backend.SendRequestAsync<object, MixSuggestion[]>(url, null, HttpMethod.Get, cancellationToken);
    }
  }
}
