using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;
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

    public string PanelId => PanelIds.Library;
    public string DisplayName => ResourceHelper.GetString("Panel.Library.DisplayName", "Library");
    public PanelRegion Region => PanelRegion.Left;

    /// <summary>Product trust Pass 01 slice 2: Library import vs drag-drop→project scope until A4 §12.</summary>
    public string ImportDragDropScopeFootnote =>
        ResourceHelper.GetString(
            "Library.Pass01.ImportDragDropScopeFootnote",
            "Importing into the library (single or batch) is not the same as dragging library items onto the project: project audio copy from drag-drop is deferred until product sign-off (Workflow 5 Option A §12). "
            + "Closed paths today follow Pass 05 Option A (for example transcribe-with-project); do not assume every library action copies into the open project.");

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

    private readonly IDispatcherTimer? _searchDebounceTimer;
    private const int SearchDebounceMs = 300;

    private readonly CancellationTokenSource _disposalCts = new();
    private CancellationTokenSource? _loadAssetsCts;
    private int _loadingCount;
    private EventHandler<VoiceStudio.App.Services.SelectionChangedEventArgs>? _selectionChangedHandler;

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

      // Subscribe to selection changes (stored for Dispose unsubscribe)
      _selectionChangedHandler = (_, e) =>
      {
        if (e.PanelId == PanelId)
        {
          UpdateAssetSelectionProperties();
          OnPropertyChanged(nameof(SelectedAssetCount));
          OnPropertyChanged(nameof(HasMultipleAssetSelection));
        }
      };
      _multiSelectService.SelectionChanged += _selectionChangedHandler;

      // Initialize EventAggregator and ContextManager for cross-panel coordination
      _eventAggregator = AppServices.TryGetEventAggregator();
      _contextManager = AppServices.TryGetContextManager();
      // Subscriptions are created in EnsureEventSubscriptions (called from OnActivatedAsync)

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

      // Search debounce: UI-thread timer (no Task.Run), linked to disposal
      _searchDebounceTimer = Dispatcher.CreateTimer();
      if (_searchDebounceTimer != null)
      {
        _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(SearchDebounceMs);
        _searchDebounceTimer.IsRepeating = false;
        _searchDebounceTimer.Tick += OnSearchDebounceTick;
      }
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
      _ = CoalescedLoadAssetsAsync();
    }

    /// <summary>
    /// Auto-refresh library when a voice profile is created (clone wizard).
    /// Audit remediation X-2: Cloned voice output now visible in Library.
    /// </summary>
    private void OnProfileCreatedRefresh(ProfileCreatedEvent e)
    {
      System.Diagnostics.Debug.WriteLine(
          $"LibraryViewModel: Profile created - {e.ProfileId} ({e.ProfileName}) from {e.SourcePanelId}");
      _ = CoalescedLoadAssetsAsync();
    }

    /// <summary>
    /// Auto-refresh library when synthesis completes (Feature synthesis path).
    /// GAP-W1: SynthesisCompletedEvent from SynthesisViewModel; AssetAddedEvent covers VoiceSynthesis path.
    /// </summary>
    private void OnSynthesisCompleted(SynthesisCompletedEvent e)
    {
      System.Diagnostics.Debug.WriteLine(
          $"LibraryViewModel: Synthesis completed - {e.AudioId} from {e.SourcePanelId}");
      _ = CoalescedLoadAssetsAsync();
    }

    /// <summary>
    /// Coalesces event-triggered asset reloads: cancels prior reload, starts new one.
    /// </summary>
    private async Task CoalescedLoadAssetsAsync()
    {
      _loadAssetsCts?.Cancel();
      _loadAssetsCts?.Dispose();
      _loadAssetsCts = CancellationTokenSource.CreateLinkedTokenSource(_disposalCts.Token);
      await LoadAssetsAsync(_loadAssetsCts.Token);
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
    public async Task OnActivatedAsync(CancellationToken cancellationToken = default)
    {
      // Subscribe first so no events are missed during the load window
      EnsureEventSubscriptions();

      // Load initial data when panel becomes active (real lifecycle: await, not fire-and-forget)
      await LoadAssetTypesAsync(cancellationToken);
      cancellationToken.ThrowIfCancellationRequested();
      await LoadFoldersAsync(cancellationToken);
      cancellationToken.ThrowIfCancellationRequested();
      await LoadAssetsAsync(cancellationToken);
    }

    /// <summary>
    /// Ensures event subscriptions exist. Called on activation; re-subscribes after deactivation.
    /// </summary>
    private void EnsureEventSubscriptions()
    {
      if (_eventAggregator == null) return;
      if (_profileSelectedToken != null) return; // Already subscribed

      _profileSelectedToken = _eventAggregator.Subscribe<ProfileSelectedEvent>(OnProfileSelected);
      _assetAddedToken = _eventAggregator.Subscribe<AssetAddedEvent>(OnAssetAdded);
      _profileCreatedToken = _eventAggregator.Subscribe<ProfileCreatedEvent>(OnProfileCreatedRefresh);
      _synthesisCompletedToken = _eventAggregator.Subscribe<SynthesisCompletedEvent>(OnSynthesisCompleted);
    }

    /// <summary>
    /// Navigates to and selects an asset by ID. Used by INavigatablePanel search-result focus.
    /// </summary>
    public async Task<bool> NavigateToAssetAsync(string itemId, CancellationToken ct)
    {
      if (string.IsNullOrEmpty(itemId))
        return false;

      var match = Assets.FirstOrDefault(a => a.Id == itemId || (a.AudioId != null && a.AudioId == itemId));
      if (match != null)
      {
        SelectedAsset = match;
        _multiSelectState?.SetSingle(match.Id);
        UpdateAssetSelectionProperties();
        _multiSelectService?.OnSelectionChanged(PanelId, _multiSelectState!);
        return true;
      }

      await LoadAssetsAsync(ct);
      ct.ThrowIfCancellationRequested();
      match = Assets.FirstOrDefault(a => a.Id == itemId || (a.AudioId != null && a.AudioId == itemId));
      if (match != null)
      {
        SelectedAsset = match;
        _multiSelectState?.SetSingle(match.Id);
        UpdateAssetSelectionProperties();
        _multiSelectService?.OnSelectionChanged(PanelId, _multiSelectState!);
        return true;
      }

      return false;
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
        _searchDebounceTimer?.Stop();
        if (_searchDebounceTimer != null)
          _searchDebounceTimer.Tick -= OnSearchDebounceTick;
        if (_selectionChangedHandler != null && _multiSelectService != null)
        {
          _multiSelectService.SelectionChanged -= _selectionChangedHandler;
          _selectionChangedHandler = null;
        }
        _profileSelectedToken?.Dispose();
        _profileSelectedToken = null;
        _assetAddedToken?.Dispose();
        _assetAddedToken = null;
        _profileCreatedToken?.Dispose();
        _profileCreatedToken = null;
        _synthesisCompletedToken?.Dispose();
        _synthesisCompletedToken = null;
      }
      base.Dispose(disposing);
    }

    private async Task LoadFoldersAsync(CancellationToken cancellationToken)
    {
      IncrementLoading();
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
        DecrementLoading();
      }
    }

    private async Task LoadAssetsAsync(CancellationToken cancellationToken = default)
    {
      await SearchAssetsAsync(cancellationToken);
    }

    /// <summary>
    /// Pure logic: returns true if filter state changed between search start and result application.
    /// Extracted for deterministic unit testing without command/debounce/threading.
    /// </summary>
    internal static bool HasFilterStateChanged(
        string? queryAtStart, string? folderIdAtStart, string? assetTypeAtStart,
        string? currentQuery, string? currentFolderId, string? currentAssetType)
    {
      return currentQuery != queryAtStart
          || currentFolderId != folderIdAtStart
          || currentAssetType != assetTypeAtStart;
    }

    private async Task SearchAssetsAsync(CancellationToken cancellationToken = default)
    {
      var queryAtStart = SearchQuery;
      var folderIdAtStart = SelectedFolder?.Id;
      var assetTypeAtStart = SelectedAssetType;
      try
      {
        IncrementLoading();
        ErrorMessage = null;

        var response = await _libraryClient.SearchAssetsAsync(
            SearchQuery,
            SelectedAssetType,
            SelectedFolder?.Id,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        // Staleness guard: do not apply results if filter state changed during search
        if (HasFilterStateChanged(queryAtStart, folderIdAtStart, assetTypeAtStart, SearchQuery, SelectedFolder?.Id, SelectedAssetType))
          return;

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
        DecrementLoading();
      }
    }

    private void IncrementLoading()
    {
      if (System.Threading.Interlocked.Increment(ref _loadingCount) == 1)
        IsLoading = true;
    }

    private void DecrementLoading()
    {
      if (System.Threading.Interlocked.Decrement(ref _loadingCount) == 0)
        IsLoading = false;
    }

    private async Task CreateFolderAsync(CancellationToken cancellationToken)
    {
      IncrementLoading();
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
        DecrementLoading();
      }
    }

    private async Task DeleteAssetAsync(LibraryAsset? asset, CancellationToken cancellationToken)
    {
      if (asset == null)
        return;

      IncrementLoading();
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
        DecrementLoading();
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
      _searchDebounceTimer?.Stop();
      _searchDebounceTimer?.Start();
    }

    private void OnSearchDebounceTick(object? sender, object e)
    {
      if (_disposalCts.IsCancellationRequested) return;
      _loadAssetsCts?.Cancel();
      _loadAssetsCts?.Dispose();
      _loadAssetsCts = CancellationTokenSource.CreateLinkedTokenSource(_disposalCts.Token);
      _ = SearchAssetsAsync(_loadAssetsCts.Token);
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
          // Publish transport context so main Play targets this asset when playable
          if (CanPlayAsset(value))
          {
            var playbackId = GetPlaybackAudioId(value) ?? value.Id;
            _contextManager.SetCurrentPlayable(playbackId, TransportSource.Library, value.Name);
          }
          else
          {
            _contextManager.SetCurrentPlayable(null, null, null);
          }
        }
        else
        {
          _eventAggregator?.Publish(new AssetSelectedEvent(PanelId, value.Id, value.Type ?? "unknown", value.Name));
        }
      }
      else if (_contextManager != null)
      {
        // Clear the active asset and transport when deselected
        _contextManager.SetActiveAsset(null, null, null);
        _contextManager.SetCurrentPlayable(null, null, null);
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
    /// Primary path: direct IAudioPlayerService call (service is eagerly resolved at startup).
    /// Fallback: event/workflow path only when _audioPlayer is null (defensive; e.g. tests).
    /// </summary>
    private async void PlayAsset(LibraryAsset? asset)
    {
      if (asset == null) return;

      System.Diagnostics.Debug.WriteLine($"LibraryViewModel: PlayAsset - {asset.Id} ({asset.Name})");

      var playbackId = GetPlaybackAudioId(asset) ?? asset.Id;

      // Primary path: direct playback (IAudioPlayerService eagerly resolved at app startup)
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
          if (!string.IsNullOrEmpty(playbackId))
          {
            var baseUrl = AppServices.GetService<BackendClientConfig>()?.BaseUrl?.TrimEnd('/')
                ?? "http://localhost:8000";
            await _audioPlayer.PlayBackendAudioIdAsync(playbackId, baseUrl, () =>
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

      // Defensive fallback: event path only when IAudioPlayerService is unavailable (e.g. unit tests)
      if (_workflowCoordinator != null)
      {
        await _workflowCoordinator.StartPlayFromLibraryAsync(playbackId, asset.Path ?? string.Empty, asset.Name);
      }
      else
      {
        _eventAggregator?.Publish(new PlaybackRequestedEvent(PanelId, playbackId, asset.Path ?? string.Empty, asset.Name));
      }
    }

    /// <summary>
    /// Extracts a string from metadata value (handles string and JsonElement from System.Text.Json).
    /// </summary>
    private static string? GetStringFromMetadata(object? v)
    {
      if (v == null) return null;
      if (v is string s) return string.IsNullOrEmpty(s) ? null : s;
#if NET6_0_OR_GREATER
      if (v is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.String)
        return je.GetString();
#endif
      return v.ToString();
    }

    /// <summary>
    /// Returns the backend-playable audio ID for a library asset.
    /// Prefers first-class AudioId; falls back to metadata["upload_id"]; otherwise asset.Id.
    /// </summary>
    private static string? GetPlaybackAudioId(LibraryAsset asset)
    {
      if (asset == null) return null;
      if (!string.IsNullOrEmpty(asset.AudioId))
        return asset.AudioId;
      if (asset.Metadata != null && asset.Metadata.TryGetValue("upload_id", out var v))
      {
        var uploadId = GetStringFromMetadata(v);
        if (!string.IsNullOrEmpty(uploadId)) return uploadId;
      }
      return asset.Id;
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

      IncrementLoading();
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
        DecrementLoading();
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
    /// <summary>Backend-playable audio ID (first-class; preferred over metadata.upload_id).</summary>
    [JsonPropertyName("audio_id")]
    public string? AudioId { get; set; }
  }
}