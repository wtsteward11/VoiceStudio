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
  public partial class AutomationViewModel : BaseViewModel, IPanelView
  {
    private readonly IBackendClient _backendClient;
    private readonly ToastNotificationService? _toastNotificationService;
    private readonly UndoRedoService? _undoRedoService;

    public string PanelId => "automation";
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

    public AutomationViewModel(IViewModelContext context, IBackendClient backendClient)
        : base(context)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));

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

      // Load initial data
      _ = LoadCurvesAsync(CancellationToken.None);
      _ = LoadTracksAsync(CancellationToken.None);
    }

    private async Task LoadTracksAsync(CancellationToken cancellationToken)
    {
      try
      {
        // Load tracks from the current project
        // Note: This assumes we have a current project context
        // For now, we'll try to get tracks from the automation API
        var tracks = await _backendClient.SendRequestAsync<object, TrackInfo[]>(
            "/api/automation/tracks",
            null,
            System.Net.Http.HttpMethod.Get,
            cancellationToken
        );

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
        var queryParams = new System.Collections.Specialized.NameValueCollection();
        if (!string.IsNullOrEmpty(SelectedTrackId))
          queryParams.Add("track_id", SelectedTrackId);
        if (!string.IsNullOrEmpty(SelectedParameterId))
          queryParams.Add("parameter_id", SelectedParameterId);

        var queryString = string.Join("&",
            queryParams.AllKeys.SelectMany(key =>
                queryParams.GetValues(key)?.Select(value => $"{key}={Uri.EscapeDataString(value)}") ?? Array.Empty<string>()
            )
        );

        var url = "/api/automation";
        if (!string.IsNullOrEmpty(queryString))
          url += $"?{queryString}";

        var curves = await _backendClient.SendRequestAsync<object, AutomationCurve[]>(
            url,
            null,
            System.Net.Http.HttpMethod.Get,
            cancellationToken
        );

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

        var request = new
        {
          name = $"{SelectedParameterId} automation",
          parameter_id = SelectedParameterId,
          track_id = SelectedTrackId,
          interpolation = "linear"
        };

        var created = await _backendClient.SendRequestAsync<object, AutomationCurve>(
            "/api/automation",
            request,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

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
                _backendClient,
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
        var request = new
        {
          name = curve.Name,
          points = curve.Points.Select(p => new
          {
            time = p.Time,
            value = p.Value,
            bezier_handle_in_x = p.BezierHandleInX,
            bezier_handle_in_y = p.BezierHandleInY,
            bezier_handle_out_x = p.BezierHandleOutX,
            bezier_handle_out_y = p.BezierHandleOutY
          }).ToArray(),
          interpolation = curve.Interpolation
        };

        var updated = await _backendClient.SendRequestAsync<object, AutomationCurve>(
            $"/api/automation/{curve.Id}",
            request,
            System.Net.Http.HttpMethod.Put,
            cancellationToken
        );

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
        await _backendClient.SendRequestAsync<object, object>(
            $"/api/automation/{curve.Id}",
            null,
            System.Net.Http.HttpMethod.Delete,
            cancellationToken
        );

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
              _backendClient,
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
        var response = await _backendClient.SendRequestAsync<object, TrackParametersResponse>(
            $"/api/automation/tracks/{SelectedTrackId}/parameters",
            null,
            System.Net.Http.HttpMethod.Get,
            cancellationToken
        );

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

    // Response models
    private class TrackParametersResponse
    {
      public ParameterInfo[] Parameters { get; set; } = Array.Empty<ParameterInfo>();
    }

    private class TrackInfo
    {
      public string Id { get; set; } = string.Empty;
      public string Name { get; set; } = string.Empty;
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