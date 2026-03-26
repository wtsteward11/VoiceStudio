using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for library API (/api/library).
  /// Use instead of IBackendClient for GetLibraryFolders, SearchAssets, CreateFolder, DeleteAsset, GetAssetTypes.
  /// </summary>
  public interface ILibraryClient
  {
    Task<LibraryFoldersResponse?> GetLibraryFoldersAsync(string? parentId = null, CancellationToken cancellationToken = default);
    Task<AssetSearchResponse?> SearchAssetsAsync(string? query = null, string? assetType = null, string? folderId = null, CancellationToken cancellationToken = default);
    Task<LibraryFolder?> CreateFolderAsync(string name, string? parentId = null, CancellationToken cancellationToken = default);
    Task DeleteAssetAsync(string assetId, CancellationToken cancellationToken = default);
    Task<AssetTypesResponse?> GetAssetTypesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads an audio file to the library via POST /api/library/assets/upload.
    /// Returns the created asset with metadata.upload_id for playback.
    /// </summary>
    Task<LibraryAsset?> UploadLibraryAssetAsync(string filePath, CancellationToken cancellationToken = default);
  }
}
