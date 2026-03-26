using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for pronunciation lexicon API (/api/lexicon/list, add, update, remove, phoneme).
  /// Use instead of IBackendClient for PronunciationLexicon panel.
  /// </summary>
  public interface IPronunciationLexiconClient
  {
    Task<List<PronunciationLexiconViewModel.LexiconEntryResponse>?> GetEntriesAsync(string? language, CancellationToken cancellationToken = default);
    Task<PronunciationLexiconViewModel.LexiconEntryResponse?> AddEntryAsync(string word, string pronunciation, string language, string? partOfSpeech, string? notes, CancellationToken cancellationToken = default);
    Task<PronunciationLexiconViewModel.LexiconEntryResponse?> UpdateEntryAsync(string word, string pronunciation, string language, string? partOfSpeech, string? notes, CancellationToken cancellationToken = default);
    Task DeleteEntryAsync(string word, CancellationToken cancellationToken = default);
    Task<PronunciationLexiconViewModel.PhonemeEstimateResponse?> EstimatePhonemesAsync(string word, string language, CancellationToken cancellationToken = default);
  }
}
