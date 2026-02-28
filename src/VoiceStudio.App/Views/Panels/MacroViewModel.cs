using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;
using IRelayCommand = CommunityToolkit.Mvvm.Input.IRelayCommand;
using Microsoft.UI.Dispatching;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.ViewModels;

// Type aliases to resolve ambiguity with local types in VoiceStudio.App.Services namespace
using Macro = VoiceStudio.Core.Models.Macro;
using AutomationCurve = VoiceStudio.Core.Models.AutomationCurve;

namespace VoiceStudio.App.Views.Panels
{
  public partial class MacroViewModel : BaseViewModel, IPanelView
  {
    private readonly IBackendClient _backendClient;
    private readonly UndoRedoService? _undoRedoService;
    private readonly ToastNotificationService? _toastNotificationService;
    private readonly MultiSelectService _multiSelectService;
    private MultiSelectState? _macroMultiSelectState;
    private MultiSelectState? _automationCurveMultiSelectState;

    public string PanelId => "macro";
    public string DisplayName => ResourceHelper.GetString("Panel.Macros.DisplayName", "Macros");
    public PanelRegion Region => PanelRegion.Bottom;

    [ObservableProperty]
    private ObservableCollection<Macro> macros = new();

    [ObservableProperty]
    private Macro? selectedMacro;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? selectedProjectId;

    [ObservableProperty]
    private bool showMacrosView = true; // Toggle between Macros and Automation

    [ObservableProperty]
    private ObservableCollection<VoiceStudio.Core.Models.AutomationCurve> automationCurves = new();

    [ObservableProperty]
    private VoiceStudio.Core.Models.AutomationCurve? selectedAutomationCurve;

    [ObservableProperty]
    private string? selectedTrackId;

    [ObservableProperty]
    private MacroExecutionStatus? executionStatus;

    [ObservableProperty]
    private string? executingMacroId;

    // Multi-select support for macros
    [ObservableProperty]
    private int selectedMacroCount;

    [ObservableProperty]
    private bool hasMultipleMacroSelection;

    public bool IsMacroSelected(string macroId) => _macroMultiSelectState?.SelectedIds.Contains(macroId) ?? false;

    // Multi-select support for automation curves
    [ObservableProperty]
    private int selectedAutomationCurveCount;

    [ObservableProperty]
    private bool hasMultipleAutomationCurveSelection;

    public bool IsAutomationCurveSelected(string curveId) => _automationCurveMultiSelectState?.SelectedIds.Contains(curveId) ?? false;

    private System.Threading.CancellationTokenSource? _statusPollingCts;
    private bool _isPollingStatus;

    /// <summary>
    /// Computed property for showing automation view (inverse of showMacrosView).
    /// </summary>
    public bool ShowAutomationView => !ShowMacrosView;

    public bool HasMacros => Macros?.Count > 0;

    public bool HasAutomationCurves => AutomationCurves?.Count > 0;

    /// <summary>
    /// Calculate estimated time remaining for macro execution.
    /// </summary>
    public string GetEstimatedTimeRemaining(MacroExecutionStatus? status)
    {
      if (status == null || status.StartedAt == null || status.Progress <= 0 || status.Progress >= 1.0)
        return string.Empty;

      var elapsed = DateTime.UtcNow - status.StartedAt.Value;
      if (elapsed.TotalSeconds < 1)
        return ResourceHelper.GetString("Macro.Calculating", "Calculating...");

      var estimatedTotal = TimeSpan.FromSeconds(elapsed.TotalSeconds / status.Progress);
      var remaining = estimatedTotal - elapsed;

      if (remaining.TotalSeconds < 0)
        return ResourceHelper.GetString("Macro.AlmostDone", "Almost done");

      if (remaining.TotalHours >= 1)
        return ResourceHelper.FormatString("Macro.TimeRemainingHours", remaining.TotalHours);
      else if (remaining.TotalMinutes >= 1)
        return ResourceHelper.FormatString("Macro.TimeRemainingMinutes", remaining.TotalMinutes);
      else
        return ResourceHelper.FormatString("Macro.TimeRemainingSeconds", remaining.TotalSeconds);
    }

    public MacroViewModel(IViewModelContext context, IBackendClient backendClient)
        : base(context)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));

      // Get optional services using helper (reduces code duplication)
      _undoRedoService = ServiceInitializationHelper.TryGetService(() => AppServices.TryGetUndoRedoService());
      _toastNotificationService = ServiceInitializationHelper.TryGetService(() => AppServices.TryGetToastNotificationService());

      // Get multi-select service
      var multiSelectService = AppServices.TryGetMultiSelectService();
      _multiSelectService = multiSelectService ?? throw new InvalidOperationException("MultiSelectService is required but not registered");
      _macroMultiSelectState = _multiSelectService.GetState($"{PanelId}_macros");
      _automationCurveMultiSelectState = _multiSelectService.GetState($"{PanelId}_curves");

      // Subscribe to selection changes
      _multiSelectService.SelectionChanged += (s, e) =>
      {
        if (e.PanelId == $"{PanelId}_macros")
        {
          UpdateMacroSelectionProperties();
          OnPropertyChanged(nameof(SelectedMacroCount));
          OnPropertyChanged(nameof(HasMultipleMacroSelection));
        }
        else if (e.PanelId == $"{PanelId}_curves")
        {
          UpdateAutomationCurveSelectionProperties();
          OnPropertyChanged(nameof(SelectedAutomationCurveCount));
          OnPropertyChanged(nameof(HasMultipleAutomationCurveSelection));
        }
      };

      LoadMacrosCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadMacros");
        await LoadMacrosAsync(ct);
      }, () => !IsLoading);
      CreateMacroCommand = new EnhancedAsyncRelayCommand<string>(async (name) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CreateMacro");
        await CreateMacroAsync(name, CancellationToken.None);
      }, (string? name) => !IsLoading && !string.IsNullOrWhiteSpace(SelectedProjectId));
      DeleteMacroCommand = new EnhancedAsyncRelayCommand<string>(async (macroId) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteMacro");
        await DeleteMacroAsync(macroId, CancellationToken.None);
      }, (string? macroId) => !IsLoading);
      ExecuteMacroCommand = new EnhancedAsyncRelayCommand<string>(async (macroId) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ExecuteMacro");
        await ExecuteMacroAsync(macroId, CancellationToken.None);
      }, (string? macroId) => !IsLoading);
      LoadAutomationCurvesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadAutomationCurves");
        await LoadAutomationCurvesAsync(ct);
      }, () => !IsLoading && !string.IsNullOrWhiteSpace(SelectedTrackId));
      CreateAutomationCurveCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CreateAutomationCurve");
        await CreateAutomationCurveAsync(ct);
      }, () => !IsLoading && !string.IsNullOrWhiteSpace(SelectedTrackId));
      DeleteAutomationCurveCommand = new EnhancedAsyncRelayCommand<string>(async (curveId) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteAutomationCurve");
        await DeleteAutomationCurveAsync(curveId, CancellationToken.None);
      }, (string? curveId) => !IsLoading);

      // Multi-select commands for macros
      SelectAllMacrosCommand = new RelayCommand(SelectAllMacros, () => Macros?.Count > 0);
      ClearMacroSelectionCommand = new RelayCommand(ClearMacroSelection);
      DeleteSelectedMacrosCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteSelectedMacros");
        await DeleteSelectedMacrosAsync(ct);
      }, () => SelectedMacroCount > 0 && !IsLoading);

      // Multi-select commands for automation curves
      SelectAllAutomationCurvesCommand = new RelayCommand(SelectAllAutomationCurves, () => AutomationCurves?.Count > 0);
      ClearAutomationCurveSelectionCommand = new RelayCommand(ClearAutomationCurveSelection);
      DeleteSelectedAutomationCurvesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteSelectedAutomationCurves");
        await DeleteSelectedAutomationCurvesAsync(ct);
      }, () => SelectedAutomationCurveCount > 0 && !IsLoading);
    }

    public IAsyncRelayCommand LoadMacrosCommand { get; }
    public IAsyncRelayCommand<string> CreateMacroCommand { get; }
    public IAsyncRelayCommand<string> DeleteMacroCommand { get; }
    public IAsyncRelayCommand<string> ExecuteMacroCommand { get; }
    public IAsyncRelayCommand LoadAutomationCurvesCommand { get; }
    public IAsyncRelayCommand CreateAutomationCurveCommand { get; }
    public IAsyncRelayCommand<string> DeleteAutomationCurveCommand { get; }

    // Multi-select commands for macros
    public IRelayCommand SelectAllMacrosCommand { get; }
    public IRelayCommand ClearMacroSelectionCommand { get; }
    public IAsyncRelayCommand DeleteSelectedMacrosCommand { get; }

    // Multi-select commands for automation curves
    public IRelayCommand SelectAllAutomationCurvesCommand { get; }
    public IRelayCommand ClearAutomationCurveSelectionCommand { get; }
    public IAsyncRelayCommand DeleteSelectedAutomationCurvesCommand { get; }

    partial void OnSelectedProjectIdChanged(string? value)
    {
      CreateMacroCommand.NotifyCanExecuteChanged();
      if (!string.IsNullOrWhiteSpace(value))
      {
        _ = LoadMacrosAsync(CancellationToken.None);
      }
    }

    partial void OnShowMacrosViewChanged(bool value)
    {
      OnPropertyChanged(nameof(ShowAutomationView));
    }

    partial void OnSelectedTrackIdChanged(string? value)
    {
      LoadAutomationCurvesCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsLoadingChanged(bool value)
    {
      LoadAutomationCurvesCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadMacrosAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var macrosList = await _backendClient.GetMacrosAsync(SelectedProjectId, cancellationToken);

        Macros.Clear();
        foreach (var macro in macrosList)
        {
          Macros.Add(macro);
        }

        if (Macros.Count > 0)
        {
          _toastNotificationService?.ShowSuccess("Macros Loaded", $"Loaded {Macros.Count} macro(s)");
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("Macro.LoadMacrosFailed", ErrorHandler.GetUserFriendlyMessage(ex));
        await HandleErrorAsync(ex, "LoadMacros");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task CreateMacroAsync(string? name, CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(SelectedProjectId))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var macro = new Macro
        {
          Id = Guid.NewGuid().ToString(),
          Name = name,
          ProjectId = SelectedProjectId,
          IsEnabled = true,
          Created = DateTime.UtcNow,
          Modified = DateTime.UtcNow
        };

        var createdMacro = await _backendClient.CreateMacroAsync(macro, cancellationToken);
        Macros.Add(createdMacro);

        // Register undo action
        if (_undoRedoService != null)
        {
          var action = new CreateMacroAction(
              Macros,
              _backendClient,
              createdMacro,
              onUndo: (m) =>
              {
                if (SelectedMacro?.Id == m.Id)
                {
                  SelectedMacro = Macros.FirstOrDefault();
                }
              },
              onRedo: (m) => SelectedMacro = m);
          _undoRedoService.RegisterAction(action);
        }

        _toastNotificationService?.ShowSuccess("Macro Created", $"Macro '{createdMacro.Name}' created successfully");
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("Macro.CreateMacroFailed", ex.Message);
        await HandleErrorAsync(ex, "CreateMacro");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task DeleteMacroAsync(string? macroId, CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(macroId))
        return;

      var macro = Macros.FirstOrDefault(m => m.Id == macroId);
      if (macro == null)
        return;

      // Show confirmation dialog
      var confirmed = await Utilities.ConfirmationDialog.ShowDeleteConfirmationAsync(
          macro.Name ?? "Unnamed Macro",
          "macro"
      );

      if (!confirmed)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var success = await _backendClient.DeleteMacroAsync(macroId, cancellationToken);
        if (success)
        {
          var macroToDelete = Macros.FirstOrDefault(m => m.Id == macroId);
          if (macroToDelete != null)
          {
            var originalIndex = Macros.IndexOf(macroToDelete);
            Macros.Remove(macroToDelete);
            var deletedMacro = macroToDelete;
            if (SelectedMacro?.Id == macroId)
            {
              SelectedMacro = null;
            }

            // Register undo action
            if (_undoRedoService != null)
            {
              var action = new DeleteMacroAction(
                  Macros,
                  _backendClient,
                  deletedMacro,
                  originalIndex,
                  onUndo: (m) => SelectedMacro = m,
                  onRedo: (m) =>
                  {
                    if (SelectedMacro?.Id == m.Id)
                    {
                      SelectedMacro = null;
                    }
                  });
              _undoRedoService.RegisterAction(action);
            }
          }

          var macroName = macro.Name ?? ResourceHelper.GetString("Macro.UnnamedMacro", "Unnamed Macro");
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.GetString("Toast.Title.MacroDeleted", "Macro Deleted"),
              ResourceHelper.FormatString("Macro.MacroDeletedSuccess", macroName));
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to delete macro: {ex.Message}";
        await HandleErrorAsync(ex, "DeleteMacro");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ExecuteMacroAsync(string? macroId, CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(macroId))
        return;

      IsLoading = true;
      ErrorMessage = null;
      ExecutingMacroId = macroId;

      try
      {
        // Start execution (non-blocking)
        _ = Task.Run(async () =>
        {
          try
          {
            await _backendClient.ExecuteMacroAsync(macroId, cancellationToken);
          }
          catch (Exception ex)
          {
            await Task.Delay(100, cancellationToken); // Small delay to ensure status is updated
            ErrorMessage = ResourceHelper.FormatString("Macro.ExecuteMacroFailed", ex.Message);
          }
        }, cancellationToken);

        // Start polling for status
        StartStatusPolling(macroId);

        var macro = Macros.FirstOrDefault(m => m.Id == macroId);
        var macroName = macro?.Name ?? "Unknown Macro";
        _toastNotificationService?.ShowSuccess("Macro Execution Started", $"Executing macro '{macroName}'");
      }
      catch (OperationCanceledException)
      {
        ExecutingMacroId = null;
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("Macro.StartMacroExecutionFailed", ex.Message);
        await HandleErrorAsync(ex, "ExecuteMacro");
        ExecutingMacroId = null;
        IsLoading = false;
      }
    }

    private void StartStatusPolling(string macroId)
    {
      StopStatusPolling();
      _statusPollingCts = new System.Threading.CancellationTokenSource();
      _isPollingStatus = true;

      _ = Task.Run(async () =>
      {
        while (_isPollingStatus && !_statusPollingCts.Token.IsCancellationRequested)
        {
          try
          {
            var status = await _backendClient.GetMacroExecutionStatusAsync(macroId, _statusPollingCts.Token);

            // Update on UI thread using DispatcherQueue
            var dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            dispatcherQueue?.TryEnqueue(() =>
                    {
                      ExecutionStatus = status;

                      // Stop polling if execution is complete or failed
                      if (status.Status == "completed" || status.Status == "failed")
                      {
                        StopStatusPolling();
                        ExecutingMacroId = null;
                        IsLoading = false;

                        if (status.Status == "failed" && !string.IsNullOrEmpty(status.ErrorMessage))
                        {
                          ErrorMessage = $"Macro execution failed: {status.ErrorMessage}";
                          _toastNotificationService?.ShowError("Macro Execution Failed", status.ErrorMessage);
                        }
                        else if (status.Status == "completed")
                        {
                          _toastNotificationService?.ShowSuccess("Macro Execution Complete", "Macro executed successfully");
                        }
                      }
                    });
          }
          catch (Exception ex)
          {
            // Log error but continue polling
            System.Diagnostics.Debug.WriteLine($"Error polling macro status: {ex.Message}");
          }

          // Poll every 200ms
          await Task.Delay(200, _statusPollingCts.Token);
        }
      });
    }

    private void StopStatusPolling()
    {
      _isPollingStatus = false;
      _statusPollingCts?.Cancel();
      _statusPollingCts?.Dispose();
      _statusPollingCts = null;
    }

    protected override void Dispose(bool disposing)
    {
      if (IsDisposed)
      {
        return;
      }

      if (disposing)
      {
        // Stop status polling if active
        StopStatusPolling();

        // Clear collections
        Macros.Clear();
        AutomationCurves.Clear();
      }

      base.Dispose(disposing);
    }

    private async Task LoadAutomationCurvesAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(SelectedTrackId))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var curves = await _backendClient.GetAutomationCurvesAsync(SelectedTrackId, cancellationToken);

        AutomationCurves.Clear();
        foreach (var curve in curves)
        {
          AutomationCurves.Add(curve);
        }

        if (AutomationCurves.Count > 0)
        {
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.GetString("Toast.Title.AutomationCurvesLoaded", "Automation Curves Loaded"),
              ResourceHelper.FormatString("Macro.AutomationCurvesLoaded", AutomationCurves.Count));
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to load automation curves: {ex.Message}";
        await HandleErrorAsync(ex, "LoadAutomationCurves");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task CreateAutomationCurveAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(SelectedTrackId))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var curve = new VoiceStudio.Core.Models.AutomationCurve
        {
          Id = Guid.NewGuid().ToString(),
          Name = ResourceHelper.GetString("Macro.NewAutomationCurve", "New Automation Curve"),
          ParameterId = "volume",
          TrackId = SelectedTrackId,
          Interpolation = "linear",
          Points = new List<VoiceStudio.Core.Models.AutomationPoint>()
        };

        var createdCurve = await _backendClient.CreateAutomationCurveAsync(curve, cancellationToken);
        AutomationCurves.Add(createdCurve);
        SelectedAutomationCurve = createdCurve;

        // Note: Register undo action - type conversion issues between Core and ViewModels
        // if (_undoRedoService != null)
        // {
        //     var action = new CreateAutomationCurveAction(
        //         AutomationCurves,
        //         _backendClient,
        //         createdCurve,
        //         onUndo: (c) =>
        //         {
        //             if (SelectedAutomationCurve?.Id == c.Id)
        //             {
        //                 SelectedAutomationCurve = AutomationCurves.FirstOrDefault();
        //             }
        //         },
        //         onRedo: (c) =>
        //         {
        //             SelectedAutomationCurve = new VoiceStudio.Core.Models.AutomationCurve
        //             {
        //                 Id = c.Id,
        //                 Name = c.Name,
        //                 ParameterId = c.ParameterId,
        //                 TrackId = c.TrackId,
        //                 Points = c.Points?.Select(p => new VoiceStudio.Core.Models.AutomationPoint
        //                 {
        //                     Time = p.Time,
        //                     Value = p.Value,
        //                     BezierHandleInX = p.BezierHandleInX,
        //                     BezierHandleInY = p.BezierHandleInY,
        //                     BezierHandleOutX = p.BezierHandleOutX,
        //                     BezierHandleOutY = p.BezierHandleOutY
        //                 }).ToList() ?? new List<VoiceStudio.Core.Models.AutomationPoint>(),
        //                 Interpolation = c.Interpolation
        //             };
        //         });
        //     _undoRedoService.RegisterAction(action);
        // }

        _toastNotificationService?.ShowSuccess("Automation Curve Created", $"Automation curve '{createdCurve.Name}' created successfully");
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("Macro.CreateAutomationCurveFailed", ex.Message);
        await HandleErrorAsync(ex, "CreateAutomationCurve");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task DeleteAutomationCurveAsync(string? curveId, CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(curveId))
        return;

      var curve = AutomationCurves.FirstOrDefault(c => c.Id == curveId);
      if (curve == null)
        return;

      // Show confirmation dialog
      var confirmed = await Utilities.ConfirmationDialog.ShowDeleteConfirmationAsync(
          curve.Name ?? "Unnamed Curve",
          "automation curve"
      );

      if (!confirmed)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var success = await _backendClient.DeleteAutomationCurveAsync(curveId, cancellationToken);
        if (success)
        {
          var curveToDelete = AutomationCurves.FirstOrDefault(c => c.Id == curveId);
          if (curveToDelete != null)
          {
            var originalIndex = AutomationCurves.IndexOf(curveToDelete);
            AutomationCurves.Remove(curveToDelete);
            var deletedCurve = curveToDelete;
            if (SelectedAutomationCurve?.Id == curveId)
            {
              SelectedAutomationCurve = null;
            }

            // Note: Register undo action - type conversion issues between Core and ViewModels
            // if (_undoRedoService != null)
            // {
            //     var action = new DeleteAutomationCurveAction(
            //         AutomationCurves,
            //         _backendClient,
            //         deletedCurve,
            //         originalIndex,
            //         onUndo: (c) =>
            //         {
            //             SelectedAutomationCurve = new VoiceStudio.Core.Models.AutomationCurve
            //             {
            //                 Id = c.Id,
            //                 Name = c.Name,
            //                 ParameterId = c.ParameterId,
            //                 TrackId = c.TrackId,
            //                 Points = c.Points?.Select(p => new VoiceStudio.Core.Models.AutomationPoint
            //                 {
            //                     Time = p.Time,
            //                     Value = p.Value,
            //                     BezierHandleInX = p.BezierHandleInX,
            //                     BezierHandleInY = p.BezierHandleInY,
            //                     BezierHandleOutX = p.BezierHandleOutX,
            //                     BezierHandleOutY = p.BezierHandleOutY
            //                 }).ToList() ?? new List<VoiceStudio.Core.Models.AutomationPoint>(),
            //                 Interpolation = c.Interpolation
            //             };
            //         },
            //         onRedo: (c) =>
            //         {
            //             if (SelectedAutomationCurve?.Id == c.Id)
            //             {
            //                 SelectedAutomationCurve = null;
            //             }
            //         });
            //     _undoRedoService.RegisterAction(action);
            // }
          }

          var curveName = curve.Name ?? ResourceHelper.GetString("Macro.UnnamedCurve", "Unnamed Curve");
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.GetString("Toast.Title.AutomationCurveDeleted", "Automation Curve Deleted"),
              ResourceHelper.FormatString("Macro.AutomationCurveDeletedSuccess", curveName));
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to delete automation curve: {ex.Message}";
        await HandleErrorAsync(ex, "DeleteAutomationCurve");
      }
      finally
      {
        IsLoading = false;
      }
    }

    // Multi-select methods for macros
    public void ToggleMacroSelection(string macroId, bool isCtrlPressed, bool isShiftPressed)
    {
      if (_macroMultiSelectState == null)
        return;

      if (isShiftPressed && _macroMultiSelectState.RangeAnchorId != null)
      {
        // Range selection
        var anchorIndex = Macros.ToList().FindIndex(m => m.Id == _macroMultiSelectState.RangeAnchorId);
        var targetIndex = Macros.ToList().FindIndex(m => m.Id == macroId);

        if (anchorIndex >= 0 && targetIndex >= 0)
        {
          var startIndex = Math.Min(anchorIndex, targetIndex);
          var endIndex = Math.Max(anchorIndex, targetIndex);

          for (int i = startIndex; i <= endIndex; i++)
          {
            var macro = Macros[i];
            if (!_macroMultiSelectState.SelectedIds.Contains(macro.Id))
            {
              _macroMultiSelectState.Add(macro.Id);
            }
          }
        }
      }
      else if (isCtrlPressed)
      {
        // Toggle single item
        _macroMultiSelectState.Toggle(macroId);
        _macroMultiSelectState.RangeAnchorId = macroId;
      }
      else
      {
        // Single selection
        _macroMultiSelectState.SetSingle(macroId);
      }
    }

    public void SelectAllMacros()
    {
      foreach (var macro in Macros)
      {
        _macroMultiSelectState?.Add(macro.Id);
      }
    }

    public void ClearMacroSelection()
    {
      _macroMultiSelectState?.Clear();
    }

    private async Task DeleteSelectedMacrosAsync(CancellationToken cancellationToken)
    {
      if (_macroMultiSelectState == null || _macroMultiSelectState.SelectedIds.Count == 0)
        return;

      var selectedIds = _macroMultiSelectState.SelectedIds.ToList();

      // Show confirmation dialog
      var confirmed = await Utilities.ConfirmationDialog.ShowDeleteConfirmationAsync(
          $"{selectedIds.Count} macro(s)",
          "macros"
      );

      if (!confirmed)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var deletedMacros = new List<Macro>();
        foreach (var macroId in selectedIds)
        {
          cancellationToken.ThrowIfCancellationRequested();

          var macro = Macros.FirstOrDefault(m => m.Id == macroId);
          if (macro != null)
          {
            var success = await _backendClient.DeleteMacroAsync(macroId, cancellationToken);
            if (success)
            {
              deletedMacros.Add(macro);
              Macros.Remove(macro);
              if (SelectedMacro?.Id == macroId)
              {
                SelectedMacro = null;
              }
            }
          }
        }

        ClearMacroSelection();

        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("Toast.Title.MacrosDeleted", "Macros Deleted"),
            ResourceHelper.FormatString("Macro.MacrosDeleted", deletedMacros.Count));
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("Macro.DeleteMacrosFailed", ex.Message);
        await HandleErrorAsync(ex, "DeleteSelectedMacros");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private void UpdateMacroSelectionProperties()
    {
      if (_macroMultiSelectState == null)
      {
        SelectedMacroCount = 0;
        HasMultipleMacroSelection = false;
        return;
      }

      SelectedMacroCount = _macroMultiSelectState.Count;
      HasMultipleMacroSelection = _macroMultiSelectState.IsMultipleSelection;
    }

    // Multi-select methods for automation curves
    public void ToggleAutomationCurveSelection(string curveId, bool isCtrlPressed, bool isShiftPressed)
    {
      if (_automationCurveMultiSelectState == null)
        return;

      if (isShiftPressed && _automationCurveMultiSelectState.RangeAnchorId != null)
      {
        // Range selection
        var anchorIndex = AutomationCurves.ToList().FindIndex(c => c.Id == _automationCurveMultiSelectState.RangeAnchorId);
        var targetIndex = AutomationCurves.ToList().FindIndex(c => c.Id == curveId);

        if (anchorIndex >= 0 && targetIndex >= 0)
        {
          var startIndex = Math.Min(anchorIndex, targetIndex);
          var endIndex = Math.Max(anchorIndex, targetIndex);

          for (int i = startIndex; i <= endIndex; i++)
          {
            var curve = AutomationCurves[i];
            if (!_automationCurveMultiSelectState.SelectedIds.Contains(curve.Id))
            {
              _automationCurveMultiSelectState.Add(curve.Id);
            }
          }
        }
      }
      else if (isCtrlPressed)
      {
        // Toggle single item
        _automationCurveMultiSelectState.Toggle(curveId);
        _automationCurveMultiSelectState.RangeAnchorId = curveId;
      }
      else
      {
        // Single selection
        _automationCurveMultiSelectState.SetSingle(curveId);
      }
    }

    public void SelectAllAutomationCurves()
    {
      foreach (var curve in AutomationCurves)
      {
        _automationCurveMultiSelectState?.Add(curve.Id);
      }
    }

    public void ClearAutomationCurveSelection()
    {
      _automationCurveMultiSelectState?.Clear();
    }

    private async Task DeleteSelectedAutomationCurvesAsync(CancellationToken cancellationToken)
    {
      if (_automationCurveMultiSelectState == null || _automationCurveMultiSelectState.SelectedIds.Count == 0)
        return;

      var selectedIds = _automationCurveMultiSelectState.SelectedIds.ToList();

      // Show confirmation dialog
      var confirmed = await Utilities.ConfirmationDialog.ShowDeleteConfirmationAsync(
          $"{selectedIds.Count} automation curve(s)",
          "automation curves"
      );

      if (!confirmed)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var deletedCurves = new List<VoiceStudio.Core.Models.AutomationCurve>();
        foreach (var curveId in selectedIds)
        {
          cancellationToken.ThrowIfCancellationRequested();

          var curve = AutomationCurves.FirstOrDefault(c => c.Id == curveId);
          if (curve != null)
          {
            var success = await _backendClient.DeleteAutomationCurveAsync(curveId, cancellationToken);
            if (success)
            {
              deletedCurves.Add(curve);
              AutomationCurves.Remove(curve);
              if (SelectedAutomationCurve?.Id == curveId)
              {
                SelectedAutomationCurve = null;
              }
            }
          }
        }

        ClearAutomationCurveSelection();

        _toastNotificationService?.ShowSuccess("Automation Curves Deleted", $"Deleted {deletedCurves.Count} automation curve(s)");
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("Macro.DeleteAutomationCurvesFailed", ex.Message);
        await HandleErrorAsync(ex, "DeleteSelectedAutomationCurves");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private void UpdateAutomationCurveSelectionProperties()
    {
      if (_automationCurveMultiSelectState == null)
      {
        SelectedAutomationCurveCount = 0;
        HasMultipleAutomationCurveSelection = false;
        return;
      }

      SelectedAutomationCurveCount = _automationCurveMultiSelectState.Count;
      HasMultipleAutomationCurveSelection = _automationCurveMultiSelectState.IsMultipleSelection;
    }
  }
}