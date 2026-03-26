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
using VoiceStudio.App.Utilities;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the AutomationView panel - Automation curve editor.
  /// </summary>
  public partial class AutomationViewModel : BaseViewModel, IPanelView, IPanelLifecycle
  {
    private readonly IAutomationClient _automationClient;
    private readonly ToastNotificationService? _toastNotificationService;
    private readonly UndoRedoService? _undoRedoService;

    public string PanelId => PanelIds.Automation;
    public string DisplayName => ResourceHelper.GetString("Panel.Automation.DisplayName", "Automation");
    public PanelRegion Region => PanelRegion.Right;

    [ObservableProperty]
    private ObservableCollection<AutomationCurveItem> curves = new();

    [ObservableProperty]
    private AutomationCurveItem? selectedCurve;

    [ObservableProperty]
    private string? selectedTrackId;

    [ObservableProperty]
    private string? selectedParameterId;

    [ObservableProperty]
    private ObservableCollection<string> availableTracks = new();

    [ObservableProperty]
    private ObservableCollection<ParameterInfo> availableParameters = new();

    [ObservableProperty]
    private bool isEditing;

    public AutomationViewModel(IViewModelContext context, IAutomationClient automationClient)
        : base(context)
    {
      _automationClient = automationClient ?? throw new ArgumentNullException(nameof(automationClient));

      // Get services (may be null if not initialized)
      try
      {
        _toastNotificationService = AppServices.TryGetToastNotificationService();
        _undoRedoService = AppServices.TryGetUndoRedoService();
      }
      catch
      {
        // Services may not be initialized yet - that's okay
        _toastNotificationService = null;
        _undoRedoService = null;
      }

      LoadCurvesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadCurves");
        await LoadCurvesAsync(ct);
      }, () => !IsLoading);
      CreateCurveCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CreateCurve");
        await CreateCurveAsync(ct);
      }, () => !IsLoading);
      UpdateCurveCommand = new EnhancedAsyncRelayCommand<AutomationCurveItem>(async (curve, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("UpdateCurve");
        await UpdateCurveAsync(curve, ct);
      }, (curve) => curve != null && !IsLoading);
      DeleteCurveCommand = new EnhancedAsyncRelayCommand<AutomationCurveItem>(async (curve, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteCurve");
        await DeleteCurveAsync(curve, ct);
      }, (curve) => curve != null && !IsLoading);
      LoadParametersCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadParameters");
        await LoadParametersAsync(ct);
      }, () => !IsLoading);
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      }, () => !IsLoading);
    }

    Task IPanelLifecycle.OnActivatedAsync(CancellationToken ct)
    {
      _ = LoadCurvesAsync(ct);
      _ = LoadTracksAsync(ct);
      return Task.CompletedTask;
    }

    Task IPanelLifecycle.OnDeactivatedAsync(CancellationToken ct) => Task.CompletedTask;

    async Task IPanelLifecycle.RefreshAsync(CancellationToken ct) => await RefreshAsync(ct);

    private async Task LoadTracksAsync(CancellationToken cancellationToken)
    {
      try
      {
        var tracks = await _automationClient.GetTracksAsync(cancellationToken);

        AvailableTracks.Clear();
        if (tracks != null)
        {
          foreach (var track in tracks)
          {
            AvailableTracks.Add(track.Id);
          }
        }
      }
      catch (Exception ex)
      {
        // Track loading is optional - don't show error if it fails
        // Just leave AvailableTracks empty
        System.Diagnostics.Debug.WriteLine($"Failed to load tracks: {ex.Message}");
      }
    }

    public IAsyncRelayCommand LoadCurvesCommand { get; }
    public IAsyncRelayCommand CreateCurveCommand { get; }
    public IAsyncRelayCommand<AutomationCurveItem> UpdateCurveCommand { get; }
    public IAsyncRelayCommand<AutomationCurveItem> DeleteCurveCommand { get; }
    public IAsyncRelayCommand LoadParametersCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    private async Task LoadCurvesAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var curves = await _automationClient.GetCurvesAsync(SelectedTrackId, SelectedParameterId, cancellationToken);

        Curves.Clear();
        if (curves != null)
        {
          foreach (var curve in curves)
          {
            Curves.Add(new AutomationCurveItem(curve));
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadCurves");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task CreateCurveAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SelectedTrackId) || string.IsNullOrEmpty(SelectedParameterId))
      {
        ErrorMessage = ResourceHelper.GetString("Automation.TrackAndParameterRequired", "Track and parameter must be selected");
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var request = new AutomationCreateRequest
        {
          Name = $"{SelectedParameterId} automation",
          ParameterId = SelectedParameterId,
          TrackId = SelectedTrackId,
          Interpolation = "linear"
        };

        var created = await _automationClient.CreateCurveAsync(request, cancellationToken);

        if (created != null)
        {
          var curveItem = new AutomationCurveItem(created);
          Curves.Add(curveItem);
          SelectedCurve = Curves.Last();
          StatusMessage = ResourceHelper.GetString("Automation.CurveCreated", "Automation curve created");
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("Automation.CurveCreatedDetail", curveItem.Name),
              ResourceHelper.GetString("Toast.Title.CurveCreated", "Curve Created"));

          // Register undo action
          if (_undoRedoService != null)
          {
            var action = new CreateAutomationCurveAction(
                Curves,
                _automationClient,
                curveItem,
                onUndo: (c) =>
                {
                  if (SelectedCurve?.Id == c.Id)
                  {
                    SelectedCurve = Curves.FirstOrDefault();
                  }
                },
                onRedo: (c) => SelectedCurve = c);
            _undoRedoService.RegisterAction(action);
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "CreateCurve");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task UpdateCurveAsync(AutomationCurveItem? curve, CancellationToken cancellationToken)
    {
      if (curve == null)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new AutomationUpdateRequest
        {
          Name = curve.Name,
          Points = curve.Points.Select(p => new AutomationPointDto
          {
            Time = p.Time,
            Value = p.Value,
            BezierHandleInX = p.BezierHandleInX,
            BezierHandleInY = p.BezierHandleInY,
            BezierHandleOutX = p.BezierHandleOutX,
            BezierHandleOutY = p.BezierHandleOutY
          }).ToList(),
          Interpolation = curve.Interpolation
        };

        var updated = await _automationClient.UpdateCurveAsync(curve.Id, request, cancellationToken);

        if (updated != null)
        {
          curve.UpdateFrom(updated);
        }

        await LoadCurvesAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("Automation.CurveUpdated", "Automation curve updated");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.FormatString("Automation.CurveUpdatedDetail", curve.Name),
            ResourceHelper.GetString("Toast.Title.CurveUpdated", "Curve Updated"));
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "UpdateCurve");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task DeleteCurveAsync(AutomationCurveItem? curve, CancellationToken cancellationToken)
    {
      if (curve == null)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        await _automationClient.DeleteCurveAsync(curve.Id, cancellationToken);

        var curveToDelete = curve;
        var originalIndex = Curves.IndexOf(curveToDelete);
        Curves.Remove(curveToDelete);
        StatusMessage = ResourceHelper.GetString("Automation.CurveDeleted", "Automation curve deleted");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.FormatString("Automation.CurveDeletedDetail", curveToDelete.Name),
            ResourceHelper.GetString("Toast.Title.CurveDeleted", "Curve Deleted"));

        // Register undo action
        if (_undoRedoService != null && curveToDelete != null)
        {
          var action = new DeleteAutomationCurveAction(
              Curves,
              _automationClient,
              curveToDelete,
              originalIndex,
              onUndo: (c) => SelectedCurve = c,
              onRedo: (c) =>
              {
                if (SelectedCurve?.Id == c.Id)
                {
                  SelectedCurve = null;
                }
              });
          _undoRedoService.RegisterAction(action);
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "DeleteCurve");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadParametersAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SelectedTrackId))
        return;

      try
      {
        var response = await _automationClient.GetTrackParametersAsync(SelectedTrackId!, cancellationToken);

        AvailableParameters.Clear();
        if (response?.Parameters != null)
        {
          foreach (var param in response.Parameters)
          {
            AvailableParameters.Add(new ParameterInfo
            {
              Id = param.Id,
              Name = param.Name,
              Min = param.Min,
              Max = param.Max
            });
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadParameters");
      }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
      try
      {
        await LoadCurvesAsync(cancellationToken);
        await LoadTracksAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("Automation.CurvesRefreshed", "Automation curves refreshed");
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "Refresh");
      }
    }

    partial void OnSelectedTrackIdChanged(string? value)
    {
      _ = LoadParametersAsync(CancellationToken.None);
      _ = LoadCurvesAsync(CancellationToken.None);
    }

    partial void OnSelectedParameterIdChanged(string? value)
    {
      _ = LoadCurvesAsync(CancellationToken.None);
    }

  }

  // Data models
  public class AutomationCurve
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ParameterId { get; set; } = string.Empty;
    public string TrackId { get; set; } = string.Empty;
    public System.Collections.Generic.List<AutomationPoint> Points { get; set; } = new();
    public string Interpolation { get; set; } = "linear";
    public string Created { get; set; } = string.Empty;
    public string Modified { get; set; } = string.Empty;
  }

  public class AutomationPoint
  {
    public double Time { get; set; }
    public double Value { get; set; }
    public double? BezierHandleInX { get; set; }
    public double? BezierHandleInY { get; set; }
    public double? BezierHandleOutX { get; set; }
    public double? BezierHandleOutY { get; set; }
  }

  public class ParameterInfo
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Min { get; set; }
    public double Max { get; set; }
  }

  public class AutomationCurveItem : ObservableObject
  {
    public string Id { get; set; }
    public string Name { get; set; }
    public string ParameterId { get; set; }
    public string TrackId { get; set; }
    public System.Collections.Generic.List<AutomationPoint> Points { get; set; }
    public string Interpolation { get; set; }
    public int PointCount => Points?.Count ?? 0;

    public AutomationCurveItem(AutomationCurve curve)
    {
      Id = curve.Id;
      Name = curve.Name;
      ParameterId = curve.ParameterId;
      TrackId = curve.TrackId;
      Points = curve.Points ?? new System.Collections.Generic.List<AutomationPoint>();
      Interpolation = curve.Interpolation;
    }

    public void UpdateFrom(AutomationCurve curve)
    {
      Name = curve.Name;
      Points = curve.Points ?? new System.Collections.Generic.List<AutomationPoint>();
      Interpolation = curve.Interpolation;
      OnPropertyChanged(nameof(Name));
      OnPropertyChanged(nameof(Points));
      OnPropertyChanged(nameof(PointCount));
      OnPropertyChanged(nameof(Interpolation));
    }
  }
}