using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Collections.Generic;
using VoiceStudio.Core.Services;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Services.UndoableActions
{
  /// <summary>
  /// Undoable action for creating a library folder.
  /// </summary>
  public class CreateLibraryFolderAction : IUndoableAction
  {
    private readonly ObservableCollection<LibraryFolder> _folders;
    private readonly ILibraryClient _libraryClient;
    private readonly LibraryFolder _folder;
    private readonly Action<LibraryFolder>? _onUndo;
    private readonly Action<LibraryFolder>? _onRedo;

    public string ActionName => $"Create Folder '{_folder.Name ?? "Unnamed"}'";

    public CreateLibraryFolderAction(
        ObservableCollection<LibraryFolder> folders,
        ILibraryClient libraryClient,
        LibraryFolder folder,
        Action<LibraryFolder>? onUndo = null,
        Action<LibraryFolder>? onRedo = null)
    {
      _folders = folders ?? throw new ArgumentNullException(nameof(folders));
      _libraryClient = libraryClient ?? throw new ArgumentNullException(nameof(libraryClient));
      _folder = folder ?? throw new ArgumentNullException(nameof(folder));
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      // Remove the folder from the collection
      var folderToRemove = _folders.FirstOrDefault(f => f.Id == _folder.Id);
      if (folderToRemove != null)
      {
        _folders.Remove(folderToRemove);
        _onUndo?.Invoke(folderToRemove);
      }
    }

    public void Redo()
    {
      // Re-add the folder to the collection if not already present
      if (!_folders.Any(f => f.Id == _folder.Id))
      {
        _folders.Add(_folder);
        _onRedo?.Invoke(_folder);
      }
    }
  }

  /// <summary>
  /// Undoable action for deleting a library asset.
  /// </summary>
  public class DeleteLibraryAssetAction : IUndoableAction
  {
    private readonly ObservableCollection<LibraryAsset> _assets;
    private readonly ILibraryClient _libraryClient;
    private readonly LibraryAsset _asset;
    private readonly int _originalIndex;
    private readonly Action<LibraryAsset>? _onUndo;
    private readonly Action<LibraryAsset>? _onRedo;

    public string ActionName => $"Delete Asset '{_asset.Name ?? "Unnamed"}'";

    public DeleteLibraryAssetAction(
        ObservableCollection<LibraryAsset> assets,
        ILibraryClient libraryClient,
        LibraryAsset asset,
        Action<LibraryAsset>? onUndo = null,
        Action<LibraryAsset>? onRedo = null)
    {
      _assets = assets ?? throw new ArgumentNullException(nameof(assets));
      _libraryClient = libraryClient ?? throw new ArgumentNullException(nameof(libraryClient));
      _asset = asset ?? throw new ArgumentNullException(nameof(asset));
      _originalIndex = assets.IndexOf(asset);
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      // Re-add the asset at its original position
      if (!_assets.Any(a => a.Id == _asset.Id))
      {
        if (_originalIndex >= 0 && _originalIndex <= _assets.Count)
        {
          _assets.Insert(_originalIndex, _asset);
        }
        else
        {
          _assets.Add(_asset);
        }
        _onUndo?.Invoke(_asset);
      }
    }

    public void Redo()
    {
      // Remove the asset from the collection
      var assetToRemove = _assets.FirstOrDefault(a => a.Id == _asset.Id);
      if (assetToRemove != null)
      {
        _assets.Remove(assetToRemove);
        _onRedo?.Invoke(assetToRemove);
      }
    }
  }

  /// <summary>
  /// Undoable action for batch deleting multiple library assets.
  /// </summary>
  public class BatchDeleteLibraryAssetsAction : IUndoableAction
  {
    private readonly ObservableCollection<LibraryAsset> _assets;
    private readonly ILibraryClient _libraryClient;
    private readonly List<(LibraryAsset Asset, int Index)> _deletedAssets;
    private readonly Action<IEnumerable<LibraryAsset>>? _onUndo;
    private readonly Action<IEnumerable<LibraryAsset>>? _onRedo;

    public string ActionName => $"Delete {_deletedAssets.Count} Asset(s)";

    public BatchDeleteLibraryAssetsAction(
        ObservableCollection<LibraryAsset> assets,
        ILibraryClient libraryClient,
        IEnumerable<LibraryAsset> assetsToDelete,
        Action<IEnumerable<LibraryAsset>>? onUndo = null,
        Action<IEnumerable<LibraryAsset>>? onRedo = null)
    {
      _assets = assets ?? throw new ArgumentNullException(nameof(assets));
      _libraryClient = libraryClient ?? throw new ArgumentNullException(nameof(libraryClient));
      _deletedAssets = assetsToDelete?.Select(a => (a, assets.IndexOf(a))).ToList()
          ?? throw new ArgumentNullException(nameof(assetsToDelete));
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      // Re-add all assets at their original positions (in reverse order to maintain indices)
      var assetsToRestore = _deletedAssets.OrderByDescending(x => x.Index).ToList();
      foreach (var (asset, originalIndex) in assetsToRestore)
      {
        if (!_assets.Any(a => a.Id == asset.Id))
        {
          if (originalIndex >= 0 && originalIndex <= _assets.Count)
          {
            _assets.Insert(originalIndex, asset);
          }
          else
          {
            _assets.Add(asset);
          }
        }
      }
      _onUndo?.Invoke(_deletedAssets.Select(x => x.Asset));
    }

    public void Redo()
    {
      // Remove all assets from the collection
      foreach (var asset in _assets.Where(a => _deletedAssets.Any(d => d.Asset.Id == a.Id)).ToList())
      {
        _assets.Remove(asset);
      }
      _onRedo?.Invoke(_deletedAssets.Select(x => x.Asset));
    }
  }
}