using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the ScriptEditorView panel - Advanced script editor for voice synthesis.
  /// </summary>
  public partial class ScriptEditorViewModel : BaseViewModel, IPanelView
  {
    private readonly IBackendClient _backendClient;
    private readonly IDialogService _dialogService;
    private readonly UndoRedoService? _undoRedoService;
    private readonly ToastNotificationService? _toastNotificationService;
    private readonly MultiSelectService _multiSelectService;
    private MultiSelectState? _multiSelectState;

    public string PanelId => "script-editor";
    public string DisplayName => ResourceHelper.GetString("Panel.ScriptEditor.DisplayName", "Script Editor");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private ObservableCollection<ScriptItem> scripts = new();

    [ObservableProperty]
    private ScriptItem? selectedScript;

    [ObservableProperty]
    private string? selectedProjectId;

    [ObservableProperty]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> availableProjects = new();

    [ObservableProperty]
    private ScriptSegment? selectedSegment;

    [ObservableProperty]
    private string newScriptName = string.Empty;

    [ObservableProperty]
    private string newScriptDescription = string.Empty;

    // Multi-select support
    [ObservableProperty]
    private int selectedScriptCount;

    [ObservableProperty]
    private bool hasMultipleScriptSelection;

    public bool IsScriptSelected(string scriptId) => _multiSelectState?.SelectedIds.Contains(scriptId) ?? false;

    public ScriptEditorViewModel(IViewModelContext context, IBackendClient backendClient, IDialogService dialogService)
        : base(context)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
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

      LoadScriptsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadScripts");
        await LoadScriptsAsync(ct);
      }, () => !IsLoading);
      CreateScriptCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CreateScript");
        await CreateScriptAsync(ct);
      }, () => !IsLoading);
      UpdateScriptCommand = new EnhancedAsyncRelayCommand<ScriptItem>(async (script, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("UpdateScript");
        await UpdateScriptAsync(script, ct);
      }, (script) => script != null && !IsLoading);
      DeleteScriptCommand = new EnhancedAsyncRelayCommand<ScriptItem>(async (script, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteScript");
        await DeleteScriptAsync(script, ct);
      }, (script) => script != null && !IsLoading);
      SynthesizeScriptCommand = new EnhancedAsyncRelayCommand<ScriptItem>(async (script, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("SynthesizeScript");
        await SynthesizeScriptAsync(script, ct);
      }, (script) => script != null && !IsLoading);
      AddSegmentCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("AddSegment");
        await AddSegmentAsync(ct);
      }, () => !IsLoading);
      RemoveSegmentCommand = new EnhancedAsyncRelayCommand<ScriptSegment>(async (segment, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("RemoveSegment");
        await RemoveSegmentAsync(segment, ct);
      }, (segment) => segment != null && !IsLoading);
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      }, () => !IsLoading);

      // Multi-select commands
      SelectAllScriptsCommand = new RelayCommand(SelectAllScripts, () => Scripts?.Count > 0);
      ClearScriptSelectionCommand = new RelayCommand(ClearScriptSelection);
      DeleteSelectedScriptsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteSelectedScripts");
        await DeleteSelectedScriptsAsync(ct);
      }, () => SelectedScriptCount > 0 && !IsLoading);

      // Subscribe to selection changes
      _multiSelectService.SelectionChanged += (_, e) =>
      {
        if (e.PanelId == PanelId)
        {
          UpdateScriptSelectionProperties();
          OnPropertyChanged(nameof(SelectedScriptCount));
          OnPropertyChanged(nameof(HasMultipleScriptSelection));
        }
      };

      // Load initial data
      _ = LoadScriptsAsync(CancellationToken.None);
    }

    public IAsyncRelayCommand LoadScriptsCommand { get; }
    public IAsyncRelayCommand CreateScriptCommand { get; }
    public IAsyncRelayCommand<ScriptItem> UpdateScriptCommand { get; }
    public IAsyncRelayCommand<ScriptItem> DeleteScriptCommand { get; }
    public IAsyncRelayCommand<ScriptItem> SynthesizeScriptCommand { get; }
    public IAsyncRelayCommand AddSegmentCommand { get; }
    public IAsyncRelayCommand<ScriptSegment> RemoveSegmentCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    // Multi-select commands
    public IRelayCommand SelectAllScriptsCommand { get; }
    public IRelayCommand ClearScriptSelectionCommand { get; }
    public IAsyncRelayCommand DeleteSelectedScriptsCommand { get; }

    private async Task LoadScriptsAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var scripts = await _backendClient.GetScriptsAsync(SelectedProjectId, SearchQuery, cancellationToken);

        Scripts.Clear();
        if (scripts != null)
        {
          foreach (var script in scripts)
          {
            Scripts.Add(new ScriptItem(script));
          }
        }

        if (Scripts.Count > 0)
        {
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("ScriptEditor.ScriptsLoaded", Scripts.Count),
              ResourceHelper.GetString("Toast.Title.ScriptsLoaded", "Scripts Loaded"));
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadScripts");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task CreateScriptAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SelectedProjectId))
      {
        ErrorMessage = ResourceHelper.GetString("ScriptEditor.ProjectRequired", "Project must be selected");
        return;
      }

      if (string.IsNullOrWhiteSpace(NewScriptName))
      {
        ErrorMessage = ResourceHelper.GetString("ScriptEditor.ScriptNameRequired", "Script name is required");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new ScriptCreateRequest
        {
          Name = NewScriptName,
          Description = NewScriptDescription,
          ProjectId = SelectedProjectId
        };

        var created = await _backendClient.CreateScriptAsync(request, cancellationToken);

        if (created != null)
        {
          var scriptItem = new ScriptItem(created);
          Scripts.Add(scriptItem);
          SelectedScript = scriptItem;

          // Register undo action
          if (_undoRedoService != null)
          {
            var action = new CreateScriptAction(
                Scripts,
                _backendClient,
                scriptItem,
                onUndo: (s) =>
                {
                  if (SelectedScript?.Id == s.Id)
                  {
                    SelectedScript = Scripts.FirstOrDefault();
                  }
                },
                onRedo: (s) => SelectedScript = s);
            _undoRedoService.RegisterAction(action);
          }

          NewScriptName = string.Empty;
          NewScriptDescription = string.Empty;
          StatusMessage = ResourceHelper.GetString("ScriptEditor.ScriptCreated", "Script created");
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("ScriptEditor.ScriptCreatedSuccess", created.Name),
              ResourceHelper.GetString("Toast.Title.ScriptCreated", "Script Created"));
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "CreateScript");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task UpdateScriptAsync(ScriptItem? script, CancellationToken cancellationToken = default)
    {
      if (script == null)
        return;

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var request = new ScriptUpdateRequest
        {
          Name = script.Name,
          Description = script.Description,
          Segments = script.Segments.ToList(),
          Metadata = script.Metadata
        };

        var updated = await _backendClient.UpdateScriptAsync(script.Id, request, cancellationToken);

        if (updated != null)
        {
          script.UpdateFrom(updated);
        }

        await LoadScriptsAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("ScriptEditor.ScriptUpdated", "Script updated");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.FormatString("ScriptEditor.ScriptUpdatedDetail", script.Name),
            ResourceHelper.GetString("Toast.Title.ScriptUpdated", "Script Updated"));
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("ScriptEditor.UpdateScriptFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.UpdateScriptFailed", "Failed to Update Script"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task DeleteScriptAsync(ScriptItem? script, CancellationToken cancellationToken)
    {
      if (script == null)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        await _backendClient.DeleteScriptAsync(script.Id, cancellationToken);
        var scriptToDelete = script;
        var originalIndex = Scripts.IndexOf(script);
        Scripts.Remove(script);
        if (SelectedScript == script)
        {
          SelectedScript = null;
        }

        // Register undo action
        if (_undoRedoService != null)
        {
          var action = new DeleteScriptAction(
              Scripts,
              _backendClient,
              scriptToDelete,
              originalIndex,
              onUndo: (s) => SelectedScript = s,
              onRedo: (s) =>
              {
                if (SelectedScript?.Id == s.Id)
                {
                  SelectedScript = null;
                }
              });
          _undoRedoService.RegisterAction(action);
        }

        StatusMessage = ResourceHelper.GetString("ScriptEditor.ScriptDeleted", "Script deleted");
        var scriptName = scriptToDelete?.Name ?? ResourceHelper.GetString("ScriptEditor.UnknownScript", "Unknown Script");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.FormatString("ScriptEditor.ScriptDeletedDetail", scriptName),
            ResourceHelper.GetString("Toast.Title.ScriptDeleted", "Script Deleted"));
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "DeleteScript");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task SynthesizeScriptAsync(ScriptItem? script, CancellationToken cancellationToken = default)
    {
      if (script == null)
        return;

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var response = await _backendClient.SynthesizeScriptAsync(script.Id, cancellationToken);
        StatusMessage = ResourceHelper.FormatString("ScriptEditor.ScriptSynthesized", response.AudioId);
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.FormatString("ScriptEditor.ScriptSynthesizedDetail", script.Name, response.AudioId),
            ResourceHelper.GetString("Toast.Title.ScriptSynthesized", "Script Synthesized"));
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("ScriptEditor.SynthesizeScriptFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("ScriptEditor.SynthesizeScriptFailed", ex.Message),
            ResourceHelper.GetString("Toast.Title.SynthesisFailed", "Synthesis Failed"));
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task AddSegmentAsync(CancellationToken cancellationToken)
    {
      if (SelectedScript == null)
      {
        ErrorMessage = ResourceHelper.GetString("ScriptEditor.NoScriptSelected", "No script selected");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var segment = new ScriptSegment
        {
          Id = Guid.NewGuid().ToString(),
          Text = ResourceHelper.GetString("ScriptEditor.NewSegment", "New segment"),
          VoiceProfileId = null
        };

        var updated = await _backendClient.AddSegmentToScriptAsync(SelectedScript.Id, segment, cancellationToken);

        if (updated != null)
        {
          SelectedScript.UpdateFrom(updated);
          // Find the newly added segment (should be the last one or match by ID)
          var addedSegment = SelectedScript.Segments.FirstOrDefault(s => s.Id == segment.Id) ?? SelectedScript.Segments.LastOrDefault();

          // Register undo action
          if (_undoRedoService != null && addedSegment != null && SelectedScript != null)
          {
            var action = new AddScriptSegmentAction(
                SelectedScript,
                addedSegment,
                _backendClient,
                onUndo: (seg) =>
                {
                  if (SelectedSegment?.Id == seg.Id)
                  {
                    SelectedSegment = null;
                  }
                },
                onRedo: (seg) => SelectedSegment = seg);
            _undoRedoService.RegisterAction(action);
          }
        }

        StatusMessage = ResourceHelper.GetString("ScriptEditor.SegmentAdded", "Segment added");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("ScriptEditor.SegmentAddedSuccess", "Segment added to script successfully"),
            ResourceHelper.GetString("Toast.Title.SegmentAdded", "Segment Added"));
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "AddSegment");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task RemoveSegmentAsync(ScriptSegment? segment, CancellationToken cancellationToken = default)
    {
      if (segment == null || SelectedScript == null)
        return;

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        await _backendClient.RemoveSegmentFromScriptAsync(SelectedScript.Id, segment.Id, cancellationToken);
        var segmentToRemove = segment;
        var originalIndex = SelectedScript.Segments.IndexOf(segment);
        SelectedScript.Segments.Remove(segment);
        if (SelectedSegment == segment)
        {
          SelectedSegment = null;
        }

        // Register undo action
        if (_undoRedoService != null && SelectedScript != null)
        {
          var action = new RemoveScriptSegmentAction(
              SelectedScript,
              segmentToRemove,
              _backendClient,
              originalIndex,
              onUndo: (seg) => SelectedSegment = seg,
              onRedo: (seg) =>
              {
                if (SelectedSegment?.Id == seg.Id)
                {
                  SelectedSegment = null;
                }
              });
          _undoRedoService.RegisterAction(action);
        }

        StatusMessage = ResourceHelper.GetString("ScriptEditor.SegmentRemoved", "Segment removed");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("ScriptEditor.SegmentRemovedSuccess", "Segment removed from script successfully"),
            ResourceHelper.GetString("Toast.Title.SegmentRemoved", "Segment Removed"));
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("ScriptEditor.RemoveSegmentFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.RemoveSegmentFailed", "Failed to Remove Segment"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
      await LoadScriptsAsync(cancellationToken);
    }

    public void ToggleScriptSelection(string scriptId, bool isCtrlPressed, bool isShiftPressed)
    {
      if (_multiSelectState == null)
        return;

      if (isShiftPressed && !string.IsNullOrEmpty(_multiSelectState.RangeAnchorId))
      {
        // Range selection
        var allIds = Scripts.Select(s => s.Id).ToList();
        _multiSelectState.SetRange(_multiSelectState.RangeAnchorId, scriptId, allIds);
      }
      else if (isCtrlPressed)
      {
        // Toggle selection
        _multiSelectState.Toggle(scriptId);
        if (!_multiSelectState.SelectedIds.Contains(scriptId))
        {
          _multiSelectState.RangeAnchorId = null;
        }
        else if (_multiSelectState.RangeAnchorId == null)
        {
          _multiSelectState.RangeAnchorId = scriptId;
        }
      }
      else
      {
        // Single selection
        _multiSelectState.SetSingle(scriptId);
      }

      UpdateScriptSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
    }

    private void SelectAllScripts()
    {
      if (_multiSelectState == null)
        return;

      _multiSelectState.Clear();
      foreach (var script in Scripts)
      {
        _multiSelectState.Add(script.Id);
      }
      UpdateScriptSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
    }

    private void ClearScriptSelection()
    {
      if (_multiSelectState == null)
        return;

      _multiSelectState.Clear();
      UpdateScriptSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
      DeleteSelectedScriptsCommand.NotifyCanExecuteChanged();
    }

    private async Task DeleteSelectedScriptsAsync(CancellationToken cancellationToken)
    {
      if (_multiSelectState == null || _multiSelectState.SelectedIds.Count == 0)
        return;

      var selectedIds = new System.Collections.Generic.List<string>(_multiSelectState.SelectedIds);

      // Show confirmation dialog (Panel Hardening: IDialogService per PANEL_HARDENING_PATTERN)
      var confirmed = await _dialogService.ShowConfirmationAsync(
          "Delete scripts?",
          $"Are you sure you want to delete '{selectedIds.Count} script(s)'? This action cannot be undone.",
          "Delete",
          "Cancel");

      if (!confirmed)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var scriptsToDelete = new System.Collections.Generic.List<ScriptItem>();
        int deletedCount = 0;

        foreach (var scriptId in selectedIds)
        {
          cancellationToken.ThrowIfCancellationRequested();

          try
          {
            var script = Scripts.FirstOrDefault(s => s.Id == scriptId);
            if (script != null)
            {
              await _backendClient.DeleteScriptAsync(scriptId, cancellationToken);
              scriptsToDelete.Add(script);
              Scripts.Remove(script);
              if (SelectedScript?.Id == scriptId)
              {
                SelectedScript = null;
              }
              deletedCount++;
            }
          }
          catch (OperationCanceledException)
          {
            throw; // Re-throw cancellation to abort batch deletion
          }
          catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "ScriptEditorViewModel.DeleteSelectedScriptsAsync");
      }
        }

        // Clear selection after deletion
        ClearScriptSelection();

        // Show success toast
        if (deletedCount > 0)
        {
          StatusMessage = ResourceHelper.FormatString("ScriptEditor.ScriptsDeleted", deletedCount);
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("ScriptEditor.ScriptsDeleted", deletedCount),
              ResourceHelper.GetString("Toast.Title.BatchDeleteComplete", "Batch Delete Complete"));
        }
        if (deletedCount < selectedIds.Count)
        {
          _toastNotificationService?.ShowWarning($"Some scripts could not be deleted ({deletedCount}/{selectedIds.Count} succeeded)", "Partial Delete");
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "DeleteSelectedScripts");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private void UpdateScriptSelectionProperties()
    {
      if (_multiSelectState == null)
      {
        SelectedScriptCount = 0;
        HasMultipleScriptSelection = false;
      }
      else
      {
        SelectedScriptCount = _multiSelectState.Count;
        HasMultipleScriptSelection = _multiSelectState.Count > 1;
      }
      DeleteSelectedScriptsCommand.NotifyCanExecuteChanged();
    }
  }

  /// <summary>
  /// Wrapper class for Script with observable properties.
  /// </summary>
  public partial class ScriptItem : ObservableObject
  {
    [ObservableProperty]
    private string id;

    [ObservableProperty]
    private string name;

    [ObservableProperty]
    private string? description;

    [ObservableProperty]
    private string projectId;

    [ObservableProperty]
    private ObservableCollection<ScriptSegment> segments;

    [ObservableProperty]
    private Dictionary<string, object> metadata;

    [ObservableProperty]
    private string created;

    [ObservableProperty]
    private string modified;

    [ObservableProperty]
    private int version;

    public ScriptItem(Script script)
    {
      Id = script.Id;
      Name = script.Name;
      Description = script.Description;
      ProjectId = script.ProjectId;
      Segments = new ObservableCollection<ScriptSegment>(script.Segments ?? new List<ScriptSegment>());
      Metadata = script.Metadata ?? new Dictionary<string, object>();
      Created = script.Created;
      Modified = script.Modified;
      Version = script.Version;
    }

    public void UpdateFrom(Script script)
    {
      Id = script.Id;
      Name = script.Name;
      Description = script.Description;
      ProjectId = script.ProjectId;
      Segments.Clear();
      foreach (var segment in script.Segments ?? new List<ScriptSegment>())
      {
        Segments.Add(segment);
      }
      Metadata = script.Metadata ?? new Dictionary<string, object>();
      Created = script.Created;
      Modified = script.Modified;
      Version = script.Version;
    }
  }
}