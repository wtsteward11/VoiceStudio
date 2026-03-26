using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for mix assistant API (/api/mix-assistant).
  /// Use instead of IBackendClient for analyze, apply, dismiss, presets, suggestions.
  /// </summary>
  public interface IMixAssistantClient
  {
    Task<MixSuggestion[]?> AnalyzeMixAsync(
      string projectId,
      bool analyzeLevels,
      bool analyzeFrequency,
      bool analyzeStereo,
      bool analyzeDynamics,
      CancellationToken cancellationToken = default);

    Task ApplySuggestionsAsync(
      string[] suggestionIds,
      bool applyAll,
      CancellationToken cancellationToken = default);

    Task DismissSuggestionAsync(string suggestionId, CancellationToken cancellationToken = default);

    Task<MixPreset?> GeneratePresetAsync(
      string projectId,
      string name,
      string? genre,
      CancellationToken cancellationToken = default);

    Task<MixSuggestion[]?> GetSuggestionsAsync(
      string? projectId = null,
      string? category = null,
      string? priority = null,
      CancellationToken cancellationToken = default);
  }
}
