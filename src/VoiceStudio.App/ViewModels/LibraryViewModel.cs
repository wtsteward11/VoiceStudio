using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.Logging;
using VoiceStudio.Core.Events;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the LibraryView panel - Asset library browser.
  /// Backend-Frontend Integration Plan - Phase 2: Implements state persistence.
  /// Panel Workflow Integration: Uses WorkflowCoordinatorService for multi-panel workflows.
  /// </summary>
  public partial class LibraryViewModel : BaseViewModel, IPanelView, IPanelStatePersistable, IPanelLifecycle
  {
    private readonly ILibraryClient _libraryClient;
    private readonly IDialogService _dialogService;
    private readonly IAudioPlayerService? _audioPlayer;
    private readonly ToastNotificationService? _toastNotificationService;
    private readonly UndoRedoService? _undoRedoService;
    private readonly IEventAggregator? _eventAggregator;
    private readonly IContextManager? _contextManager;
    private readonly IWorkflowCoordinatorService? _workflowCoordinator;
    private ISubscriptionToken? _profileSelectedToken;
    private ISubscriptionToken? _assetAddedToken;
    private ISubscriptionToken? _profileCreatedToken;
    private ISubscriptionToken? _synthesisCompletedToken;

    public string PanelId => "library";
    public string DisplayName => ResourceHelper.GetString("Panel.Library.DisplayName", "Library");
    public PanelRegion Region => PanelRegion.Left;

    [ObservableProperty]
    private ObservableCollection<LibraryFolder> folders = new();

    [ObservableProperty]
    private LibraryFolder? selectedFolder;

    [ObservableProperty]
    private ObservableCollection<LibraryAsset> assets = new();

    [ObservableProperty]
    private LibraryAsset? selectedAsset;

    [ObservableProperty]
    private string? searchQuery;

    [ObservableProperty]
    private string? selectedAssetType;

    [ObservableProperty]
    private ObservableCollection<string> availableAssetTypes = new();

    [ObservableProperty]
    private int totalAssets;

    [ObservableProperty]
    private bool showFolders = true;

    // Multi-select support
    private readonly MultiSelectService _multiSelectService;
    private MultiSelectState? _multiSelectState;

    [ObservableProperty]
    private int selectedAssetCount;

    [ObservableProperty]
    private bool hasMultipleAssetSelection;

    public bool IsAssetSelected(string assetId) => _multiSelectState?.SelectedIds.Contains(assetId) ?? false;

    private readonly Action? _triggerImport;

    private CancellationTokenSource? _searchDebounceCts;
    private const int SearchDebounceMs = 300;

    private readonly CancellationTokenSource _disposalCts = new();
    private CancellationTokenSource? _loadAssetsCts;

    public LibraryViewModel(IViewModelContext context, ILibraryClient libraryClient, IDialogService dialogService, Action? triggerImport = null)
        : base(context)
    {
      _libraryClient = libraryClient ?? throw new ArgumentNullException(nameof(libraryClient));
      _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
      _triggerImport = triggerImport;
      var multiSelectService = AppServices.TryGetMultiSelectService();
      _multiSelectService = multiSelectService ?? throw new InvalidOperationException("MultiSelectService is required but not registered");
      _multiSelectState = _multiSelectService.GetState(PanelId);

      // Get optional services using helper (reduces code duplication)
      _audioPlayer = AppServices.GetService<IAudioPlayerService>();
      _toastNotificationService = ServiceInitializationHelper.TryGetService(() => AppServices.TryGetToastNotificationService());
      _undoRedoService = ServiceInitializationHelper.TryGetService(() => AppServices.TryGetUndoRedoService());
      _workflowCoordinator = ServiceInitializationHelper.TryGetService(() => AppServices.TryGetWorkflowCoordinatorService());

      LoadFoldersCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadFolders");
        await LoadFoldersAsync(ct);
      });
      LoadAssetsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadAssets");
        await LoadAssetsAsync(ct);
      });
      SearchAssetsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("SearchAssets");
        await SearchAssetsAsync(ct);
      });
      CreateFolderCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CreateFolder");
        await CreateFolderAsync(ct);
      });
      DeleteAssetCommand = new EnhancedAsyncRelayCommand<LibraryAsset>(async (asset, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteAsset");
        await DeleteAssetAsync(asset, ct);
      });
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      });
      ImportFromEmptyStateCommand = new RelayCommand(
        () => _triggerImport?.Invoke(),
        () => _triggerImport != null && !IsLoading);
      RetryOnErrorCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        ErrorMessage = null;
        await LoadAssetsAsync(ct);
      }, () => !string.IsNullOrEmpty(ErrorMessage) && !IsLoading);
      LoadAssetTypesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadAssetTypes");
        await LoadAssetTypesAsync(ct);
      });

      // Multi-select commands
      SelectAllAssetsCommand = new RelayCommand(SelectAllAssets, () => Assets?.Count > 0);
      ClearAssetSelectionCommand = new RelayCommand(ClearAssetSelection);
      DeleteSelectedAssetsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteSelectedAssets");
        await DeleteSelectedAssetsAsync(ct);
      }, () => SelectedAssetCount > 0);

      // Context menu commands (Audit remediation C.2: Clone Reference, Use Voice Now)
      UseAsCloneReferenceCommand = new RelayCommand<LibraryAsset>(UseAsCloneReference, CanUseAsCloneReference);
      UseSynthesisVoiceCommand = new RelayCommand<LibraryAsset>(UseSynthesisVoice, CanUseSynthesisVoice);
      PlayAssetCommand = new RelayCommand<LibraryAsset>(PlayAsset, CanPlayAsset);

      // Subscribe to selection changes
      _multiSelectService.SelectionChanged += (_, e) =>
      {
        if (e.PanelId == PanelId)
        {
          UpdateAssetSelectionProperties();
          OnPropertyChanged(nameof(SelectedAssetCount));
          OnPropertyChanged(nameof(HasMultipleAssetSelection));
        }
      };

      // Initialize EventAggregator and ContextManager for cross-panel coordination
      _eventAggregator = AppServices.TryGetEventAggregator();
      _contextManager = AppServices.TryGetContextManager();
      if (_eventAggregator != null)
      {
        _profileSelectedToken = _eventAggregator.Subscribe<ProfileSelectedEvent>(OnProfileSelected);
        _assetAddedToken = _eventAggregator.Subscribe<AssetAddedEvent>(OnAssetAdded);
        _profileCreatedToken = _eventAggregator.Subscribe<ProfileCreatedEvent>(OnProfileCreatedRefresh);
        _synthesisCompletedToken = _eventAggregator.Subscribe<SynthesisCompletedEvent>(OnSynthesisCompleted);
      }

      // Update ShowEmptyState when Assets, IsLoading, or ErrorMessage changes
      Assets.CollectionChanged += (_, __) => OnPropertyChanged(nameof(ShowEmptyState));
      PropertyChanged += (_, e) =>
      {
        if (e.PropertyName is nameof(IsLoading) or nameof(ErrorMessage))
        {
          OnPropertyChanged(nameof(ShowEmptyState));
          ImportFromEmptyStateCommand.NotifyCanExecuteChanged();
          RetryOnErrorCommand.NotifyCanExecuteChanged();
        }
      };
    }

    /// <summary>
    /// Handles profile selection events from other panels.
    /// Backend-Frontend Integration Plan - Phase 4: Cross-panel synchronization.
    /// </summary>
    private void OnProfileSelected(ProfileSelectedEvent e)
    {
      // When a profile is selected in ProfilesPanel, filter library to show profile-related assets
      System.Diagnostics.Debug.WriteLine($"LibraryViewModel: Profile selected - {e.ProfileId} ({e.ProfileName})");
      // Future enhancement: could filter assets by profile or highlight related items
    }

    /// <summary>
    /// Auto-refresh library when an audio asset is added (import, drag-drop, recording).
    /// Audit remediation X-1: Imported audio now automatically visible in Library.
    /// </summary>
    private void OnAssetAdded(AssetAddedEvent e)
    {
      System.Diagnostics.Debug.WriteLine(
          $"LibraryViewModel: Asset added - {e.AssetId} ({e.AssetType}) from {e.SourcePanelId}");
      _ = LoadAssetsAsync(_disposalCts.Token);
    }

    /// <summary>
    /// Auto-refresh library when a voice profile is created (clone wizard).
    /// Audit remediation X-2: Cloned voice output now visible in Library.
    /// </summary>
    private void OnProfileCreatedRefresh(ProfileCreatedEvent e)
    {
      System.Diagnostics.Debug.WriteLine(
          $"LibraryViewModel: Profile created - {e.ProfileId} ({e.ProfileName}) from {e.SourcePanelId}");
      _ = LoadAssetsAsync(_disposalCts.Token);
    }

    /// <summary>
    /// Auto-refresh library when synthesis completes (Feature synthesis path).
    /// GAP-W1: SynthesisCompletedEvent from SynthesisViewModel; AssetAddedEvent covers VoiceSynthesis path.
    /// </summary>
    private void OnSynthesisCompleted(SynthesisCompletedEvent e)
    {
      System.Diagnostics.Debug.WriteLine(
          $"LibraryViewModel: Synthesis completed - {e.AudioId} from {e.SourcePanelId}");
      _ = LoadAssetsAsync(_disposalCts.Token);
    }

    public IRelayCommand ImportFromEmptyStateCommand { get; }
    public IAsyncRelayCommand RetryOnErrorCommand { get; }
    public IAsyncRelayCommand LoadFoldersCommand { get; }
    public IAsyncRelayCommand LoadAssetsCommand { get; }
    public IAsyncRelayCommand SearchAssetsCommand { get; }
    public IAsyncRelayCommand CreateFolderCommand { get; }
    public IAsyncRelayCommand<LibraryAsset> DeleteAssetCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand LoadAssetTypesCommand { get; }

    // Multi-select commands
    public IRelayCommand SelectAllAssetsCommand { get; }
    public IRelayCommand ClearAssetSelectionCommand { get; }
    public IAsyncRelayCommand DeleteSelectedAssetsCommand { get; }

    /// <summary>True when assets list is empty, not loading, and no error (show empty state CTA).</summary>
    public bool ShowEmptyState => (Assets?.Count ?? 0) == 0 && !IsLoading && string.IsNullOrEmpty(ErrorMessage);

    public string EmptyStateTitle => ResourceHelper.GetString("LibraryView_EmptyState.Title", "No Assets");
    public string EmptyStateMessage => ResourceHelper.GetString("LibraryView_EmptyState.Message", "Import audio files or drag-and-drop to add to library.");
    public string EmptyStateActionText => ResourceHelper.GetString("LibraryView_EmptyState.ActionText", "Import");

    // Context menu commands (Audit remediation C.2)
    public IRelayCommand<LibraryAsset> UseAsCloneReferenceCommand { get; }
    public IRelayCommand<LibraryAsset> UseSynthesisVoiceCommand { get; }
    public IRelayCommand<LibraryAsset> PlayAssetCommand { get; }

    /// <inheritdoc />
    public Task OnActivatedAsync(CancellationToken cancellationToken = default)
    {
      // Load initial data when panel becomes active (ADR-047: no constructor fire-and-forget)
      _ = LoadAssetTypesAsync(cancellationToken);
      _ = LoadFoldersAsync(cancellationToken);
      _ = LoadAssetsAsync(cancellationToken);
      return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <summary>Unsubscribe from EventAggregator to prevent memory leaks (GAP-W3).</summary>
    public Task OnDeactivatedAsync(CancellationToken cancellationToken = default)
    {
      _profileSelectedToken?.Dispose();
      _profileSelectedToken = null;
      _assetAddedToken?.Dispose();
      _assetAddedToken = null;
      _profileCreatedToken?.Dispose();
      _profileCreatedToken = null;
      _synthesisCompletedToken?.Dispose();
      _synthesisCompletedToken = null;
      return Task.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
      if (disposing)
      {
        _disposalCts.Cancel();
        _disposalCts.Dispose();
        _loadAssetsCts?.Cancel();
        _loadAssetsCts?.Dispose();
        _loadAssetsCts = null;
        _searchDebounceCts?.Cancel();
        _searchDebounceCts?.Dispose();
        _searchDebounceCts = null;
      }
      base.Dispose(disposing);
    }

    private async Task LoadFoldersAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var parentId = SelectedFolder?.Id;
        var response = await _libraryClient.GetLibraryFoldersAsync(parentId, cancellationToken);

        Folders.Clear();
        if (response?.Folders != null)
        {
          foreach (var folder in response.Folders)
          {
            Folders.Add(folder);
          }
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("Library.LoadFoldersFailed", ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadAssetsAsync(CancellationToken cancellationToken = default)
    {
      await SearchAssetsAsync(cancellationToken);
    }

    private async Task SearchAssetsAsync(CancellationToken cancellationToken = default)
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var response = await _libraryClient.SearchAssetsAsync(
            SearchQuery,
            SelectedAssetType,
            SelectedFolder?.Id,
            cancellationToken);

        Assets.Clear();
        if (response?.Assets != null)
        {
          foreach (var asset in response.Assets)
          {
            Assets.Add(asset);
          }
        }

        TotalAssets = response?.Total ?? 0;
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("Library.SearchAssetsFailed", ex.Message);
        await HandleErrorAsync(ex, "SearchAssets");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task CreateFolderAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        // Show dialog to get folder name
        var folderName = await ShowFolderNameDialogAsync();
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(folderName))
        {
          // User cancelled
          return;
        }

        var parentId = SelectedFolder?.Id;

        var createdFolder = await _libraryClient.CreateFolderAsync(folderName, parentId, cancellationToken);

        if (createdFolder == null)
        {
          throw new InvalidOperationException("Backend did not return the created folder.");
        }

        await LoadFoldersAsync(cancellationToken);

        // Find the created folder in the collection (after reload)
        var folderInCollection = Folders.FirstOrDefault(f => f.Id == createdFolder.Id ||
            (f.Name == folderName && f.ParentId == parentId));

        // Register undo action if folder was found
        if (folderInCollection != null && _undoRedoService != null)
        {
          var action = new CreateLibraryFolderAction(
              Folders,
              _libraryClient,
              folderInCollection);
          _undoRedoService.RegisterAction(action);
        }

        StatusMessage = ResourceHelper.GetString("Library.FolderCreated", "Folder created");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.FormatString("Library.FolderCreatedSuccess", folderName),
            ResourceHelper.GetString("Toast.Title.FolderCreated", "Folder Created"));
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        var errorMsg = ResourceHelper.FormatString("Library.CreateFolderFailed", ex.Message);
        ErrorMessage = errorMsg;
        _toastNotificationService?.ShowError(
            errorMsg,
            ResourceHelper.GetString("Toast.Title.CreateFolderFailed", "Create Folder Failed"));
        await HandleErrorAsync(ex, "CreateFolder");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task DeleteAssetAsync(LibraryAsset? asset, CancellationToken cancellationToken)
    {
      if (asset == null)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        // Capture asset before deletion for undo
        var assetToDelete = asset;
        var wasSelected = SelectedAsset?.Id == asset.Id;

        await _libraryClient.DeleteAssetAsync(asset.Id, cancellationToken);

        // Register undo action before reload
        if (_undoRedoService != null)
        {
          var action = new DeleteLibraryAssetAction(
              Assets,
              _libraryClient,
              assetToDelete,
              onUndo: (a) => SelectedAsset = a,
              onRedo: (a) =>
              {
                if (SelectedAsset?.Id == a.Id)
                {
                  SelectedAsset = null;
                }
              });
          _undoRedoService.RegisterAction(action);
        }

        await LoadAssetsAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("Library.AssetDeleted", "Asset deleted");
        var assetName = asset.Name ?? ResourceHelper.GetString("Library.UnnamedAsset", "Unnamed Asset");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.FormatString("Library.AssetDeletedSuccess", assetName),
            ResourceHelper.GetString("Toast.Title.AssetDeleted", "Asset Deleted"));
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        var errorMsg = ResourceHelper.FormatString("Library.DeleteAssetFailed", ex.Message);
        ErrorMessage = errorMsg;
        _toastNotificationService?.ShowError(
            errorMsg,
            ResourceHelper.GetString("Toast.Title.DeleteAssetFailed", "Delete Asset Failed"));
        await HandleErrorAsync(ex, "DeleteAsset");
      }
      finally
      {
        IsLoading = false;
      }
    }

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
      await LoadFoldersAsync(cancellationToken);
      await LoadAssetsAsync(cancellationToken);
      StatusMessage = ResourceHelper.GetString("Library.Refreshed", "Library refreshed");
    }

    private async Task LoadAssetTypesAsync(CancellationToken cancellationToken)
    {
      try
      {
        var response = await _libraryClient.GetAssetTypesAsync(cancellationToken);

        AvailableAssetTypes.Clear();
        if (response?.Types != null)
        {
          foreach (var type in response.Types)
          {
            AvailableAssetTypes.Add(type.Id);
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("Library.LoadAssetTypesFailed", ex.Message);
        await HandleErrorAsync(ex, "LoadAssetTypes");
      }
    }

    partial void OnSelectedFolderChanged(LibraryFolder? value)
    {
      _loadAssetsCts?.Cancel();
      _loadAssetsCts?.Dispose();
      _loadAssetsCts = CancellationTokenSource.CreateLinkedTokenSource(_disposalCts.Token);
      _ = LoadAssetsAsync(_loadAssetsCts.Token);
    }

    partial void OnSearchQueryChanged(string? value)
    {
      _searchDebounceCts?.Cancel();
      _searchDebounceCts = new CancellationTokenSource();
      var cts = _searchDebounceCts;
      _ = Task.Run(async () =>
      {
        try
        {
          await Task.Delay(SearchDebounceMs, cts.Token);
          Dispatcher.TryEnqueue(() => _ = SearchAssetsAsync(cts.Token));
        }
        catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "LibraryViewModel.OnSearchQueryChanged");
      }
      });
    }

    partial void OnSelectedAssetTypeChanged(string? value)
    {
      _loadAssetsCts?.Cancel();
      _loadAssetsCts?.Dispose();
      _loadAssetsCts = CancellationTokenSource.CreateLinkedTokenSource(_disposalCts.Token);
      _ = LoadAssetsAsync(_loadAssetsCts.Token);
    }

    /// <summary>
    /// Called when the selected asset changes.
    /// Backend-Frontend Integration Plan - Phase 4: Publishes asset selection event.
    /// Panel Architecture Phase 2: Uses ContextManager for centralized state.
    /// </summary>
    partial void OnSelectedAssetChanged(LibraryAsset? value)
    {
      // Use ContextManager for centralized asset state (preferred path)
      // Falls back to direct event publishing if context manager unavailable
      if (value != null)
      {
        if (_contextManager != null)
        {
          _contextManager.SetActiveAsset(value.Id, value.Type ?? "unknown", value.Name, InteractionIntent.Navigation);
        }
        else
        {
          _eventAggregator?.Publish(new AssetSelectedEvent(PanelId, value.Id, value.Type ?? "unknown", value.Name));
        }
      }
      else if (_contextManager != null)
      {
        // Clear the active asset when deselected
        _contextManager.SetActiveAsset(null, null, null);
      }
    }

    private async Task<string?> ShowFolderNameDialogAsync()
    {
      var name = await _dialogService.ShowInputAsync(
          ResourceHelper.GetString("Library.CreateNewFolder", "Create New Folder"),
          ResourceHelper.GetString("Library.EnterFolderName", "Enter folder name"),
          ResourceHelper.GetString("Library.NewFolder", "New Folder"),
          ResourceHelper.GetString("Library.EnterFolderName", "Enter folder name"));

      if (string.IsNullOrWhiteSpace(name))
        return null;

      name = name.Trim();
      var invalidChars = System.IO.Path.GetInvalidFileNameChars();
      if (name.IndexOfAny(invalidChars) >= 0)
      {
        ErrorMessage = ResourceHelper.GetString("Library.FolderNameInvalidChars", "Folder name contains invalid characters");
        return null;
      }

      return name;
    }

    #region Context Menu Commands (Audit remediation C.2)

    /// <summary>
    /// Use the selected audio asset as a reference for voice cloning.
    /// Uses WorkflowCoordinatorService for multi-panel workflow orchestration.
    /// </summary>
    private async void UseAsCloneReference(LibraryAsset? asset)
    {
      if (asset == null) return;

      System.Diagnostics.Debug.WriteLine($"LibraryViewModel: UseAsCloneReference - {asset.Id} ({asset.Name})");

      // Use workflow coordinator for orchestrated multi-panel workflow
      if (_workflowCoordinator != null)
      {
        var context = await _workflowCoordinator.StartCloneFromLibraryAsync(
          asset.Id,
          asset.Path,
          asset.Name,
          useQuickClone: true);

        if (context.Status == WorkflowStatus.Completed)
        {
          _toastNotificationService?.ShowInfo(
              $"'{asset.Name}' set as clone reference. Quick Clone panel ready.",
              "Clone Reference Set");
        }
        else
        {
          _toastNotificationService?.ShowWarning(
              $"Failed to start clone workflow: {context.ErrorMessage}",
              "Workflow Error");
        }
      }
      else
      {
        // Fallback: Publish event directly
        _eventAggregator?.Publish(new CloneReferenceSelectedEvent(PanelId, asset.Id, asset.Path, asset.Name));
        _toastNotificationService?.ShowInfo(
            $"'{asset.Name}' set as clone reference. Open Quick Clone or Cloning Wizard to continue.",
            "Clone Reference Set");
      }
    }

    private bool CanUseAsCloneReference(LibraryAsset? asset)
    {
      // Only audio assets can be used as clone references
      return asset != null && IsAudioAsset(asset);
    }

    /// <summary>
    /// Use the selected voice profile for immediate synthesis.
    /// Uses WorkflowCoordinatorService for multi-panel workflow orchestration.
    /// </summary>
    private async void UseSynthesisVoice(LibraryAsset? asset)
    {
      if (asset == null) return;

      System.Diagnostics.Debug.WriteLine($"LibraryViewModel: UseSynthesisVoice - {asset.Id} ({asset.Name})");

      // Use workflow coordinator for orchestrated multi-panel workflow
      if (_workflowCoordinator != null)
      {
        var context = await _workflowCoordinator.StartSynthesizeWithVoiceAsync(
          asset.Id,
          asset.Name);

        if (context.Status == WorkflowStatus.Completed)
        {
          _toastNotificationService?.ShowInfo(
              $"'{asset.Name}' selected for synthesis. Synthesis panel ready.",
              "Voice Selected");
        }
        else
        {
          _toastNotificationService?.ShowWarning(
              $"Failed to start synthesis workflow: {context.ErrorMessage}",
              "Workflow Error");
        }
      }
      else
      {
        // Fallback: Publish event directly
        _eventAggregator?.Publish(new VoiceProfileSelectedEvent(PanelId, asset.Id, asset.Name));
        _toastNotificationService?.ShowInfo(
            $"'{asset.Name}' selected for synthesis. Open Synthesis panel to use.",
            "Voice Selected");
      }
    }

    private bool CanUseSynthesisVoice(LibraryAsset? asset)
    {
      // Only voice profile assets can be used for synthesis
      return asset != null && IsVoiceProfileAsset(asset);
    }

    /// <summary>
    /// Play the selected audio asset.
    /// Prefers direct IAudioPlayerService call for immediate playback; falls back to event for cross-panel workflows.
    /// </summary>
    private async void PlayAsset(LibraryAsset? asset)
    {
      if (asset == null) return;

      System.Diagnostics.Debug.WriteLine($"LibraryViewModel: PlayAsset - {asset.Id} ({asset.Name})");

      // Primary path: direct playback when service available (no event subscription dependency)
      if (_audioPlayer != null)
      {
        try
        {
          if (!string.IsNullOrEmpty(asset.Path) && System.IO.File.Exists(asset.Path))
          {
            await _audioPlayer.PlayFileAsync(asset.Path, () =>
              _toastNotificationService?.ShowToast(ToastType.Info, "Playback Complete", $"Finished playing {asset.Name}"));
            _toastNotificationService?.ShowToast(ToastType.Success, "Playing", $"Now playing: {asset.Name}");
            return;
          }
          if (!string.IsNullOrEmpty(asset.Id))
          {
            var baseUrl = AppServices.GetService<BackendClientConfig>()?.BaseUrl?.TrimEnd('/')
                ?? "http://localhost:8000";
            await _audioPlayer.PlayBackendAudioIdAsync(asset.Id, baseUrl, () =>
              _toastNotificationService?.ShowToast(ToastType.Info, "Playback Complete", $"Finished playing {asset.Name}"));
            _toastNotificationService?.ShowToast(ToastType.Success, "Playing", $"Now playing: {asset.Name}");
            return;
          }
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine($"[LibraryViewModel] Direct playback failed: {ex.Message}");
          _toastNotificationService?.ShowToast(ToastType.Error, "Playback Error", ex.Message);
          return;
        }
      }

      // Fallback: event path for cross-panel workflows or when service unavailable
      if (_workflowCoordinator != null)
      {
        await _workflowCoordinator.StartPlayFromLibraryAsync(asset.Id, asset.Path ?? string.Empty, asset.Name);
      }
      else
      {
        _eventAggregator?.Publish(new PlaybackRequestedEvent(PanelId, asset.Id, asset.Path ?? string.Empty, asset.Name));
      }
    }

    private bool CanPlayAsset(LibraryAsset? asset)
    {
      // Only audio/voice assets can be played
      return asset != null && (IsAudioAsset(asset) || IsVoiceProfileAsset(asset));
    }

    private static bool IsAudioAsset(LibraryAsset asset)
    {
      var audioTypes = new[] { "audio", "wav", "mp3", "flac", "ogg", "m4a", "recording" };
      return audioTypes.Contains(asset.Type?.ToLowerInvariant() ?? "") ||
             asset.Path?.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) == true ||
             asset.Path?.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) == true ||
             asset.Path?.EndsWith(".flac", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsVoiceProfileAsset(LibraryAsset asset)
    {
      var voiceTypes = new[] { "voice", "voice_profile", "profile", "clone", "xtts", "rvc" };
      return voiceTypes.Contains(asset.Type?.ToLowerInvariant() ?? "");
    }

    #endregion

    // Multi-select methods
    public void ToggleAssetSelection(string assetId, bool isCtrlPressed, bool isShiftPressed)
    {
      if (_multiSelectState == null)
        return;

      if (isShiftPressed && !string.IsNullOrEmpty(_multiSelectState.RangeAnchorId))
      {
        // Range selection
        var allAssetIds = Assets.Select(a => a.Id).ToList();
        _multiSelectState.SetRange(_multiSelectState.RangeAnchorId, assetId, allAssetIds);
      }
      else if (isCtrlPressed)
      {
        // Toggle selection
        _multiSelectState.Toggle(assetId);
      }
      else
      {
        // Single selection (clear others)
        _multiSelectState.SetSingle(assetId);
      }

      UpdateAssetSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
    }

    private void SelectAllAssets()
    {
      if (_multiSelectState == null)
        return;

      _multiSelectState.Clear();
      foreach (var asset in Assets)
      {
        _multiSelectState.Add(asset.Id);
      }
      if (Assets.Count > 0)
      {
        _multiSelectState.RangeAnchorId = Assets[0].Id;
      }

      UpdateAssetSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
      SelectAllAssetsCommand.NotifyCanExecuteChanged();
    }

    private void ClearAssetSelection()
    {
      if (_multiSelectState == null)
        return;

      _multiSelectState.Clear();
      UpdateAssetSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
      DeleteSelectedAssetsCommand.NotifyCanExecuteChanged();
    }

    private async Task DeleteSelectedAssetsAsync(CancellationToken cancellationToken)
    {
      if (_multiSelectState == null || _multiSelectState.SelectedIds.Count == 0)
        return;

      var selectedIds = new List<string>(_multiSelectState.SelectedIds);

      // Show confirmation dialog (Panel Hardening: IDialogService per PANEL_HARDENING_PATTERN)
      var confirmed = await _dialogService.ShowConfirmationAsync(
          ResourceHelper.GetString("Library.DeleteAssets", "Delete assets?"),
          string.Format(
              ResourceHelper.GetString("Library.DeleteAssetsConfirm", "Are you sure you want to delete '{0}'? This action cannot be undone."),
              $"{selectedIds.Count} asset(s)"),
          confirmText: ResourceHelper.GetString("Library.Delete", "Delete"),
          cancelText: ResourceHelper.GetString("Library.Cancel", "Cancel"));

      if (!confirmed)
        return;

      cancellationToken.ThrowIfCancellationRequested();

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        // Capture assets before deletion for undo
        var assetsToDelete = Assets.Where(a => selectedIds.Contains(a.Id)).ToList();
        var wasAnySelected = assetsToDelete.Any(a => SelectedAsset?.Id == a.Id);

        foreach (var assetId in selectedIds)
        {
          cancellationToken.ThrowIfCancellationRequested();

          try
          {
            await _libraryClient.DeleteAssetAsync(assetId, cancellationToken);
          }
          catch (OperationCanceledException)
          {
            throw; // Re-throw cancellation
          }
          catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "LibraryViewModel.DeleteSelectedAssetsAsync");
      }
        }

        // Register batch undo action before reload
        if (assetsToDelete.Count > 0 && _undoRedoService != null)
        {
          var action = new BatchDeleteLibraryAssetsAction(
              Assets,
              _libraryClient,
              assetsToDelete,
              onUndo: (assets) =>
              {
                if (wasAnySelected && assets.Any())
                {
                  SelectedAsset = assets.First();
                }
              },
              onRedo: (assets) =>
              {
                if (SelectedAsset != null && assets.Any(a => a.Id == SelectedAsset.Id))
                {
                  SelectedAsset = null;
                }
              });
          _undoRedoService.RegisterAction(action);
        }

        // Reload assets
        await LoadAssetsAsync(cancellationToken);

        // Clear selection after deletion
        ClearAssetSelection();
        StatusMessage = $"{selectedIds.Count} asset(s) deleted";

        // Count successful deletions by checking if assets are still in the list
        var remainingAssets = Assets.Select(a => a.Id).ToList();
        var deletedCount = selectedIds.Count(id => !remainingAssets.Contains(id));

        // Show success toast
        if (deletedCount > 0)
        {
          _toastNotificationService?.ShowSuccess($"Deleted {deletedCount} asset(s)", "Assets Deleted");
        }
        if (deletedCount < selectedIds.Count)
        {
          _toastNotificationService?.ShowWarning($"Some assets could not be deleted ({deletedCount}/{selectedIds.Count} succeeded)", "Partial Delete");
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        var errorMsg = ResourceHelper.FormatString("Library.DeleteAssetsFailed", ex.Message);
        ErrorMessage = errorMsg;
        _toastNotificationService?.ShowError(
            errorMsg,
            ResourceHelper.GetString("Toast.Title.BatchDeleteFailed", "Batch Delete Failed"));
        await HandleErrorAsync(ex, "DeleteSelectedAssets");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private void UpdateAssetSelectionProperties()
    {
      if (_multiSelectState == null)
      {
        SelectedAssetCount = 0;
        HasMultipleAssetSelection = false;
      }
      else
      {
        SelectedAssetCount = _multiSelectState.Count;
        HasMultipleAssetSelection = _multiSelectState.IsMultipleSelection;
      }

      OnPropertyChanged(nameof(SelectedAssetCount));
      OnPropertyChanged(nameof(HasMultipleAssetSelection));
      DeleteSelectedAssetsCommand.NotifyCanExecuteChanged();
    }

    #region IPanelStatePersistable Implementation

    /// <summary>
    /// Gets the current panel state for persistence.
    /// Backend-Frontend Integration Plan - Phase 2.
    /// </summary>
    public PanelStateData? GetCurrentState()
    {
      try
      {
        var state = new PanelStateData
        {
          PanelId = PanelId,
          SelectedItemId = SelectedAsset?.Id,
          SearchText = SearchQuery,
          CapturedAt = DateTime.UtcNow,
          CustomData = new Dictionary<string, object>()
        };

        // Store folder selection
        if (SelectedFolder != null)
          state.CustomData["SelectedFolderId"] = SelectedFolder.Id;

        // Store asset type filter
        if (!string.IsNullOrEmpty(SelectedAssetType))
          state.CustomData["SelectedAssetType"] = SelectedAssetType;

        // Store view settings
        state.CustomData["ShowFolders"] = ShowFolders;

        // Store multi-select state
        if (_multiSelectState?.SelectedIds.Count > 0)
          state.SelectedItemIds = _multiSelectState.SelectedIds.ToArray();

        return state;
      }
      catch (Exception ex)
      {
        // Log state persistence failure silently (non-critical)
        ErrorLoggingService?.LogWarning($"Failed to get LibraryViewModel state: {ex.Message}", "GetPanelState");
        return null;
      }
    }

    /// <summary>
    /// Restores panel state from persistence.
    /// Backend-Frontend Integration Plan - Phase 2.
    /// </summary>
    public async Task RestoreStateAsync(PanelStateData state, CancellationToken cancellationToken = default)
    {
      if (state == null) return;

      try
      {
        // Restore search query
        if (!string.IsNullOrEmpty(state.SearchText))
          SearchQuery = state.SearchText;

        // Restore asset type filter
        if (state.CustomData?.TryGetValue("SelectedAssetType", out var assetType) == true && assetType is string assetTypeStr)
          SelectedAssetType = assetTypeStr;

        // Restore view settings
        if (state.CustomData?.TryGetValue("ShowFolders", out var showFolders) == true && showFolders is bool showFoldersBool)
          ShowFolders = showFoldersBool;

        // Restore folder selection (need to wait for folders to load)
        if (state.CustomData?.TryGetValue("SelectedFolderId", out var folderId) == true && folderId is string folderIdStr)
        {
          var folder = Folders.FirstOrDefault(f => f.Id == folderIdStr);
          if (folder != null)
            SelectedFolder = folder;
        }

        // Restore asset selection (need to wait for assets to load)
        if (!string.IsNullOrEmpty(state.SelectedItemId))
        {
          var asset = Assets.FirstOrDefault(a => a.Id == state.SelectedItemId);
          if (asset != null)
            SelectedAsset = asset;
        }

        // Restore multi-select state
        if (state.SelectedItemIds?.Length > 0 && _multiSelectState != null)
        {
          foreach (var id in state.SelectedItemIds)
          {
            _multiSelectState.Add(id);
          }
          _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
          UpdateAssetSelectionProperties();
        }

        await Task.CompletedTask;
      }
      catch (Exception ex)
      {
        // Log state restoration failure silently (non-critical)
        ErrorLoggingService?.LogWarning($"Failed to restore LibraryViewModel state: {ex.Message}", "RestoreState");
      }
    }

    #endregion

  }

  // Data models
  public class LibraryFolder
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public string Path { get; set; } = string.Empty;
    public DateTime Created { get; set; }
    public DateTime Modified { get; set; }
    public int AssetCount { get; set; }
  }

  public class LibraryAsset
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? FolderId { get; set; }
    public System.Collections.Generic.List<string> Tags { get; set; } = new();
    public System.Collections.Generic.Dictionary<string, object> Metadata { get; set; } = new();
    public DateTime Created { get; set; }
    public DateTime Modified { get; set; }
    public long Size { get; set; }
    public double? Duration { get; set; }
    public string? ThumbnailUrl { get; set; }
  }
}