using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Core.Models;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the RecordingView panel.
  /// Supports both local microphone recording (via NAudio) and backend-based recording.
  /// </summary>
  public partial class RecordingViewModel : BaseViewModel, IPanelView, IPanelLifecycle
  {
    private readonly IRecordingClient _recordingClient;
    private readonly IProjectAudioClient _projectAudioClient;
    private readonly IAudioPlayerService _audioPlayer;
    private readonly ToastNotificationService? _toastNotificationService;
    private readonly IErrorPresentationService? _errorService;
    private readonly IErrorLoggingService? _logService;
    private readonly IDispatcherTimer _statusTimer;
    private readonly MicrophoneRecordingService _microphoneService;
    private ISubscriptionToken? _projectChangedToken;

    public string PanelId => PanelIds.Recording;
    public string DisplayName => ResourceHelper.GetString("Panel.Recording.DisplayName", "Recording");
    public PanelRegion Region => PanelRegion.Right;

    [ObservableProperty]
    private bool isRecording;

    [ObservableProperty]
    private string? recordingId;

    [ObservableProperty]
    private TimeSpan recordingDuration = TimeSpan.Zero;

    [ObservableProperty]
    private string recordingDurationDisplay = "00:00";

    [ObservableProperty]
    private int sampleRate = 44100;

    [ObservableProperty]
    private int channels = 1;

    [ObservableProperty]
    private int bitDepth = 16;

    [ObservableProperty]
    private string? selectedDevice;

    [ObservableProperty]
    private string? filename;

    [ObservableProperty]
    private string? projectId;

    [ObservableProperty]
    private string? recordedAudioId;

    [ObservableProperty]
    private string? recordedAudioUrl;

    [ObservableProperty]
    private ObservableCollection<string> availableDevices = new();

    [ObservableProperty]
    private ObservableCollection<float> waveformSamples = new();

    [ObservableProperty]
    private string selectedFormat = "wav";

    [ObservableProperty]
    private ObservableCollection<string> availableFormats = new() { "wav", "mp3", "flac", "ogg" };

    public ObservableCollection<int> AvailableSampleRates { get; } = new()
        {
            44100,
            48000,
            96000
        };

    public ObservableCollection<int> AvailableChannels { get; } = new()
        {
            1,  // Mono
            2   // Stereo
        };

    public ObservableCollection<int> AvailableBitDepths { get; } = new()
        {
            16,
            24
        };

    public RecordingViewModel(
        IViewModelContext context,
        IRecordingClient recordingClient,
        IProjectAudioClient projectAudioClient,
        IAudioPlayerService audioPlayer)
        : base(context)
    {
      _recordingClient = recordingClient ?? throw new ArgumentNullException(nameof(recordingClient));
      _projectAudioClient = projectAudioClient ?? throw new ArgumentNullException(nameof(projectAudioClient));
      _audioPlayer = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));

      // Get services using helper (reduces code duplication)
      _toastNotificationService = ServiceInitializationHelper.TryGetService(() => AppServices.TryGetToastNotificationService());

      // Get error services
      _errorService = ServiceProvider.TryGetErrorPresentationService();
      _logService = ServiceProvider.TryGetErrorLoggingService();

      // Initialize local microphone recording service (NAudio)
      _microphoneService = new MicrophoneRecordingService();
      _microphoneService.RecordingStarted += MicrophoneService_RecordingStarted;
      _microphoneService.RecordingStopped += MicrophoneService_RecordingStopped;
      _microphoneService.LevelChanged += MicrophoneService_LevelChanged;
      _microphoneService.RecordingError += MicrophoneService_RecordingError;

      _statusTimer = Dispatcher.CreateTimer();
      _statusTimer.Interval = TimeSpan.FromMilliseconds(100);
      _statusTimer.IsRepeating = true;
      _statusTimer.Tick += StatusTimer_Tick;

      StartRecordingCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("StartRecording");
        await StartRecordingAsync(ct);
      }, () => !IsRecording);

      StopRecordingCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("StopRecording");
        await StopRecordingAsync(ct);
      }, () => IsRecording);

      CancelRecordingCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CancelRecording");
        await CancelRecordingAsync(ct);
      }, () => IsRecording);

      LoadDevicesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadDevices");
        await LoadDevicesAsync(ct);
      });

      PlayCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Play");
        await PlayRecordedAsync(ct);
      }, () => (!string.IsNullOrEmpty(RecordedAudioId) || !string.IsNullOrEmpty(RecordedAudioUrl)) && !IsLoading);

      PropertyChanged += (_, e) =>
      {
        if (e.PropertyName is nameof(RecordedAudioId) or nameof(RecordedAudioUrl) or nameof(IsLoading))
          PlayCommand.NotifyCanExecuteChanged();
      };
    }

    public async Task OnActivatedAsync(CancellationToken cancellationToken = default)
    {
      SyncProjectFromContext();
      EnsureProjectChangedSubscription();
      await LoadDevicesAsync(cancellationToken);
    }

    public Task OnDeactivatedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>Pass 05 C1: align with active project from <see cref="IContextManager"/>.</summary>
    private void SyncProjectFromContext()
    {
      var ctx = AppServices.TryGetContextManager();
      if (ctx == null)
        return;
      var activeId = ctx.ActiveProjectId;
      if (ProjectId != activeId)
        ProjectId = activeId;
    }

    private void EnsureProjectChangedSubscription()
    {
      if (_projectChangedToken != null)
        return;
      var agg = AppServices.TryGetEventAggregator();
      if (agg == null)
        return;
      _projectChangedToken = agg.Subscribe<ProjectChangedEvent>(OnProjectChanged);
    }

    private void OnProjectChanged(ProjectChangedEvent e)
    {
      Dispatcher.TryEnqueue(() =>
      {
        if (ProjectId != e.ProjectId)
          ProjectId = e.ProjectId;
      });
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) => LoadDevicesAsync(cancellationToken);

    public EnhancedAsyncRelayCommand StartRecordingCommand { get; }
    public EnhancedAsyncRelayCommand StopRecordingCommand { get; }
    public EnhancedAsyncRelayCommand CancelRecordingCommand { get; }
    public EnhancedAsyncRelayCommand LoadDevicesCommand { get; }
    public EnhancedAsyncRelayCommand PlayCommand { get; }

    private async Task StartRecordingAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        // Generate a unique recording ID
        RecordingId = $"rec_{Guid.NewGuid():N}"[..16];
        
        // Build output path if filename specified
        string? outputPath = null;
        if (!string.IsNullOrWhiteSpace(Filename))
        {
          var tempDir = System.IO.Path.GetTempPath();
          outputPath = System.IO.Path.Combine(tempDir, $"{Filename}.wav");
        }

        // Use local NAudio-based microphone recording
        await _microphoneService.StartRecordingAsync(outputPath, SampleRate, Channels);

        IsRecording = true;
        RecordingDuration = TimeSpan.Zero;
        RecordedAudioId = null;
        RecordedAudioUrl = null;

        // Start status polling for duration updates
        _statusTimer.Start();

        StatusMessage = ResourceHelper.GetString("Recording.RecordingStarted", "Recording started");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.FormatString("Recording.RecordingStartedDetail", SampleRate, Channels),
            ResourceHelper.GetString("Toast.Title.RecordingStarted", "Recording Started"));
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        var errorMsg = ErrorHandler.GetUserFriendlyMessage(ex);
        ErrorMessage = ResourceHelper.FormatString("Recording.StartRecordingFailed", errorMsg);
        _errorService?.ShowError(ex, ResourceHelper.GetString("Recording.StartRecordingFailedTitle", "Failed to start recording"));
        _logService?.LogError(ex, "StartRecording");
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("Recording.StartRecordingFailed", errorMsg),
            ResourceHelper.GetString("Toast.Title.StartFailed", "Start Failed"));
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task StopRecordingAsync(CancellationToken cancellationToken)
    {
      if (!_microphoneService.IsRecording)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        _statusTimer.Stop();

        // Stop local microphone recording
        var recordingPath = await _microphoneService.StopRecordingAsync();

        IsRecording = false;
        RecordingDuration = _microphoneService.Duration;

        // If there's a recording file, optionally upload to backend library
        if (!string.IsNullOrEmpty(recordingPath) && System.IO.File.Exists(recordingPath))
        {
          try
          {
            // Upload the recorded file to backend
            var uploadResult = await _recordingClient.UploadAudioFileAsync(recordingPath);
            await ApplyPostLibraryUploadSuccessAsync(uploadResult, recordingPath, cancellationToken).ConfigureAwait(false);
          }
          catch (Exception uploadEx)
          {
            // Upload failure is non-critical; recording still succeeded locally
            _logService?.LogError(uploadEx, "UploadRecording");
            RecordedAudioUrl = recordingPath;  // Use local path as fallback
          }
        }

        StatusMessage = ResourceHelper.FormatString("Recording.RecordingStopped", RecordingDuration.TotalSeconds);
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.FormatString("Recording.RecordingStoppedDetail", RecordingDuration.TotalSeconds),
            ResourceHelper.GetString("Toast.Title.RecordingComplete", "Recording Complete"));
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        var errorMsg = ErrorHandler.GetUserFriendlyMessage(ex);
        ErrorMessage = ResourceHelper.FormatString("Recording.StopRecordingFailed", errorMsg);
        _errorService?.ShowError(ex, ResourceHelper.GetString("Recording.StopRecordingFailedTitle", "Failed to stop recording"));
        _logService?.LogError(ex, "StopRecording");
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("Recording.StopRecordingFailed", errorMsg),
            ResourceHelper.GetString("Toast.Title.StopFailed", "Stop Failed"));
      }
      finally
      {
        IsLoading = false;
      }
    }

    /// <summary>
    /// Pass 05 Option C: seam-tested wiring after a recording file is uploaded to the library (IDs, project save, transport, <see cref="AssetAddedEvent"/>).
    /// Matches the success path inside <see cref="StopRecordingAsync"/> after <see cref="IRecordingClient.UploadAudioFileAsync"/>.
    /// Public for seam tests (WinUI project may not emit <c>InternalsVisibleTo</c> to test assembly reliably).
    /// </summary>
    public async Task ApplyPostLibraryUploadSuccessAsync(
        AudioUploadResponse uploadResult,
        string recordingPath,
        CancellationToken cancellationToken)
    {
      ArgumentNullException.ThrowIfNull(uploadResult);
      RecordedAudioId = uploadResult.Id;
      RecordedAudioUrl = uploadResult.Path;

      await RecordingToProjectPersistence.TrySaveAfterUploadAsync(
          _projectAudioClient,
          _logService,
          ProjectId,
          uploadResult.Id,
          recordingPath,
          cancellationToken).ConfigureAwait(false);

      var ctx = AppServices.TryGetContextManager();
      if (ctx != null)
        ctx.SetCurrentPlayable(uploadResult.Id, TransportSource.Recording, "Recording");

      var eventAggregator = AppServices.TryGetEventAggregator();
      eventAggregator?.Publish(new AssetAddedEvent(
          "recording-panel",
          uploadResult.Id,
          "audio",
          recordingPath));
    }

    private async Task CancelRecordingAsync(CancellationToken cancellationToken)
    {
      if (!_microphoneService.IsRecording)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        _statusTimer.Stop();

        // Stop local microphone recording and discard the file
        var recordingPath = await _microphoneService.StopRecordingAsync();
        
        // Delete the temporary recording file since user cancelled
        if (!string.IsNullOrEmpty(recordingPath) && System.IO.File.Exists(recordingPath))
        {
          try
          {
            System.IO.File.Delete(recordingPath);
          }
          catch (Exception deleteEx)
          {
            System.Diagnostics.Debug.WriteLine($"Failed to delete cancelled recording: {deleteEx.Message}");
          }
        }

        RecordingId = null;
        IsRecording = false;
        RecordingDuration = TimeSpan.Zero;
        RecordedAudioId = null;
        RecordedAudioUrl = null;

        StatusMessage = ResourceHelper.GetString("Recording.RecordingCancelled", "Recording cancelled");
        _toastNotificationService?.ShowWarning(
            ResourceHelper.GetString("Recording.RecordingCancelled", "Recording cancelled"),
            ResourceHelper.GetString("Toast.Title.RecordingCancelled", "Recording Cancelled"));
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        var errorMsg = ErrorHandler.GetUserFriendlyMessage(ex);
        ErrorMessage = ResourceHelper.FormatString("Recording.CancelRecordingFailed", errorMsg);
        _errorService?.ShowError(ex, ResourceHelper.GetString("Recording.CancelRecordingFailedTitle", "Failed to cancel recording"));
        _logService?.LogError(ex, "CancelRecording");
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("Recording.CancelRecordingFailed", errorMsg),
            ResourceHelper.GetString("Toast.Title.CancelFailed", "Cancel Failed"));
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task PlayRecordedAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(RecordedAudioId) && string.IsNullOrEmpty(RecordedAudioUrl))
        return;

      // Own global transport so main Play routes here
      if (!string.IsNullOrEmpty(RecordedAudioId))
      {
        var ctx = AppServices.TryGetContextManager();
        if (ctx != null)
          ctx.SetCurrentPlayable(RecordedAudioId, TransportSource.Recording, "Recording");
      }

      try
      {
        if (!string.IsNullOrEmpty(RecordedAudioId))
        {
          var baseUrl = BackendPlaybackBaseUrl.Resolve(AppServices.GetService<BackendClientConfig>());
          await _audioPlayer.PlayBackendAudioIdAsync(RecordedAudioId, baseUrl);
        }
        else if (!string.IsNullOrEmpty(RecordedAudioUrl))
        {
          if (RecordedAudioUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
              || RecordedAudioUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
          {
            await _audioPlayer.PlayUrlAsync(RecordedAudioUrl);
          }
          else if (System.IO.File.Exists(RecordedAudioUrl))
          {
            await _audioPlayer.PlayFileAsync(RecordedAudioUrl);
          }
        }
      }
      catch (Exception ex)
      {
        _logService?.LogError(ex, "PlayRecorded");
        _toastNotificationService?.ShowError(
            ErrorHandler.GetUserFriendlyMessage(ex),
            ResourceHelper.GetString("Toast.Title.PlaybackFailed", "Playback Failed"));
      }
    }

    private async Task LoadDevicesAsync(CancellationToken cancellationToken)
    {
      try
      {
        var response = await _recordingClient.GetRecordingDevicesAsync(cancellationToken);

        AvailableDevices.Clear();
        if (response?.Devices != null)
        {
          foreach (var device in response.Devices)
          {
            AvailableDevices.Add(device.Name);
          }
        }

        if (AvailableDevices.Count > 0 && string.IsNullOrEmpty(SelectedDevice))
        {
          SelectedDevice = AvailableDevices[0];
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        var errorMsg = ErrorHandler.GetUserFriendlyMessage(ex);
        ErrorMessage = ResourceHelper.FormatString("Recording.LoadDevicesFailed", errorMsg);
        _errorService?.ShowError(ex, ResourceHelper.GetString("Recording.LoadDevicesFailedTitle", "Failed to load recording devices"));
        _logService?.LogError(ex, "LoadDevices");
      }
    }

    private void StatusTimer_Tick(object? sender, object? args)
    {
      if (!_microphoneService.IsRecording)
        return;

      // Update duration from local microphone service
      RecordingDuration = _microphoneService.Duration;
      RecordingDurationDisplay = RecordingDuration.ToString(@"mm\:ss");
    }

    #region Microphone Service Event Handlers

    private void MicrophoneService_RecordingStarted(object? sender, EventArgs e)
    {
      // Ensure UI updates happen on dispatcher thread
      Dispatcher.TryEnqueue(() =>
      {
        IsRecording = true;
        StatusMessage = ResourceHelper.GetString("Recording.RecordingStarted", "Recording started");
      });
    }

    private void MicrophoneService_RecordingStopped(object? sender, RecordingCompletedEventArgs e)
    {
      Dispatcher.TryEnqueue(() =>
      {
        IsRecording = false;
        _statusTimer.Stop();
        RecordingDuration = e.Duration;
        RecordingDurationDisplay = e.Duration.ToString(@"mm\:ss");
        
        if (!string.IsNullOrEmpty(e.FilePath))
        {
          RecordedAudioUrl = e.FilePath;
        }
      });
    }

    private void MicrophoneService_LevelChanged(object? sender, float level)
    {
      // Update VU meter / waveform on the UI thread
      Dispatcher.TryEnqueue(() =>
      {
        // Add the level to the waveform samples for visualization
        // Keep only the last 100 samples for performance
        if (WaveformSamples.Count >= 100)
        {
          WaveformSamples.RemoveAt(0);
        }
        WaveformSamples.Add(level);
      });
    }

    private void MicrophoneService_RecordingError(object? sender, string errorMessage)
    {
      Dispatcher.TryEnqueue(() =>
      {
        IsRecording = false;
        _statusTimer.Stop();
        
        ErrorMessage = ResourceHelper.FormatString("Recording.RecordingError", errorMessage);
        _toastNotificationService?.ShowError(
            errorMessage,
            ResourceHelper.GetString("Toast.Title.RecordingError", "Recording Error"));
      });
    }

    #endregion

    // Response models
    private class RecordingStartResponse
    {
      public string RecordingId { get; set; } = string.Empty;
      public bool IsRecording { get; set; }
      public double Duration { get; set; }
      public int SampleRate { get; set; }
      public int Channels { get; set; }
      public int BitDepth { get; set; }
    }

    private class RecordingStatusResponse
    {
      public string RecordingId { get; set; } = string.Empty;
      public bool IsRecording { get; set; }
      public double Duration { get; set; }
      public float[]? WaveformSamples { get; set; }
    }

    private class RecordingStopResponse
    {
      public string RecordingId { get; set; } = string.Empty;
      public string AudioId { get; set; } = string.Empty;
      public string AudioUrl { get; set; } = string.Empty;
      public double Duration { get; set; }
    }

    /// <summary>
    /// Notify commands when IsRecording changes to update their CanExecute state.
    /// </summary>
    partial void OnIsRecordingChanged(bool value)
    {
      StartRecordingCommand.NotifyCanExecuteChanged();
      StopRecordingCommand.NotifyCanExecuteChanged();
      CancelRecordingCommand.NotifyCanExecuteChanged();
    }

    protected override void Dispose(bool disposing)
    {
      if (disposing)
      {
        if (_projectChangedToken != null)
        {
          AppServices.TryGetEventAggregator()?.Unsubscribe(_projectChangedToken);
          _projectChangedToken = null;
        }

        _statusTimer.Stop();
        _statusTimer.Tick -= StatusTimer_Tick;

        // Unsubscribe from microphone service events
        _microphoneService.RecordingStarted -= MicrophoneService_RecordingStarted;
        _microphoneService.RecordingStopped -= MicrophoneService_RecordingStopped;
        _microphoneService.LevelChanged -= MicrophoneService_LevelChanged;
        _microphoneService.RecordingError -= MicrophoneService_RecordingError;
        _microphoneService.Dispose();
      }
      base.Dispose(disposing);
    }
  }
}