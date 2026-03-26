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
  public partial class ScriptEditorViewModel : BaseViewModel, IPanelView, IPanelLifecycle
  {
    private readonly IScriptEditorClient _scriptEditorClient;
    private readonly IDialogService _dialogService;
    private readonly IVoiceSynthesisService? _voiceSynthesisService;
    private readonly IAudioPlayerService? _audioPlayerService;
    private readonly UndoRedoService? _undoRedoService;
    private readonly ToastNotificationService? _toastNotificationService;
    private readonly MultiSelectService _multiSelectService;
    private MultiSelectState? _multiSelectState;
    private EventHandler<VoiceStudio.App.Services.SelectionChangedEventArgs>? _selectionChangedHandler;
    private bool _selectionSubscribed;

    public string PanelId => PanelIds.ScriptEditor;
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

    /// <summary>
    /// Segments to display in the segments list. Returns SelectedScript.Segments when a script is selected,
    /// otherwise an empty collection. Use this for null-safe binding.
    /// </summary>
    public ObservableCollection<ScriptSegment> DisplaySegments =>
        SelectedScript?.Segments ?? _emptySegments;

    private static readonly ObservableCollection<ScriptSegment> _emptySegments = new();

    public bool IsScriptSelected(string scriptId) => _multiSelectState?.SelectedIds.Contains(scriptId) ?? false;

    partial void OnSelectedScriptChanged(ScriptItem? value)
    {
        NewScriptName = value?.Name ?? string.Empty;
        NewScriptDescription = value?.Description ?? string.Empty;
        OnPropertyChanged(nameof(DisplaySegments));
    }

    public ScriptEditorViewModel(
        IViewModelContext context,
        IScriptEditorClient scriptEditorClient,
        IDialogService dialogService,
        IVoiceSynthesisService? voiceSynthesisService = null,
        IAudioPlayerService? audioPlayerService = null)
        : base(context)
    {
      _scriptEditorClient = scriptEditorClient ?? throw new ArgumentNullException(nameof(scriptEditorClient));
      _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
      _voiceSynthesisService = voiceSynthesisService;
      _audioPlayerService = audioPlayerService;

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
      GenerateSegmentCommand = new EnhancedAsyncRelayCommand<ScriptSegment>(async (segment, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("GenerateSegment");
        await GenerateSegmentAsync(segment, ct);
      }, (segment) => segment != null && !IsLoading && _voiceSynthesisService != null
          && !string.IsNullOrEmpty(segment.VoiceProfileId)
          && !string.IsNullOrWhiteSpace(segment.Text));
      PlaySegmentCommand = new EnhancedAsyncRelayCommand<ScriptSegment>(async (segment, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("PlaySegment");
        await PlaySegmentAsync(segment, ct);
      }, (segment) => segment != null && !string.IsNullOrEmpty(segment.GeneratedAudioId) && _audioPlayerService != null);
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

      // Selection subscription moved to OnActivatedAsync; unsubscribe in OnDeactivatedAsync
      _selectionChangedHandler = (_, e) =>
      {
        if (e.PanelId == PanelId)
        {
          UpdateScriptSelectionProperties();
          OnPropertyChanged(nameof(SelectedScriptCount));
          OnPropertyChanged(nameof(HasMultipleScriptSelection));
        }
      };
    }

    async Task IPanelLifecycle.OnActivatedAsync(CancellationToken ct)
    {
      if (_selectionChangedHandler != null && !_selectionSubscribed)
      {
        _multiSelectService.SelectionChanged += _selectionChangedHandler;
        _selectionSubscribed = true;
      }
      await LoadScriptsAsync(ct);
    }

    Task IPanelLifecycle.OnDeactivatedAsync(CancellationToken ct)
    {
      if (_selectionChangedHandler != null && _selectionSubscribed)
      {
        _multiSelectService.SelectionChanged -= _selectionChangedHandler;
        _selectionSubscribed = false;
      }
      return Task.CompletedTask;
    }

    async Task IPanelLifecycle.RefreshAsync(CancellationToken ct) => await RefreshAsync(ct);

    public IAsyncRelayCommand LoadScriptsCommand { get; }
    public IAsyncRelayCommand CreateScriptCommand { get; }
    public IAsyncRelayCommand<ScriptItem> UpdateScriptCommand { get; }
    public IAsyncRelayCommand<ScriptItem> DeleteScriptCommand { get; }
    public IAsyncRelayCommand AddSegmentCommand { get; }
    public IAsyncRelayCommand<ScriptSegment> RemoveSegmentCommand { get; }
    public IAsyncRelayCommand<ScriptSegment> GenerateSegmentCommand { get; }
    public IAsyncRelayCommand<ScriptSegment> PlaySegmentCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    // Multi-select commands
    public IRelayCommand SelectAllScriptsCommand { get; }
    public IRelayCommand ClearScriptSelectionCommand { get; }
    public IAsyncRelayCommand DeleteSelectedScriptsCommand { get; }

    /// <summary>
    /// Navigates to and selects a script by ID. Used by INavigatablePanel search-result focus.
    /// </summary>
    public async Task<bool> NavigateToScriptAsync(string itemId, CancellationToken ct)
    {
      if (string.IsNullOrEmpty(itemId))
        return false;

      var script = await _scriptEditorClient.GetScriptAsync(itemId, ct);
      if (script == null)
        return false;

      SelectedProjectId = script.ProjectId;
      await LoadScriptsAsync(ct);
      ct.ThrowIfCancellationRequested();

      var match = Scripts.FirstOrDefault(s => s.Id == itemId);
      if (match != null)
      {
        SelectedScript = match;
        _multiSelectState?.SetSingle(match.Id);
        UpdateScriptSelectionProperties();
        _multiSelectService?.OnSelectionChanged(PanelId, _multiSelectState!);
        return true;
      }

      return false;
    }

    private async Task LoadScriptsAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var scripts = await _scriptEditorClient.GetScriptsAsync(SelectedProjectId, SearchQuery, cancellationToken);

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

        var created = await _scriptEditorClient.CreateScriptAsync(request, cancellationToken);

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
                _scriptEditorClient,
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
          Name = NewScriptName ?? script.Name,
          Description = NewScriptDescription ?? script.Description,
          Segments = script.Segments.ToList(),
          Metadata = script.Metadata
        };

        var updated = await _scriptEditorClient.UpdateScriptAsync(script.Id, request, cancellationToken);

        if (updated == null)
        {
          ErrorMessage = ResourceHelper.GetString("ScriptEditor.PersistFailed", "Failed to persist changes.");
          return;
        }

        var selectedScriptId = SelectedScript?.Id;
        var selectedSegmentId = SelectedSegment?.Id;

        await LoadScriptsAsync(cancellationToken);

        SelectedScript = Scripts.FirstOrDefault(s => s.Id == selectedScriptId);
        SelectedSegment = SelectedScript?.Segments.FirstOrDefault(s => s.Id == selectedSegmentId);

        StatusMessage = ResourceHelper.GetString("ScriptEditor.ScriptUpdated", "Script updated");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.FormatString("ScriptEditor.ScriptUpdatedDetail", NewScriptName ?? script.Name),
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
        await _scriptEditorClient.DeleteScriptAsync(script.Id, cancellationToken);
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
              _scriptEditorClient,
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

        var updated = await _scriptEditorClient.AddSegmentToScriptAsync(SelectedScript.Id, segment, cancellationToken);

        if (updated != null)
        {
          var previousSegmentId = SelectedSegment?.Id;
          SelectedScript.UpdateFrom(updated);
          SelectedSegment = SelectedScript.Segments.FirstOrDefault(s => s.Id == previousSegmentId);
          var addedSegment = SelectedScript.Segments.FirstOrDefault(s => s.Id == segment.Id) ?? SelectedScript.Segments.LastOrDefault();

          // Register undo action
          if (_undoRedoService != null && addedSegment != null && SelectedScript != null)
          {
            var action = new AddScriptSegmentAction(
                SelectedScript,
                addedSegment,
                _scriptEditorClient,
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

        await _scriptEditorClient.RemoveSegmentFromScriptAsync(SelectedScript.Id, segment.Id, cancellationToken);
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
              _scriptEditorClient,
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

    private async Task GenerateSegmentAsync(ScriptSegment? segment, CancellationToken cancellationToken)
    {
      if (segment == null || _voiceSynthesisService == null)
        return;

      var text = segment.Text?.Trim();
      if (string.IsNullOrWhiteSpace(text))
      {
        ErrorMessage = ResourceHelper.GetString("ScriptEditor.SegmentTextRequired", "Segment has no text to synthesize");
        return;
      }

      var profileId = segment.VoiceProfileId;
      if (string.IsNullOrEmpty(profileId))
      {
        ErrorMessage = ResourceHelper.GetString("ScriptEditor.VoiceProfileRequired", "No voice profile assigned to segment. Assign a profile or create one.");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = ScriptEditorSynthesisRequestBuilder.Build(
          segment,
          SelectedScript?.Metadata,
          text,
          profileId);

        var response = await _voiceSynthesisService.SynthesizeVoiceAsync(request, cancellationToken);

        if (response != null && !string.IsNullOrEmpty(response.AudioId) && SelectedScript != null)
        {
          var generatedAt = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
          var segmentsForUpdate = SelectedScript.Segments.Select(s =>
          {
            if (s.Id == segment.Id)
            {
              return new ScriptSegment
              {
                Id = s.Id,
                Text = s.Text,
                StartTime = s.StartTime,
                EndTime = s.EndTime,
                Speaker = s.Speaker,
                VoiceProfileId = s.VoiceProfileId,
                Prosody = s.Prosody,
                Phonemes = s.Phonemes,
                Notes = s.Notes,
                GeneratedAudioId = response.AudioId,
                GeneratedAt = generatedAt,
                GenerationProfileId = profileId,
                GenerationEngineId = request.Engine,
                GenerationStatus = "success"
              };
            }
            return s;
          }).ToList();

          // Shared edit buffer: persist visible fields so unsaved edits are not clobbered
          var updateRequest = new ScriptUpdateRequest
          {
            Name = NewScriptName ?? SelectedScript.Name,
            Description = NewScriptDescription ?? SelectedScript.Description,
            Segments = segmentsForUpdate,
            Metadata = SelectedScript.Metadata
          };
          var updated = await _scriptEditorClient.UpdateScriptAsync(SelectedScript.Id, updateRequest, cancellationToken);
          if (updated != null)
          {
            var selectedScriptId = SelectedScript?.Id;
            var selectedSegmentId = segment.Id;
            await LoadScriptsAsync(cancellationToken);
            SelectedScript = Scripts.FirstOrDefault(s => s.Id == selectedScriptId);
            SelectedSegment = SelectedScript?.Segments.FirstOrDefault(s => s.Id == selectedSegmentId);
            StatusMessage = ResourceHelper.FormatString("ScriptEditor.SegmentGenerated", response.AudioId);
            _toastNotificationService?.ShowSuccess(
                ResourceHelper.FormatString("ScriptEditor.SegmentGeneratedDetail", response.AudioId),
                ResourceHelper.GetString("Toast.Title.SegmentGenerated", "Segment Generated"));
          }
          else
          {
            ErrorMessage = ResourceHelper.GetString("ScriptEditor.PersistFailed", "Failed to persist generated output.");
          }
        }
        else if (response == null || string.IsNullOrEmpty(response.AudioId))
        {
          ErrorMessage = ResourceHelper.GetString(
            "ScriptEditor.SynthesisReturnedNoAudioId",
            "Synthesis completed but no audio was returned. Nothing was saved.");
          _toastNotificationService?.ShowWarning(
            ErrorMessage,
            ResourceHelper.GetString("Toast.Title.SegmentGenerateIncomplete", "Generate Incomplete"));
        }
        else
        {
          // Audio returned but no script context to persist (should be rare; avoids silent no-op).
          ErrorMessage = ResourceHelper.GetString(
            "ScriptEditor.SynthesisNoScriptContext",
            "Generated audio could not be saved because no script is selected.");
          _toastNotificationService?.ShowWarning(
            ErrorMessage,
            ResourceHelper.GetString("Toast.Title.SegmentGenerateIncomplete", "Generate Incomplete"));
        }
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("ScriptEditor.GenerateSegmentFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.GenerateSegmentFailed", "Generate Failed"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    /// <summary>
    /// Plays the generated audio for a segment. Segment playback is local to Script Editor per
    /// docs/design/SCRIPT_EDITOR_PLAYBACK_POLICY.md.
    /// </summary>
    private async Task PlaySegmentAsync(ScriptSegment? segment, CancellationToken cancellationToken)
    {
      if (segment == null || _audioPlayerService == null || string.IsNullOrEmpty(segment.GeneratedAudioId))
        return;

      var baseUrl = BackendPlaybackBaseUrl.Resolve(AppServices.GetService<BackendClientConfig>());

      try
      {
        await _audioPlayerService.PlayBackendAudioIdAsync(
            segment.GeneratedAudioId,
            baseUrl,
            () => _toastNotificationService?.ShowSuccess(
                ResourceHelper.GetString("Toast.Title.PlaybackComplete", "Playback Complete"),
                ResourceHelper.GetString("ScriptEditor.PlaybackComplete", "Finished playing segment")));
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("Toast.Title.Playing", "Playing"),
            ResourceHelper.GetString("ScriptEditor.PlayingSegment", "Playing generated segment"));
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("ScriptEditor.PlaySegmentFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.PlaybackFailed", "Playback Failed"),
            ex.Message);
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
              await _scriptEditorClient.DeleteScriptAsync(scriptId, cancellationToken);
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