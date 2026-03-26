using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for pronunciation lexicon API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class PronunciationLexiconClient : IPronunciationLexiconClient
  {
    private readonly IBackendClient _backend;

    public PronunciationLexiconClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    public Task<List<PronunciationLexiconViewModel.LexiconEntryResponse>?> GetEntriesAsync(string? language, CancellationToken cancellationToken = default)
    {
      var lang = string.IsNullOrEmpty(language) ? "en" : language;
      return _backend.SendRequestAsync<object, List<PronunciationLexiconViewModel.LexiconEntryResponse>>(
          $"/api/lexicon/list?language={Uri.EscapeDataString(lang)}",
          null,
          HttpMethod.Get,
          cancellationToken);
    }

    public Task<PronunciationLexiconViewModel.LexiconEntryResponse?> AddEntryAsync(string word, string pronunciation, string language, string? partOfSpeech, string? notes, CancellationToken cancellationToken = default)
    {
      var request = new { word, pronunciation, language, part_of_speech = partOfSpeech, notes };
      return _backend.SendRequestAsync<object, PronunciationLexiconViewModel.LexiconEntryResponse>(
          "/api/lexicon/add",
          request,
          HttpMethod.Post,
          cancellationToken);
    }

    public Task<PronunciationLexiconViewModel.LexiconEntryResponse?> UpdateEntryAsync(string word, string pronunciation, string language, string? partOfSpeech, string? notes, CancellationToken cancellationToken = default)
    {
      var request = new { word, pronunciation, language, part_of_speech = partOfSpeech, notes };
      return _backend.SendRequestAsync<object, PronunciationLexiconViewModel.LexiconEntryResponse>(
          "/api/lexicon/update",
          request,
          HttpMethod.Put,
          cancellationToken);
    }

    public Task DeleteEntryAsync(string word, CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, Dictionary<string, object>>(
          $"/api/lexicon/remove/{Uri.EscapeDataString(word)}",
          null,
          HttpMethod.Delete,
          cancellationToken);
    }

    public Task<PronunciationLexiconViewModel.PhonemeEstimateResponse?> EstimatePhonemesAsync(string word, string language, CancellationToken cancellationToken = default)
    {
      var request = new { word, language };
      return _backend.SendRequestAsync<object, PronunciationLexiconViewModel.PhonemeEstimateResponse>(
          "/api/lexicon/phoneme",
          request,
          HttpMethod.Post,
          cancellationToken);
    }
  }
}
