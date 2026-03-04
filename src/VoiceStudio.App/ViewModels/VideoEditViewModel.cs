using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.Storage.Pickers;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for video editing panel.
  /// </summary>
  public class VideoEditViewModel : BaseViewModel, IPanelView
  {
    public string PanelId => "video-edit";
    public string DisplayName => ResourceHelper.GetString("Panel.VideoEdit.DisplayName", "Video Editing");
    public PanelRegion Region => PanelRegion.Center;
    private readonly IBackendClient _backendClient;

    private string? _selectedVideoPath;
    private double _videoDuration;
    private double _trimStart;
    private double _trimEnd;
    private string? _selectedEffect;
    private string? _selectedTransition;
    private string _exportFormat = "mp4";
    private int _exportQuality = 5;
    private string? _selectedOperation;
    private double _startTime;
    private double _endTime;
    private ObservableCollection<string> _qualityPresets = new();
    private string? _selectedQuality;

    public VideoEditViewModel(IViewModelContext context, IBackendClient backendClient)
        : base(context)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));

      Effects = new ObservableCollection<string>
            {
                "Brightness",
                "Contrast",
                "Saturation",
                "Blur",
                "Sharpen",
                "Grayscale",
                "Sepia",
                "Vignette"
            };

      Transitions = new ObservableCollection<string>
            {
                "Fade In",
                "Fade Out",
                "Cross Fade"
            };

      ExportFormats = new ObservableCollection<string>
            {
                "mp4",
                "avi",
                "mov",
                "mkv",
                "webm"
            };

      SelectVideoCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("SelectVideo");
        await SelectVideoAsync(ct);
      }, () => !IsLoading);
      TrimCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("TrimVideo");
        await TrimVideoAsync(ct);
      }, () => CanTrim);
      SplitCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("SplitVideo");
        await SplitVideoAsync(ct);
      }, () => CanSplit);
      ApplyEffectCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ApplyEffect");
        await ApplyEffectAsync(ct);
      }, () => CanApplyEffect);
      ApplyTransitionCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ApplyTransition");
        await ApplyTransitionAsync(ct);
      }, () => CanApplyTransition);
      ExportCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ExportVideo");
        await ExportVideoAsync(ct);
      }, () => CanExport);
    }

    public ObservableCollection<string> Effects { get; }
    public ObservableCollection<string> Transitions { get; }
    public ObservableCollection<string> ExportFormats { get; }

    public string? SelectedVideoPath
    {
      get => _selectedVideoPath;
      set
      {
        if (SetProperty(ref _selectedVideoPath, value))
        {
          if (!string.IsNullOrWhiteSpace(value))
          {
            _ = LoadVideoInfoAsync();
          }
          else
          {
            VideoDuration = 0;
            TrimStart = 0;
            TrimEnd = 0;
          }
          UpdateCommandStates();
        }
      }
    }

    public double VideoDuration
    {
      get => _videoDuration;
      set
      {
        SetProperty(ref _videoDuration, value);
        if (TrimEnd > value)
        {
          TrimEnd = value;
        }
        UpdateCommandStates();
      }
    }

    public double TrimStart
    {
      get => _trimStart;
      set
      {
        if (SetProperty(ref _trimStart, value))
        {
          if (value > TrimEnd)
          {
            TrimEnd = value;
          }
          UpdateCommandStates();
        }
      }
    }

    public double TrimEnd
    {
      get => _trimEnd;
      set
      {
        if (SetProperty(ref _trimEnd, value))
        {
          if (value < TrimStart)
          {
            TrimStart = value;
          }
          if (VideoDuration > 0 && value > VideoDuration)
          {
            TrimEnd = VideoDuration;
          }
          UpdateCommandStates();
        }
      }
    }

    public string? SelectedEffect
    {
      get => _selectedEffect;
      set
      {
        SetProperty(ref _selectedEffect, value);
        UpdateCommandStates();
      }
    }

    public string? SelectedTransition
    {
      get => _selectedTransition;
      set
      {
        SetProperty(ref _selectedTransition, value);
        UpdateCommandStates();
      }
    }

    public string ExportFormat
    {
      get => _exportFormat;
      set => SetProperty(ref _exportFormat, value);
    }

    public int ExportQuality
    {
      get => _exportQuality;
      set => SetProperty(ref _exportQuality, value);
    }

    public bool CanTrim => !string.IsNullOrWhiteSpace(SelectedVideoPath) &&
                           TrimStart >= 0 &&
                           TrimEnd > TrimStart &&
                           TrimEnd <= VideoDuration &&
                           !IsLoading;

    public bool CanSplit => !string.IsNullOrWhiteSpace(SelectedVideoPath) &&
                           TrimStart >= 0 &&
                           TrimStart < VideoDuration &&
                           !IsLoading;

    public bool CanApplyEffect => !string.IsNullOrWhiteSpace(SelectedVideoPath) &&
                                 !string.IsNullOrWhiteSpace(SelectedEffect) &&
                                 !IsLoading;

    public bool CanApplyTransition => !string.IsNullOrWhiteSpace(SelectedVideoPath) &&
                                     !string.IsNullOrWhiteSpace(SelectedTransition) &&
                                     !IsLoading;

    public bool CanExport => !string.IsNullOrWhiteSpace(SelectedVideoPath) && !IsLoading;

    public string? SelectedOperation
    {
      get => _selectedOperation;
      set => SetProperty(ref _selectedOperation, value);
    }

    public double StartTime
    {
      get => _startTime;
      set => SetProperty(ref _startTime, value);
    }

    public double EndTime
    {
      get => _endTime;
      set => SetProperty(ref _endTime, value);
    }

    public ObservableCollection<string> QualityPresets => _qualityPresets;

    public string? SelectedQuality
    {
      get => _selectedQuality;
      set => SetProperty(ref _selectedQuality, value);
    }

    public ICommand SelectVideoCommand { get; }
    public ICommand TrimCommand { get; }
    public ICommand SplitCommand { get; }
    public ICommand ApplyEffectCommand { get; }
    public ICommand ApplyTransitionCommand { get; }
    public ICommand ExportCommand { get; }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
      base.OnPropertyChanged(e);

      if (e.PropertyName == nameof(IsLoading))
      {
        UpdateCommandStates();
      }
    }

    private void UpdateCommandStates()
    {
      ((ICommand)TrimCommand).NotifyCanExecuteChanged();
      ((ICommand)SplitCommand).NotifyCanExecuteChanged();
      ((ICommand)ApplyEffectCommand).NotifyCanExecuteChanged();
      ((ICommand)ApplyTransitionCommand).NotifyCanExecuteChanged();
      ((ICommand)ExportCommand).NotifyCanExecuteChanged();
    }

    private async Task SelectVideoAsync(CancellationToken cancellationToken)
    {
      try
      {
        cancellationToken.ThrowIfCancellationRequested();

        var openPicker = new FileOpenPicker();
        openPicker.SuggestedStartLocation = PickerLocationId.VideosLibrary;
        openPicker.FileTypeFilter.Add(".mp4");
        openPicker.FileTypeFilter.Add(".avi");
        openPicker.FileTypeFilter.Add(".mov");
        openPicker.FileTypeFilter.Add(".mkv");
        openPicker.FileTypeFilter.Add(".webm");

        // WinUI 3 requires initializing the picker with the window handle
        var window = App.MainWindowInstance;
        if (window != null)
        {
          var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
          WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hwnd);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var file = await openPicker.PickSingleFileAsync();
        cancellationToken.ThrowIfCancellationRequested();

        if (file != null)
        {
          SelectedVideoPath = file.Path;
          StatusMessage = ResourceHelper.FormatString("VideoEdit.VideoSelected", file.Name);
          ErrorMessage = null;
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "Video selection");
      }
    }

    private async Task LoadVideoInfoAsync(CancellationToken cancellationToken = default)
    {
      if (string.IsNullOrWhiteSpace(SelectedVideoPath))
        return;

      try
      {
        IsLoading = true;
        var info = await _backendClient.GetVideoInfoAsync(SelectedVideoPath, cancellationToken);
        VideoDuration = info.Duration;
        TrimStart = 0;
        TrimEnd = info.Duration;
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("VideoEdit.LoadVideoInfoFailed", ex.Message);
        await HandleErrorAsync(ex, "Loading video info", showDialog: false);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task TrimVideoAsync(CancellationToken cancellationToken)
    {
      if (!CanTrim)
        return;

      IsLoading = true;
      ErrorMessage = null;
      StatusMessage = ResourceHelper.GetString("VideoEdit.TrimmingVideo", "Trimming video...");

      try
      {
        var request = new VideoEditRequest
        {
          Operation = "trim",
          InputPath = SelectedVideoPath,
          StartTime = TrimStart,
          EndTime = TrimEnd
        };

        var response = await _backendClient.EditVideoAsync(request, cancellationToken);

        if (response.Success && !string.IsNullOrWhiteSpace(response.OutputPath))
        {
          StatusMessage = response.Message ?? ResourceHelper.GetString("VideoEdit.VideoTrimmedSuccess", "Video trimmed successfully!");
          SelectedVideoPath = response.OutputPath; // Update to trimmed video
          await LoadVideoInfoAsync(cancellationToken); // Reload info for trimmed video
        }
        else
        {
          ErrorMessage = response.Message ?? ResourceHelper.GetString("VideoEdit.TrimVideoFailed", "Failed to trim video");
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "Video trimming");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private VideoEditRequest? CreateEditRequest()
    {
      if (string.IsNullOrWhiteSpace(SelectedVideoPath))
        return null;

      // Map UI operation names to backend operation names
      string? backendOperation = SelectedOperation switch
      {
        "Cut/Trim" => "trim",
        "Resize" => "resize",
        "Add Audio" => "add_audio",
        "Transcode" => "export",
        "Upscale" => "upscale",
        _ => null
      };

      if (backendOperation == null)
        return null;

      var request = new VideoEditRequest
      {
        Operation = backendOperation,
        InputPath = SelectedVideoPath
      };

      // Set operation-specific parameters
      switch (SelectedOperation)
      {
        case "Cut/Trim":
          request.StartTime = StartTime;
          request.EndTime = EndTime > StartTime ? EndTime : StartTime + 10; // Default 10s if invalid
          break;
        case "Resize":
          request.Format = "mp4";
          // Note: Backend resize operation may need width/height in future
          break;
        case "Add Audio":
          // Note: Backend add_audio operation may need audio path in future
          break;
        case "Transcode":
          request.Format = "mp4";
          var qualityIndex = !string.IsNullOrEmpty(SelectedQuality) ? QualityPresets.IndexOf(SelectedQuality) : -1;
          request.Quality = Math.Clamp(qualityIndex + 1, 1, Math.Max(1, QualityPresets.Count)); // 1..Count
          break;
        case "Upscale":
          request.Format = "mp4";
          // Note: Backend upscale operation may need scale factor in future
          break;
      }

      return request;
    }

    private async Task SplitVideoAsync(CancellationToken cancellationToken)
    {
      if (!CanSplit)
        return;

      IsLoading = true;
      ErrorMessage = null;
      StatusMessage = ResourceHelper.GetString("VideoEdit.SplittingVideo", "Splitting video...");

      try
      {
        var request = new VideoEditRequest
        {
          Operation = "split",
          InputPath = SelectedVideoPath,
          SplitTime = TrimStart
        };

        var response = await _backendClient.EditVideoAsync(request, cancellationToken);

        if (response.Success)
        {
          StatusMessage = response.Message ?? ResourceHelper.GetString("VideoEdit.VideoSplitSuccess", "Video split successfully!");
        }
        else
        {
          ErrorMessage = response.Message ?? ResourceHelper.GetString("VideoEdit.SplitVideoFailed", "Failed to split video");
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "Video splitting");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ApplyEffectAsync(CancellationToken cancellationToken = default)
    {
      if (!CanApplyEffect)
        return;

      IsLoading = true;
      ErrorMessage = null;
      StatusMessage = ResourceHelper.FormatString("VideoEdit.ApplyingEffect", SelectedEffect ?? string.Empty);

      try
      {
        var request = new VideoEditRequest
        {
          Operation = "effect",
          InputPath = SelectedVideoPath,
          Effect = SelectedEffect
        };

        var response = await _backendClient.EditVideoAsync(request, cancellationToken);

        if (response.Success && !string.IsNullOrWhiteSpace(response.OutputPath))
        {
          StatusMessage = response.Message ?? ResourceHelper.FormatString("VideoEdit.EffectAppliedSuccess", SelectedEffect ?? string.Empty);
          SelectedVideoPath = response.OutputPath; // Update to video with effect
          await LoadVideoInfoAsync(cancellationToken); // Reload info
        }
        else
        {
          ErrorMessage = response.Message ?? ResourceHelper.FormatString("VideoEdit.ApplyEffectFailed", SelectedEffect ?? string.Empty);
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("VideoEdit.ApplyEffectError", ex.Message);
        await HandleErrorAsync(ex, "Applying video effect");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ApplyTransitionAsync(CancellationToken cancellationToken)
    {
      if (!CanApplyTransition)
        return;

      IsLoading = true;
      ErrorMessage = null;
      StatusMessage = ResourceHelper.FormatString("VideoEdit.ApplyingTransition", SelectedTransition ?? string.Empty);

      try
      {
        var request = new VideoEditRequest
        {
          Operation = "transition",
          InputPath = SelectedVideoPath,
          Transition = SelectedTransition,
          Duration = 1.0 // Default 1 second transition
        };

        var response = await _backendClient.EditVideoAsync(request, cancellationToken);

        if (response.Success && !string.IsNullOrWhiteSpace(response.OutputPath))
        {
          StatusMessage = response.Message ?? ResourceHelper.FormatString("VideoEdit.TransitionAppliedSuccess", SelectedTransition ?? string.Empty);
          SelectedVideoPath = response.OutputPath; // Update to video with transition
          await LoadVideoInfoAsync(cancellationToken); // Reload info
        }
        else
        {
          ErrorMessage = response.Message ?? ResourceHelper.FormatString("VideoEdit.ApplyTransitionFailed", SelectedTransition ?? string.Empty);
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "Applying video transition");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ExportVideoAsync(CancellationToken cancellationToken)
    {
      if (!CanExport)
        return;

      IsLoading = true;
      ErrorMessage = null;
      StatusMessage = ResourceHelper.FormatString("VideoEdit.ExportingVideo", ExportFormat);

      try
      {
        var request = new VideoEditRequest
        {
          Operation = "export",
          InputPath = SelectedVideoPath,
          Format = ExportFormat,
          Quality = ExportQuality
        };

        var response = await _backendClient.EditVideoAsync(request, cancellationToken);

        if (response.Success && !string.IsNullOrWhiteSpace(response.OutputPath))
        {
          StatusMessage = response.Message ?? ResourceHelper.FormatString("VideoEdit.VideoExportedSuccess", ExportFormat);

          // Open output folder
          var folderPath = Path.GetDirectoryName(response.OutputPath);
          if (!string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath))
          {
            System.Diagnostics.Process.Start("explorer.exe", folderPath);
          }
        }
        else
        {
          ErrorMessage = response.Message ?? ResourceHelper.FormatString("VideoEdit.ExportVideoFailed", ExportFormat);
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "Exporting video");
      }
      finally
      {
        IsLoading = false;
      }
    }
  }
}