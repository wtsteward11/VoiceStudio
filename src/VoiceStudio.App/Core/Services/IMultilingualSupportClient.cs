using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for Multilingual API (/api/multilingual).
  /// Use instead of IBackendClient for MultilingualSupport panel.
  /// </summary>
  public interface IMultilingualSupportClient
  {
    Task<SupportedLanguagesResponse?> GetSupportedLanguagesAsync(CancellationToken cancellationToken = default);

    Task<TranslationResponse?> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken = default);

    Task<MultilingualSynthesisResponse?> SynthesizeAsync(MultilingualSynthesisRequest request, CancellationToken cancellationToken = default);
  }
}
