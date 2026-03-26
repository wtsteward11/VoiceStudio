using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/library. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class LibraryClient : ILibraryClient
  {
    private readonly IBackendClient _backend;

    public LibraryClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<LibraryFoldersResponse?> GetLibraryFoldersAsync(string? parentId = null, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, LibraryFoldersResponse>(
          $"/api/library/folders?parent_id={Uri.EscapeDataString(parentId ?? "")}",
          null,
          HttpMethod.Get,
          cancellationToken);

    /// <inheritdoc />
    public async Task<AssetSearchResponse?> SearchAssetsAsync(string? query = null, string? assetType = null, string? folderId = null, CancellationToken cancellationToken = default)
    {
      var queryParams = new System.Collections.Specialized.NameValueCollection();
      if (!string.IsNullOrEmpty(query))
        queryParams.Add("query", query);
      if (!string.IsNullOrEmpty(assetType))
        queryParams.Add("asset_type", assetType);
      if (!string.IsNullOrEmpty(folderId))
        queryParams.Add("folder_id", folderId);

      var queryString = string.Join("&",
          queryParams.AllKeys.SelectMany(key =>
              queryParams.GetValues(key)?.Select(value => $"{key}={Uri.EscapeDataString(value)}") ?? Array.Empty<string>()
          )
      );

      var url = "/api/library/assets";
      if (!string.IsNullOrEmpty(queryString))
        url += $"?{queryString}";

      return await _backend.SendRequestAsync<object, AssetSearchResponse>(url, null, HttpMethod.Get, cancellationToken);
    }

    /// <inheritdoc />
    public Task<LibraryFolder?> CreateFolderAsync(string name, string? parentId = null, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, LibraryFolder>(
          "/api/library/folders",
          new { name, parent_id = parentId },
          HttpMethod.Post,
          cancellationToken);

    /// <inheritdoc />
    public Task DeleteAssetAsync(string assetId, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, object>(
          $"/api/library/assets/{Uri.EscapeDataString(assetId)}",
          null,
          HttpMethod.Delete,
          cancellationToken);

    /// <inheritdoc />
    public Task<AssetTypesResponse?> GetAssetTypesAsync(CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, AssetTypesResponse>(
          "/api/library/types",
          null,
          HttpMethod.Get,
          cancellationToken);

    /// <inheritdoc />
    public Task<LibraryAsset?> UploadLibraryAssetAsync(string filePath, CancellationToken cancellationToken = default)
      => _backend.UploadFileWithProgressAsync<LibraryAsset>(
          "/api/library/assets/upload",
          filePath,
          "file",
          null,
          null,
          null,
          cancellationToken);
  }
}
