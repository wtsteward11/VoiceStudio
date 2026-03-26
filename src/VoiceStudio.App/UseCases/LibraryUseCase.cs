using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Logging;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.UseCases
{
  /// <summary>
  /// Implementation of library use case.
  /// Encapsulates all library-related business logic.
  /// </summary>
  public class LibraryUseCase : ILibraryUseCase
  {
    private readonly IBackendClient _backendClient;
    private readonly IContextManager _contextManager;
    private readonly IProjectAudioClient _projectAudioClient;
    private readonly IErrorLoggingService? _logService;

    public LibraryUseCase(
      IBackendClient backendClient,
      IContextManager contextManager,
      IProjectAudioClient projectAudioClient,
      IErrorLoggingService? logService = null)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
      _contextManager = contextManager ?? throw new ArgumentNullException(nameof(contextManager));
      _projectAudioClient = projectAudioClient ?? throw new ArgumentNullException(nameof(projectAudioClient));
      _logService = logService;
    }

    public async Task<IReadOnlyList<LibraryFolder>> ListFoldersAsync(CancellationToken cancellationToken = default)
    {
      try
      {
        var response = await _backendClient.GetAsync<LibraryFoldersResponse>("/api/library/folders", cancellationToken);
        return response?.Folders ?? new List<LibraryFolder>();
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Failed to list library folders: {ex.Message}", "LibraryUseCase");
        return new List<LibraryFolder>();
      }
    }

    public async Task<LibraryFolderContents> GetFolderContentsAsync(string folderId, CancellationToken cancellationToken = default)
    {
      ArgumentException.ThrowIfNullOrEmpty(folderId);

      try
      {
        var response = await _backendClient.GetAsync<LibraryFolderContentsResponse>(
            $"/api/library/folders/{folderId}/contents", cancellationToken);
        
        return new LibraryFolderContents
        {
          Folder = response?.Folder ?? new LibraryFolder { Id = folderId },
          Subfolders = response?.Subfolders ?? new List<LibraryFolder>(),
          Items = response?.Items ?? new List<LibraryItem>()
        };
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Failed to get folder contents: {ex.Message}", "LibraryUseCase");
        return new LibraryFolderContents
        {
          Folder = new LibraryFolder { Id = folderId },
          Subfolders = new List<LibraryFolder>(),
          Items = new List<LibraryItem>()
        };
      }
    }

    public async Task<LibraryFolder> CreateFolderAsync(string name, string? parentId = null, CancellationToken cancellationToken = default)
    {
      ArgumentException.ThrowIfNullOrEmpty(name);

      var request = new CreateFolderRequest { Name = name, ParentId = parentId };
      var response = await _backendClient.PostAsync<CreateFolderRequest, LibraryFolder>(
          "/api/library/folders", request, cancellationToken);

      return response ?? throw new InvalidOperationException("Failed to create folder");
    }

    public async Task<LibraryFolder> RenameFolderAsync(string folderId, string newName, CancellationToken cancellationToken = default)
    {
      ArgumentException.ThrowIfNullOrEmpty(folderId);
      ArgumentException.ThrowIfNullOrEmpty(newName);

      var request = new RenameFolderRequest { Name = newName };
      var response = await _backendClient.PutAsync<RenameFolderRequest, LibraryFolder>(
          $"/api/library/folders/{folderId}", request, cancellationToken);

      return response ?? throw new InvalidOperationException("Failed to rename folder");
    }

    public async Task<bool> DeleteFolderAsync(string folderId, bool deleteContents = false, CancellationToken cancellationToken = default)
    {
      ArgumentException.ThrowIfNullOrEmpty(folderId);

      var request = new DeleteFolderRequest { FolderId = folderId, DeleteContents = deleteContents };
      var response = await _backendClient.PostAsync<DeleteFolderRequest, DeleteResponse>(
          "/api/library/folders/delete", request, cancellationToken);

      return response?.Success ?? false;
    }

    public async Task<IReadOnlyList<LibraryItem>> ImportFilesAsync(
        IEnumerable<string> filePaths, 
        string? targetFolderId = null, 
        CancellationToken cancellationToken = default)
    {
      var paths = filePaths.ToList();
      if (paths.Count == 0) return new List<LibraryItem>();

      var request = new ImportFilesRequest { FilePaths = paths, TargetFolderId = targetFolderId };
      var response = await _backendClient.PostAsync<ImportFilesRequest, ImportFilesResponse>(
          "/api/library/import", request, cancellationToken);

      var imported = response?.ImportedItems ?? new List<LibraryItem>();
      if (imported.Count > 0)
      {
        await ImportToProjectPersistence
          .TrySaveAfterBatchLibraryImportAsync(
            _projectAudioClient,
            _logService,
            _contextManager.ActiveProjectId,
            paths,
            imported,
            cancellationToken)
          .ConfigureAwait(false);
      }

      return imported;
    }

    public async Task<IReadOnlyList<LibraryItem>> SearchAsync(
        string query, 
        LibrarySearchOptions? options = null, 
        CancellationToken cancellationToken = default)
    {
      var endpoint = $"/api/library/search?q={Uri.EscapeDataString(query)}";
      
      if (options != null)
      {
        if (!string.IsNullOrEmpty(options.FolderId))
          endpoint += $"&folderId={options.FolderId}";
        if (!string.IsNullOrEmpty(options.FileType))
          endpoint += $"&fileType={options.FileType}";
        if (options.MaxResults.HasValue)
          endpoint += $"&maxResults={options.MaxResults}";
        if (!string.IsNullOrEmpty(options.SortBy))
          endpoint += $"&sortBy={options.SortBy}&desc={options.Descending}";
      }

      var response = await _backendClient.GetAsync<LibrarySearchResponse>(endpoint, cancellationToken);
      return response?.Results ?? new List<LibraryItem>();
    }

    public async Task<LibraryItemMetadata> GetItemMetadataAsync(string itemId, CancellationToken cancellationToken = default)
    {
      ArgumentException.ThrowIfNullOrEmpty(itemId);

      var response = await _backendClient.GetAsync<LibraryItemMetadata>(
          $"/api/library/items/{itemId}/metadata", cancellationToken);

      return response ?? new LibraryItemMetadata();
    }

    public async Task<LibraryItem> UpdateItemMetadataAsync(string itemId, LibraryItemMetadata metadata, CancellationToken cancellationToken = default)
    {
      ArgumentException.ThrowIfNullOrEmpty(itemId);

      var response = await _backendClient.PutAsync<LibraryItemMetadata, LibraryItem>(
          $"/api/library/items/{itemId}/metadata", metadata, cancellationToken);

      return response ?? throw new InvalidOperationException("Failed to update item metadata");
    }

    public async Task<int> DeleteItemsAsync(IEnumerable<string> itemIds, CancellationToken cancellationToken = default)
    {
      var ids = itemIds.ToList();
      if (ids.Count == 0) return 0;

      var request = new DeleteItemsRequest { ItemIds = ids };
      var response = await _backendClient.PostAsync<DeleteItemsRequest, DeleteItemsResponse>(
          "/api/library/items/delete", request, cancellationToken);

      return response?.DeletedCount ?? 0;
    }

    public async Task<int> MoveItemsAsync(IEnumerable<string> itemIds, string targetFolderId, CancellationToken cancellationToken = default)
    {
      var ids = itemIds.ToList();
      if (ids.Count == 0) return 0;

      var request = new MoveItemsRequest { ItemIds = ids, TargetFolderId = targetFolderId };
      var response = await _backendClient.PostAsync<MoveItemsRequest, MoveItemsResponse>(
          "/api/library/items/move", request, cancellationToken);

      return response?.MovedCount ?? 0;
    }

    public async Task<string> ExportItemsAsync(IEnumerable<string> itemIds, string exportPath, CancellationToken cancellationToken = default)
    {
      var ids = itemIds.ToList();
      if (ids.Count == 0) return exportPath;

      var request = new ExportItemsRequest { ItemIds = ids, ExportPath = exportPath };
      var response = await _backendClient.PostAsync<ExportItemsRequest, ExportItemsResponse>(
          "/api/library/items/export", request, cancellationToken);

      return response?.ExportPath ?? exportPath;
    }

    // Request/Response DTOs (public for testability)
    public class LibraryFoldersResponse { public List<LibraryFolder>? Folders { get; set; } }
    public class LibraryFolderContentsResponse
    {
      public LibraryFolder? Folder { get; set; }
      public List<LibraryFolder>? Subfolders { get; set; }
      public List<LibraryItem>? Items { get; set; }
    }
    public class CreateFolderRequest { public string Name { get; set; } = ""; public string? ParentId { get; set; } }
    public class RenameFolderRequest { public string Name { get; set; } = ""; }
    public class ImportFilesRequest { public List<string> FilePaths { get; set; } = new(); public string? TargetFolderId { get; set; } }
    public class ImportFilesResponse { public List<LibraryItem>? ImportedItems { get; set; } }
    public class LibrarySearchResponse { public List<LibraryItem>? Results { get; set; } }
    public class DeleteItemsRequest { public List<string> ItemIds { get; set; } = new(); }
    public class DeleteItemsResponse { public int DeletedCount { get; set; } }
    public class MoveItemsRequest { public List<string> ItemIds { get; set; } = new(); public string TargetFolderId { get; set; } = ""; }
    public class MoveItemsResponse { public int MovedCount { get; set; } }
    public class ExportItemsRequest { public List<string> ItemIds { get; set; } = new(); public string ExportPath { get; set; } = ""; }
    public class ExportItemsResponse { public string? ExportPath { get; set; } }
    public class DeleteFolderRequest { public string FolderId { get; set; } = ""; public bool DeleteContents { get; set; } }
    public class DeleteResponse { public bool Success { get; set; } }
  }
}
