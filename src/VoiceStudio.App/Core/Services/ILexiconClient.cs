using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for pronunciation lexicon API.
  /// Use instead of IBackendClient for lexicon panel.
  /// </summary>
  public interface ILexiconClient
  {
    Task<LexiconViewModel.Lexicon[]?> GetLexiconsAsync(CancellationToken cancellationToken = default);
    Task<LexiconViewModel.Lexicon?> CreateLexiconAsync(string name, string language, string? description, CancellationToken cancellationToken = default);
    Task<LexiconViewModel.Lexicon?> UpdateLexiconAsync(string lexiconId, string name, string language, string? description, CancellationToken cancellationToken = default);
    Task DeleteLexiconAsync(string lexiconId, CancellationToken cancellationToken = default);
    Task<LexiconViewModel.LexiconEntry[]?> GetEntriesAsync(string lexiconId, CancellationToken cancellationToken = default);
    Task<LexiconViewModel.LexiconEntry?> CreateEntryAsync(string lexiconId, string word, string pronunciation, string? partOfSpeech, string? notes, CancellationToken cancellationToken = default);
    Task<LexiconViewModel.LexiconEntry?> UpdateEntryAsync(string lexiconId, string word, string pronunciation, string? partOfSpeech, string? notes, CancellationToken cancellationToken = default);
    Task DeleteEntryAsync(string lexiconId, string word, CancellationToken cancellationToken = default);
    Task<LexiconViewModel.LexiconSearchResponse?> SearchAsync(string query, CancellationToken cancellationToken = default);
  }
}
