using System;
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
  public sealed class LexiconClient : ILexiconClient
  {
    private readonly IBackendClient _backend;

    public LexiconClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    public Task<LexiconViewModel.Lexicon[]?> GetLexiconsAsync(CancellationToken cancellationToken = default) =>
      _backend.SendRequestAsync<object, LexiconViewModel.Lexicon[]>("/api/lexicon/lexicons", null, HttpMethod.Get, cancellationToken);

    public Task<LexiconViewModel.Lexicon?> CreateLexiconAsync(string name, string language, string? description, CancellationToken cancellationToken = default)
    {
      var request = new { name, language, description };
      return _backend.SendRequestAsync<object, LexiconViewModel.Lexicon>("/api/lexicon/lexicons", request, HttpMethod.Post, cancellationToken);
    }

    public Task<LexiconViewModel.Lexicon?> UpdateLexiconAsync(string lexiconId, string name, string language, string? description, CancellationToken cancellationToken = default)
    {
      var request = new { name, language, description };
      var url = $"/api/lexicon/lexicons/{Uri.EscapeDataString(lexiconId)}";
      return _backend.SendRequestAsync<object, LexiconViewModel.Lexicon>(url, request, HttpMethod.Put, cancellationToken);
    }

    public Task DeleteLexiconAsync(string lexiconId, CancellationToken cancellationToken = default)
    {
      var url = $"/api/lexicon/lexicons/{Uri.EscapeDataString(lexiconId)}";
      return _backend.SendRequestAsync<object, object>(url, null, HttpMethod.Delete, cancellationToken);
    }

    public Task<LexiconViewModel.LexiconEntry[]?> GetEntriesAsync(string lexiconId, CancellationToken cancellationToken = default)
    {
      var url = $"/api/lexicon/lexicons/{Uri.EscapeDataString(lexiconId)}/entries";
      return _backend.SendRequestAsync<object, LexiconViewModel.LexiconEntry[]>(url, null, HttpMethod.Get, cancellationToken);
    }

    public Task<LexiconViewModel.LexiconEntry?> CreateEntryAsync(string lexiconId, string word, string pronunciation, string? partOfSpeech, string? notes, CancellationToken cancellationToken = default)
    {
      var request = new { word, pronunciation, part_of_speech = partOfSpeech, notes };
      var url = $"/api/lexicon/lexicons/{Uri.EscapeDataString(lexiconId)}/entries";
      return _backend.SendRequestAsync<object, LexiconViewModel.LexiconEntry>(url, request, HttpMethod.Post, cancellationToken);
    }

    public Task<LexiconViewModel.LexiconEntry?> UpdateEntryAsync(string lexiconId, string word, string pronunciation, string? partOfSpeech, string? notes, CancellationToken cancellationToken = default)
    {
      var request = new { word, pronunciation, part_of_speech = partOfSpeech, notes };
      var url = $"/api/lexicon/lexicons/{Uri.EscapeDataString(lexiconId)}/entries/{Uri.EscapeDataString(word)}";
      return _backend.SendRequestAsync<object, LexiconViewModel.LexiconEntry>(url, request, HttpMethod.Put, cancellationToken);
    }

    public Task DeleteEntryAsync(string lexiconId, string word, CancellationToken cancellationToken = default)
    {
      var url = $"/api/lexicon/lexicons/{Uri.EscapeDataString(lexiconId)}/entries/{Uri.EscapeDataString(word)}";
      return _backend.SendRequestAsync<object, object>(url, null, HttpMethod.Delete, cancellationToken);
    }

    public Task<LexiconViewModel.LexiconSearchResponse?> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
      var request = new { query };
      return _backend.SendRequestAsync<object, LexiconViewModel.LexiconSearchResponse>("/api/lexicon/search", request, HttpMethod.Post, cancellationToken);
    }
  }
}
