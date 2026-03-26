using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for global search API (/api/search).
  /// Use instead of IBackendClient for global search across panels and content types.
  /// Implements IDEA 5: Global Search with Panel Context.
  /// </summary>
  public interface ISearchClient
  {
    /// <summary>
    /// Performs a global search across all panels and content types.
    /// </summary>
    Task<SearchResponse> SearchAsync(string query, string? types = null, int limit = 50, CancellationToken cancellationToken = default);
  }
}
