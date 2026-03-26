using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for voice browser API (/api/voice-browser).
  /// Use instead of IBackendClient for SearchVoices, GetLanguages, GetTags.
  /// </summary>
  public interface IVoiceBrowserClient
  {
    Task<VoiceSearchResponse?> SearchVoicesAsync(
      string? query = null,
      string? language = null,
      string? gender = null,
      double minQualityScore = 0,
      string[]? tags = null,
      int limit = 50,
      int offset = 0,
      CancellationToken cancellationToken = default);

    Task<LanguagesResponse?> GetLanguagesAsync(CancellationToken cancellationToken = default);

    Task<TagsResponse?> GetTagsAsync(CancellationToken cancellationToken = default);
  }
}
