using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for Multilingual API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class MultilingualSupportClient : IMultilingualSupportClient
  {
    private readonly IBackendClient _backend;

    public MultilingualSupportClient(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<SupportedLanguagesResponse?> GetSupportedLanguagesAsync(CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, SupportedLanguagesResponse>(
        "/api/multilingual/supported",
        null,
        System.Net.Http.HttpMethod.Get,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<TranslationResponse?> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken = default)
    {
      var request = new
      {
        text = text ?? string.Empty,
        source_language = sourceLanguage ?? string.Empty,
        target_language = targetLanguage ?? string.Empty
      };
      return _backend.SendRequestAsync<object, TranslationResponse>(
        "/api/multilingual/translate",
        request,
        System.Net.Http.HttpMethod.Post,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<MultilingualSynthesisResponse?> SynthesizeAsync(MultilingualSynthesisRequest request, CancellationToken cancellationToken = default)
    {
      var body = new
      {
        text = request?.Text ?? string.Empty,
        source_language = request?.SourceLanguage,
        target_languages = request?.TargetLanguages ?? System.Array.Empty<string>(),
        profile_ids = request?.ProfileIds ?? new Dictionary<string, string>(),
        preserve_emotion = request?.PreserveEmotion ?? true,
        preserve_style = request?.PreserveStyle ?? true
      };
      return _backend.SendRequestAsync<object, MultilingualSynthesisResponse>(
        "/api/multilingual/synthesize",
        body,
        System.Net.Http.HttpMethod.Post,
        cancellationToken);
    }
  }
}
