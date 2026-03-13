using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for /api/image-search.
  /// Thin pass-through to IBackendClient.
  /// </summary>
  public interface IImageSearchClient
  {
    /// <summary>
    /// Searches for images with the given request.
    /// </summary>
    Task<ImageSearchResponse?> SearchAsync(ImageSearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets available image sources.
    /// </summary>
    Task<ImageSourceInfo[]?> GetSourcesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets available search categories.
    /// </summary>
    Task<string[]?> GetCategoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets available color filters.
    /// </summary>
    Task<string[]?> GetColorsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears search history.
    /// </summary>
    Task ClearHistoryAsync(CancellationToken cancellationToken = default);
  }
}
