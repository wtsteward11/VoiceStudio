using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/image-search.
  /// Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class ImageSearchClient : IImageSearchClient
  {
    private readonly IBackendClient _backend;

    public ImageSearchClient(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<ImageSearchResponse?> SearchAsync(ImageSearchRequest request, CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<ImageSearchRequest, ImageSearchResponse>(
        "/api/image-search/search",
        request,
        HttpMethod.Post,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<ImageSourceInfo[]?> GetSourcesAsync(CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, ImageSourceInfo[]>(
        "/api/image-search/sources",
        null,
        HttpMethod.Get,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<string[]?> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, string[]>(
        "/api/image-search/categories",
        null,
        HttpMethod.Get,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<string[]?> GetColorsAsync(CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, string[]>(
        "/api/image-search/colors",
        null,
        HttpMethod.Get,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, object>(
        "/api/image-search/history",
        null,
        HttpMethod.Delete,
        cancellationToken);
    }
  }
}
