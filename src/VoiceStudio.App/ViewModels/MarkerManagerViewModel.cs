using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.Core.Models;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the MarkerManagerView panel - Timeline markers management.
  /// </summary>
  public partial class MarkerManagerViewModel : BaseViewModel, IPanelView, ILifecyclePanelView
  {
    private readonly IMarkerManagerClient _markerManagerClient;
    private readonly IDialogService _dialogService;
    private readonly UndoRedoService? _undoRedoService;
    private readonly ToastNotificationService? _toastNotificationService;
    private readonly MultiSelectService _multiSelectService;
    private MultiSelectState? _multiSelectState;

    public string PanelId => PanelIds.MarkerManager;
    public string DisplayName => ResourceHelper.GetString("Panel.MarkerManager.DisplayName", "Marker Manager");
    public PanelRegion Region => PanelRegion.Right;

    [ObservableProperty]
    private ObservableCollection<MarkerItem> markers = new();

    [ObservableProperty]
    private MarkerItem? selectedMarker;

    // Multi-select support
    [ObservableProperty]
    private int selectedMarkerCount;

    [ObservableProperty]
    private bool hasMultipleMarkerSelection;

    public bool IsMarkerSelected(string markerId) => _multiSelectState?.SelectedIds.Contains(markerId) ?? false;

    [ObservableProperty]
    private string? selectedProjectId;

    [ObservableProperty]
    private string? selectedCategory;

    [ObservableProperty]
    private ObservableCollection<string> availableProjects = new();

    [ObservableProperty]
    private ObservableCollection<string> availableCategories = new();

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? statusMessage;

    public MarkerManagerViewModel(IViewModelContext context, IMarkerManagerClient markerManagerClient, IDialogService dialogService)
        : base(context)
    {
      _markerManagerClient = markerManagerClient ?? throw new ArgumentNullException(nameof(markerManagerClient));
      _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

      // Get undo/redo service (may be null if not initialized)
      try
      {
        _undoRedoService = AppServices.TryGetUndoRedoService();
      }
      catch
      {
        // Service may not be initialized yet - that's okay
        _undoRedoService = null;
      }

      // Get toast notification service (may be null if not initialized)
      try
      {
        _toastNotificationService = AppServices.TryGetToastNotificationService();
      }
      catch
      {
        // Service may not be initialized yet - that's okay
        _toastNotificationService = null;
      }

      // Get multi-select service
      var multiSelectService = AppServices.TryGetMultiSelectService();
      _multiSelectService = multiSelectService ?? throw new InvalidOperationException("MultiSelectService is required but not registered");
      _multiSelectState = _multiSelectService.GetState(PanelId);

      LoadMarkersCommand = new AsyncRelayCommand(LoadMarkersAsync);
      CreateMarkerCommand = new AsyncRelayCommand(CreateMarkerAsync);
      UpdateMarkerCommand = new AsyncRelayCommand<MarkerItem>(UpdateMarkerAsync);
      DeleteMarkerCommand = new AsyncRelayCommand<MarkerItem>(DeleteMarkerAsync);
      LoadCategoriesCommand = new AsyncRelayCommand(LoadCategoriesAsync);
      RefreshCommand = new AsyncRelayCommand(RefreshAsync);

      // Multi-select commands
      SelectAllMarkersCommand = new RelayCommand(SelectAllMarkers, () => Markers?.Count > 0);
      ClearMarkerSelectionCommand = new RelayCommand(ClearMarkerSelection);
      DeleteSelectedMarkersCommand = new AsyncRelayCommand(DeleteSelectedMarkersAsync, () => SelectedMarkerCount > 0 && !IsLoading);

      // Subscribe to selection changes
      _multiSelectService.SelectionChanged += (_, e) =>
      {
        if (e.PanelId == PanelId)
        {
          UpdateMarkerSelectionProperties();
          OnPropertyChanged(nameof(SelectedMarkerCount));
          OnPropertyChanged(nameof(HasMultipleMarkerSelection));
        }
      };

      // No constructor fire-and-forget — load from View Loaded via OnActivatedAsync (RETAINED_ASYNC_RULE)
    }

    /// <inheritdoc />
    public async Task OnActivatedAsync(CancellationToken cancellationToken = default)
    {
      await LoadCategoriesAsync(cancellationToken);
      await LoadMarkersAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task OnDeactivatedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
      try
      {
        await LoadMarkersAsync(cancellationToken);
        await LoadCategoriesAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("MarkerManager.MarkersRefreshed", "Markers refreshed");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("MarkerManager.MarkersRefreshedSuccessfully", "Markers refreshed successfully"),
            ResourceHelper.GetString("Toast.Title.Refreshed", "Refreshed"));
      }
      catch (Exception ex)
      {
        ErrorLoggingService?.LogError(ex, "Refresh");
        ErrorMessage = ResourceHelper.FormatString("MarkerManager.RefreshFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.RefreshFailed", "Refresh Failed"),
            ex.Message);
      }
    }

    public IAsyncRelayCommand LoadMarkersCommand { get; }
    public IAsyncRelayCommand CreateMarkerCommand { get; }
    public IAsyncRelayCommand<MarkerItem> UpdateMarkerCommand { get; }
    public IAsyncRelayCommand<MarkerItem> DeleteMarkerCommand { get; }
    public IAsyncRelayCommand LoadCategoriesCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    // Multi-select commands
    public IRelayCommand SelectAllMarkersCommand { get; }
    public IRelayCommand ClearMarkerSelectionCommand { get; }
    public IAsyncRelayCommand DeleteSelectedMarkersCommand { get; }

    private async Task LoadMarkersAsync(CancellationToken cancellationToken = default)
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var markers = await _markerManagerClient.GetMarkersAsync(SelectedProjectId, SelectedCategory, cancellationToken);

        Markers.Clear();
        if (markers != null)
        {
          foreach (var marker in markers)
          {
            Markers.Add(new MarkerItem(marker));
          }
        }

        if (Markers.Count > 0)
        {
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("MarkerManager.MarkersLoadedDetail", Markers.Count),
              ResourceHelper.GetString("Toast.Title.MarkersLoaded", "Markers Loaded"));
        }
      }
      catch (Exception ex)
      {
        ErrorLoggingService?.LogError(ex, "LoadMarkers");
        ErrorMessage = ResourceHelper.FormatString("MarkerManager.LoadMarkersFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.LoadMarkersFailed", "Failed to Load Markers"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task CreateMarkerAsync(CancellationToken cancellationToken = default)
    {
      if (string.IsNullOrEmpty(SelectedProjectId))
      {
        ErrorMessage = ResourceHelper.GetString("MarkerManager.ProjectRequired", "Project must be selected");
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var name = ResourceHelper.GetString("MarkerManager.NewMarker", "New Marker");
        var created = await _markerManagerClient.CreateMarkerAsync(SelectedProjectId!, name, 0.0, "#00FFFF", SelectedCategory, "", cancellationToken);

        if (created != null)
        {
          var markerItem = new MarkerItem(created);
          Markers.Add(markerItem);
          SelectedMarker = markerItem;

          // Register undo action
          if (_undoRedoService != null)
          {
            var action = new CreateMarkerAction(
                Markers,
                _markerManagerClient,
                markerItem,
                onUndo: (m) =>
                {
                  if (SelectedMarker?.Id == m.Id)
                  {
                    SelectedMarker = Markers.FirstOrDefault();
                  }
                },
                onRedo: (m) => SelectedMarker = m);
            _undoRedoService.RegisterAction(action);
          }

          StatusMessage = ResourceHelper.GetString("MarkerManager.MarkerCreated", "Marker created");
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("MarkerManager.MarkerCreatedDetail", created.Name),
              ResourceHelper.GetString("Toast.Title.MarkerCreated", "Marker Created"));
        }
      }
      catch (Exception ex)
      {
        ErrorLoggingService?.LogError(ex, "CreateMarker");
        ErrorMessage = ResourceHelper.FormatString("MarkerManager.CreateMarkerFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.CreateMarkerFailed", "Failed to Create Marker"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task UpdateMarkerAsync(MarkerItem? marker, CancellationToken cancellationToken = default)
    {
      if (marker == null)
        return;

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var updated = await _markerManagerClient.UpdateMarkerAsync(marker.Id, marker.Name, marker.Time, marker.Color, marker.Category, marker.Description, cancellationToken);

        if (updated != null)
        {
          marker.UpdateFrom(updated);
        }

        await LoadMarkersAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("MarkerManager.MarkerUpdated", "Marker updated");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.FormatString("MarkerManager.MarkerUpdatedDetail", marker.Name),
            ResourceHelper.GetString("Toast.Title.MarkerUpdated", "Marker Updated"));
      }
      catch (Exception ex)
      {
        ErrorLoggingService?.LogError(ex, "UpdateMarker");
        ErrorMessage = ResourceHelper.FormatString("MarkerManager.UpdateMarkerFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.UpdateMarkerFailed", "Failed to Update Marker"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task DeleteMarkerAsync(MarkerItem? marker, CancellationToken cancellationToken = default)
    {
      if (marker == null)
        return;

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        await _markerManagerClient.DeleteMarkerAsync(marker.Id, cancellationToken);

        var markerToDelete = marker;
        var originalIndex = Markers.IndexOf(marker);
        Markers.Remove(marker);
        if (SelectedMarker == marker)
        {
          SelectedMarker = null;
        }

        // Register undo action
        if (_undoRedoService != null)
        {
          var action = new DeleteMarkerAction(
              Markers,
              _markerManagerClient,
              markerToDelete,
              originalIndex,
              onUndo: (m) => SelectedMarker = m,
              onRedo: (m) =>
              {
                if (SelectedMarker?.Id == m.Id)
                {
                  SelectedMarker = null;
                }
              });
          _undoRedoService.RegisterAction(action);
        }

        StatusMessage = ResourceHelper.GetString("MarkerManager.MarkerDeleted", "Marker deleted");
        var markerName = markerToDelete?.Name ?? ResourceHelper.GetString("MarkerManager.UnknownMarker", "Unknown Marker");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.FormatString("MarkerManager.MarkerDeletedDetail", markerName),
            ResourceHelper.GetString("Toast.Title.MarkerDeleted", "Marker Deleted"));
      }
      catch (Exception ex)
      {
        ErrorLoggingService?.LogError(ex, "DeleteMarker");
        ErrorMessage = ResourceHelper.FormatString("MarkerManager.DeleteMarkerFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.DeleteMarkerFailed", "Failed to Delete Marker"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadCategoriesAsync(CancellationToken cancellationToken = default)
    {
      if (string.IsNullOrEmpty(SelectedProjectId))
        return;

      try
      {
        var response = await _markerManagerClient.GetCategoriesAsync(SelectedProjectId, cancellationToken);

        AvailableCategories.Clear();
        if (response?.Categories != null)
        {
          foreach (var category in response.Categories)
          {
            AvailableCategories.Add(category);
          }
        }
      }
      catch (Exception ex)
      {
        ErrorLoggingService?.LogError(ex, "LoadCategories");
        ErrorMessage = ResourceHelper.FormatString("MarkerManager.LoadCategoriesFailed", ex.Message);
      }
    }


    partial void OnSelectedProjectIdChanged(string? value)
    {
      _ = LoadMarkersAsync();
      _ = LoadCategoriesAsync();
    }

    partial void OnSelectedCategoryChanged(string? value)
    {
      _ = LoadMarkersAsync();
    }

    public void ToggleMarkerSelection(string markerId, bool isCtrlPressed, bool isShiftPressed)
    {
      if (_multiSelectState == null)
        return;

      if (isShiftPressed && !string.IsNullOrEmpty(_multiSelectState.RangeAnchorId))
      {
        // Range selection
        var allIds = Markers.Select(m => m.Id).ToList();
        _multiSelectState.SetRange(_multiSelectState.RangeAnchorId, markerId, allIds);
      }
      else if (isCtrlPressed)
      {
        // Toggle selection
        _multiSelectState.Toggle(markerId);
        if (!_multiSelectState.SelectedIds.Contains(markerId))
        {
          _multiSelectState.RangeAnchorId = null;
        }
        else if (_multiSelectState.RangeAnchorId == null)
        {
          _multiSelectState.RangeAnchorId = markerId;
        }
      }
      else
      {
        // Single selection
        _multiSelectState.SetSingle(markerId);
      }

      UpdateMarkerSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
    }

    private void SelectAllMarkers()
    {
      if (_multiSelectState == null)
        return;

      _multiSelectState.Clear();
      foreach (var marker in Markers)
      {
        _multiSelectState.Add(marker.Id);
      }
      UpdateMarkerSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
    }

    private void ClearMarkerSelection()
    {
      if (_multiSelectState == null)
        return;

      _multiSelectState.Clear();
      UpdateMarkerSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
      DeleteSelectedMarkersCommand.NotifyCanExecuteChanged();
    }

    private async Task DeleteSelectedMarkersAsync(CancellationToken cancellationToken = default)
    {
      if (_multiSelectState == null || _multiSelectState.SelectedIds.Count == 0)
        return;

      var selectedIds = new System.Collections.Generic.List<string>(_multiSelectState.SelectedIds);

      // Show confirmation dialog (Panel Hardening: IDialogService per PANEL_HARDENING_PATTERN)
      var confirmed = await _dialogService.ShowConfirmationAsync(
          "Delete markers?",
          $"Are you sure you want to delete '{selectedIds.Count} marker(s)'? This action cannot be undone.",
          "Delete",
          "Cancel");

      if (!confirmed)
        return;

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var markersToDelete = new System.Collections.Generic.List<MarkerItem>();
        int deletedCount = 0;

        foreach (var markerId in selectedIds)
        {
          try
          {
            await _markerManagerClient.DeleteMarkerAsync(markerId, cancellationToken);

            var marker = Markers.FirstOrDefault(m => m.Id == markerId);
            if (marker != null)
            {
              markersToDelete.Add(marker);
              Markers.Remove(marker);
              if (SelectedMarker?.Id == markerId)
              {
                SelectedMarker = null;
              }
              deletedCount++;
            }
          }
          catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "MarkerManagerViewModel.DeleteSelectedMarkersAsync");
      }
        }

        // Clear selection after deletion
        ClearMarkerSelection();

        // Show success toast
        if (deletedCount > 0)
        {
          StatusMessage = ResourceHelper.FormatString("MarkerManager.MarkersDeleted", deletedCount);
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("MarkerManager.MarkersDeletedDetail", deletedCount),
              ResourceHelper.GetString("Toast.Title.BatchDeleteComplete", "Batch Delete Complete"));
        }
        if (deletedCount < selectedIds.Count)
        {
          _toastNotificationService?.ShowWarning(
              ResourceHelper.FormatString("MarkerManager.PartialDeleteWarning", deletedCount, selectedIds.Count),
              ResourceHelper.GetString("Toast.Title.PartialDelete", "Partial Delete"));
        }
      }
      catch (Exception ex)
      {
        ErrorLoggingService?.LogError(ex, "BatchDelete");
        ErrorMessage = ResourceHelper.FormatString("MarkerManager.BatchDeleteFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.BatchDeleteFailed", "Batch Delete Failed"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private void UpdateMarkerSelectionProperties()
    {
      if (_multiSelectState == null)
      {
        SelectedMarkerCount = 0;
        HasMultipleMarkerSelection = false;
      }
      else
      {
        SelectedMarkerCount = _multiSelectState.Count;
        HasMultipleMarkerSelection = _multiSelectState.Count > 1;
      }
      DeleteSelectedMarkersCommand.NotifyCanExecuteChanged();
    }

    // Response models (public for IMarkerManagerClient)
    public class MarkerCategoriesResponse
    {
      public string[] Categories { get; set; } = Array.Empty<string>();
    }
  }

  // Data models
  public class Marker
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Time { get; set; }
    public string Color { get; set; } = "#00FFFF";
    public string? Category { get; set; }
    public string? Description { get; set; }
    public string ProjectId { get; set; } = string.Empty;
    public string Created { get; set; } = string.Empty;
    public string Modified { get; set; } = string.Empty;
  }

  public class MarkerItem : ObservableObject
  {
    public string Id { get; set; }
    public string Name { get; set; }
    public double Time { get; set; }
    public string Color { get; set; }
    public string? Category { get; set; }
    public string? Description { get; set; }
    public string ProjectId { get; set; }
    public string Created { get; set; }
    public string Modified { get; set; }
    public string TimeDisplay => $"{Time:F2}s";

    public MarkerItem(Marker marker)
    {
      Id = marker.Id;
      Name = marker.Name;
      Time = marker.Time;
      Color = marker.Color;
      Category = marker.Category;
      Description = marker.Description;
      ProjectId = marker.ProjectId;
      Created = marker.Created;
      Modified = marker.Modified;
    }

    public void UpdateFrom(Marker marker)
    {
      Name = marker.Name;
      Time = marker.Time;
      Color = marker.Color;
      Category = marker.Category;
      Description = marker.Description;
      Modified = marker.Modified;
      OnPropertyChanged(nameof(Name));
      OnPropertyChanged(nameof(Time));
      OnPropertyChanged(nameof(TimeDisplay));
      OnPropertyChanged(nameof(Color));
      OnPropertyChanged(nameof(Category));
      OnPropertyChanged(nameof(Description));
      OnPropertyChanged(nameof(Modified));
    }
  }
}