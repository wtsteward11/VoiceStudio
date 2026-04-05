using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
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
    private readonly IRecordingInputCommandState? _recordingInputCommandState;
    private readonly IRecordingDeviceAvailabilityService? _recordingDeviceAvailability;
    private readonly ToastNotificationService? _toastNotificationService;
    private readonly IErrorPresentationService? _errorService;
    private readonly IErrorLoggingService? _logService;
    private readonly IDispatcherTimer _statusTimer;
    private readonly MicrophoneRecordingService _microphoneService;
    private readonly IRecordingSessionCoordinator? _recordingSessionCoordinator;
    private readonly IRecordingCaptureFanoutService? _recordingCaptureFanout;
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
    private RecordingDevice? selectedInputDevice;

    [ObservableProperty]
    private string? filename;

    [ObservableProperty]
    private string? projectId;

    [ObservableProperty]
    private string? recordedAudioId;

    [ObservableProperty]
    private string? recordedAudioUrl;

    [ObservableProperty]
    private ObservableCollection<RecordingDevice> availableInputDevices = new();

    [ObservableProperty]
    private ObservableCollection<float> waveformSamples = new();

    [ObservableProperty]
    private string selectedFormat = "wav";

    [ObservableProperty]
    private ObservableCollection<string> availableFormats = new() { "wav", "mp3", "flac", "ogg" };

    [ObservableProperty]
    private bool isSessionOutcomeBarOpen;

    [ObservableProperty]
    private string sessionOutcomeTitle = string.Empty;

    [ObservableProperty]
    private string sessionOutcomeMessage = string.Empty;

    [ObservableProperty]
    private InfoBarSeverity sessionOutcomeSeverity = InfoBarSeverity.Informational;

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
        IAudioPlayerService audioPlayer,
        IRecordingSessionCoordinator? recordingSessionCoordinator = null,
        IRecordingCaptureFanoutService? recordingCaptureFanout = null,
        IRecordingInputCommandState? recordingInputCommandState = null,
        IRecordingDeviceAvailabilityService? recordingDeviceAvailability = null)
        : base(context)
    {
      _recordingClient = recordingClient ?? throw new ArgumentNullException(nameof(recordingClient));
      _projectAudioClient = projectAudioClient ?? throw new ArgumentNullException(nameof(projectAudioClient));
      _audioPlayer = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));
      _recordingSessionCoordinator = recordingSessionCoordinator;
      _recordingCaptureFanout = recordingCaptureFanout;
      _recordingInputCommandState = recordingInputCommandState;
      _recordingDeviceAvailability = recordingDeviceAvailability;

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

      if (_recordingCaptureFanout != null)
      {
        _recordingCaptureFanout.AggregateLevelChanged += FanoutAggregateLevelChanged;
        _recordingCaptureFanout.CaptureSessionFaulted += FanoutCaptureSessionFaulted;
      }

      if (_recordingDeviceAvailability != null)
        _recordingDeviceAvailability.InputDevicesChanged += OnRecordingHardwareDevicesChanged;

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

      ArmForMultitrackCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ArmMultitrack");
        await ArmCurrentTargetAsync(ct);
      }, () => !IsRecording && _recordingSessionCoordinator != null && _recordingCaptureFanout != null);

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
    public EnhancedAsyncRelayCommand ArmForMultitrackCommand { get; }
    public EnhancedAsyncRelayCommand LoadDevicesCommand { get; }
    public EnhancedAsyncRelayCommand PlayCommand { get; }

    private void OnRecordingHardwareDevicesChanged(object? sender, EventArgs e)
    {
      Dispatcher.TryEnqueue(() =>
      {
        _ = OnRecordingHardwareDevicesChangedAsync();
      });
    }

    private async Task OnRecordingHardwareDevicesChangedAsync()
    {
      try
      {
        await LoadDevicesAsync(CancellationToken.None).ConfigureAwait(true);
        await EvaluatePreparedArmAssignmentsAsync(CancellationToken.None).ConfigureAwait(true);
      }
      catch (Exception ex)
      {
        _logService?.LogError(ex, "RecordingDeviceChurnUi");
      }
    }

    /// <summary>GAP-035: surface stale arms when devices churn in Prepared phase.</summary>
    private async Task EvaluatePreparedArmAssignmentsAsync(CancellationToken cancellationToken)
    {
      if (_recordingSessionCoordinator == null)
        return;
      if (_recordingSessionCoordinator.Phase != VoiceStudio.Core.Recording.MultitrackRecordingSessionPhase.Prepared)
        return;
      if (_recordingSessionCoordinator.TrackInputAssignments.Count == 0)
        return;

      foreach (var kv in _recordingSessionCoordinator.TrackInputAssignments)
      {
        var (ok, _, err) = await RecordingInputDeviceResolver
            .TryResolveAsync(_recordingClient, _recordingDeviceAvailability, kv.Value, cancellationToken)
            .ConfigureAwait(true);
        if (!ok)
        {
          StatusMessage = err ?? $"Input for track '{kv.Key}' is not available.";
          ErrorMessage = ResourceHelper.GetString("Recording.ArmStaleAfterDeviceChange", "An armed track points to a microphone that is no longer available. Disarm or pick another device.");
          return;
        }
      }

      ErrorMessage = null;
    }

    private async Task StartRecordingAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;
      ClearSessionOutcomeUi();

      try
      {
        SyncProjectFromContext();

        if (_recordingDeviceAvailability != null)
          await _recordingDeviceAvailability.RefreshAsync(cancellationToken).ConfigureAwait(true);

        if (_recordingSessionCoordinator != null && _recordingCaptureFanout != null)
        {
          var ctx = AppServices.TryGetContextManager();
          var trackSvc = AppServices.TryGetTimelineTrackService();

          var (trackOk, targetTrackId, trackErr) = await RecordingTrackTargetResolver
              .ResolveRecordableTrackAsync(ProjectId, ctx, trackSvc, cancellationToken)
              .ConfigureAwait(true);
          if (!trackOk)
          {
            ErrorMessage = trackErr ?? "Cannot resolve recording target track.";
            _toastNotificationService?.ShowError(
                ErrorMessage,
                ResourceHelper.GetString("Toast.Title.StartFailed", "Start Failed"));
            return;
          }

          var inputId = SelectedInputDevice?.Id;
          if (string.IsNullOrWhiteSpace(inputId))
          {
            ErrorMessage = ResourceHelper.GetString("Recording.SelectInputDevice", "Select an input device.");
            _toastNotificationService?.ShowError(
                ErrorMessage,
                ResourceHelper.GetString("Toast.Title.StartFailed", "Start Failed"));
            return;
          }

          if (!RecordingSessionLifecycleGate.TryPrepareRecordingSessionShell(
                  _recordingSessionCoordinator,
                  ProjectId,
                  out var shellError))
          {
            ErrorMessage = shellError ?? "Cannot prepare recording session.";
            _toastNotificationService?.ShowError(
                ErrorMessage,
                ResourceHelper.GetString("Toast.Title.StartFailed", "Start Failed"));
            return;
          }

          if (!_recordingSessionCoordinator.TryArmTrack(targetTrackId!, inputId, out var armError))
          {
            ErrorMessage = armError ?? "Cannot arm recording track.";
            _toastNotificationService?.ShowError(
                ErrorMessage,
                ResourceHelper.GetString("Toast.Title.StartFailed", "Start Failed"));
            return;
          }

          var assignmentSnapshot = new Dictionary<string, string>(
              _recordingSessionCoordinator.TrackInputAssignments,
              StringComparer.Ordinal);
          var plan = await _recordingCaptureFanout.ValidateAndBuildPlanAsync(
                  assignmentSnapshot,
                  SampleRate,
                  Channels,
                  Filename,
                  cancellationToken)
              .ConfigureAwait(true);
          if (!plan.Success)
          {
            ErrorMessage = plan.ErrorMessage ?? "Cannot validate capture devices.";
            _toastNotificationService?.ShowError(
                ErrorMessage,
                ResourceHelper.GetString("Toast.Title.StartFailed", "Start Failed"));
            return;
          }

          if (!_recordingSessionCoordinator.TryStartRecording(out var startErr))
          {
            ErrorMessage = startErr ?? "Cannot enter recording phase.";
            _toastNotificationService?.ShowError(
                ErrorMessage,
                ResourceHelper.GetString("Toast.Title.StartFailed", "Start Failed"));
            return;
          }

          var started = await _recordingCaptureFanout.StartLegsAsync(plan, SampleRate, Channels, cancellationToken)
              .ConfigureAwait(true);
          if (!started.Success)
          {
            RecordingSessionLifecycleGate.NotifyCaptureStartFailed(_recordingSessionCoordinator);
            ErrorMessage = started.ErrorMessage ?? "Failed to start capture.";
            _toastNotificationService?.ShowError(
                ErrorMessage,
                ResourceHelper.GetString("Toast.Title.StartFailed", "Start Failed"));
            return;
          }

          RecordingId = $"rec_{Guid.NewGuid():N}"[..16];
          IsRecording = true;
          RecordingDuration = TimeSpan.Zero;
          RecordedAudioId = null;
          RecordedAudioUrl = null;
          _statusTimer.Start();
          StatusMessage = ResourceHelper.GetString("Recording.RecordingStarted", "Recording started");
          var legCount = assignmentSnapshot.Count;
          _toastNotificationService?.ShowSuccess(
              legCount > 1
                  ? $"Recording started ({legCount} tracks)."
                  : ResourceHelper.FormatString("Recording.RecordingStartedDetail", SampleRate, Channels),
              ResourceHelper.GetString("Toast.Title.RecordingStarted", "Recording Started"));
          return;
        }

        if (_recordingSessionCoordinator != null)
        {
          var ctx = AppServices.TryGetContextManager();
          var trackSvc = AppServices.TryGetTimelineTrackService();

          var (trackOk, targetTrackId, trackErr) = await RecordingTrackTargetResolver
              .ResolveRecordableTrackAsync(ProjectId, ctx, trackSvc, cancellationToken)
              .ConfigureAwait(true);
          if (!trackOk)
          {
            ErrorMessage = trackErr ?? "Cannot resolve recording target track.";
            _toastNotificationService?.ShowError(
                ErrorMessage,
                ResourceHelper.GetString("Toast.Title.StartFailed", "Start Failed"));
            return;
          }

          var inputId = SelectedInputDevice?.Id;
          if (string.IsNullOrWhiteSpace(inputId))
          {
            ErrorMessage = ResourceHelper.GetString("Recording.SelectInputDevice", "Select an input device.");
            _toastNotificationService?.ShowError(
                ErrorMessage,
                ResourceHelper.GetString("Toast.Title.StartFailed", "Start Failed"));
            return;
          }

          if (!RecordingSessionLifecycleGate.TryPrepareAndStartRecording(
                  _recordingSessionCoordinator,
                  ProjectId,
                  targetTrackId!,
                  inputId,
                  out var gateError))
          {
            ErrorMessage = gateError ?? "Cannot start recording.";
            _toastNotificationService?.ShowError(
                ErrorMessage,
                ResourceHelper.GetString("Toast.Title.StartFailed", "Start Failed"));
            return;
          }

          var (resOk, deviceNumber, resErr) = await RecordingInputDeviceResolver.TryResolveAsync(
                  _recordingClient,
                  _recordingDeviceAvailability,
                  inputId,
                  cancellationToken)
              .ConfigureAwait(true);
          if (!resOk)
          {
            RecordingSessionLifecycleGate.NotifyCaptureStartFailed(_recordingSessionCoordinator);
            ErrorMessage = resErr ?? "Cannot resolve input device.";
            _toastNotificationService?.ShowError(
                ErrorMessage,
                ResourceHelper.GetString("Toast.Title.StartFailed", "Start Failed"));
            return;
          }

          RecordingId = $"rec_{Guid.NewGuid():N}"[..16];
          string? outputPath = null;
          if (!string.IsNullOrWhiteSpace(Filename))
          {
            var tempDir = Path.GetTempPath();
            outputPath = Path.Combine(tempDir, $"{Filename}.wav");
          }

          await _microphoneService.StartRecordingAsync(outputPath, SampleRate, Channels, deviceNumber)
              .ConfigureAwait(true);
          IsRecording = true;
          RecordingDuration = TimeSpan.Zero;
          RecordedAudioId = null;
          RecordedAudioUrl = null;
          _statusTimer.Start();
          StatusMessage = ResourceHelper.GetString("Recording.RecordingStarted", "Recording started");
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("Recording.RecordingStartedDetail", SampleRate, Channels),
              ResourceHelper.GetString("Toast.Title.RecordingStarted", "Recording Started"));
          return;
        }

        RecordingId = $"rec_{Guid.NewGuid():N}"[..16];

        string? legacyOutputPath = null;
        if (!string.IsNullOrWhiteSpace(Filename))
        {
          var tempDir = Path.GetTempPath();
          legacyOutputPath = Path.Combine(tempDir, $"{Filename}.wav");
        }

        await _microphoneService.StartRecordingAsync(legacyOutputPath, SampleRate, Channels).ConfigureAwait(true);

        IsRecording = true;
        RecordingDuration = TimeSpan.Zero;
        RecordedAudioId = null;
        RecordedAudioUrl = null;

        _statusTimer.Start();

        StatusMessage = ResourceHelper.GetString("Recording.RecordingStarted", "Recording started");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.FormatString("Recording.RecordingStartedDetail", SampleRate, Channels),
            ResourceHelper.GetString("Toast.Title.RecordingStarted", "Recording Started"));
      }
      catch (OperationCanceledException)
      {
        RecordingSessionLifecycleGate.NotifyCaptureStartFailed(_recordingSessionCoordinator);
        return;
      }
      catch (Exception ex)
      {
        RecordingSessionLifecycleGate.NotifyCaptureStartFailed(_recordingSessionCoordinator);
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

    private async Task ArmCurrentTargetAsync(CancellationToken cancellationToken)
    {
      if (_recordingSessionCoordinator == null || _recordingCaptureFanout == null)
        return;

      IsLoading = true;
      ErrorMessage = null;
      try
      {
        SyncProjectFromContext();
        var ctx = AppServices.TryGetContextManager();
        var trackSvc = AppServices.TryGetTimelineTrackService();
        var (trackOk, targetTrackId, trackErr) = await RecordingTrackTargetResolver
            .ResolveRecordableTrackAsync(ProjectId, ctx, trackSvc, cancellationToken)
            .ConfigureAwait(true);
        if (!trackOk)
        {
          ErrorMessage = trackErr ?? "Cannot resolve recording target track.";
          _toastNotificationService?.ShowError(
              ErrorMessage,
              ResourceHelper.GetString("Toast.Title.StartFailed", "Start Failed"));
          return;
        }

        var inputId = SelectedInputDevice?.Id;
        if (string.IsNullOrWhiteSpace(inputId))
        {
          ErrorMessage = ResourceHelper.GetString("Recording.SelectInputDevice", "Select an input device.");
          _toastNotificationService?.ShowError(
              ErrorMessage,
              ResourceHelper.GetString("Toast.Title.StartFailed", "Start Failed"));
          return;
        }

        if (!RecordingSessionLifecycleGate.TryPrepareRecordingSessionShell(
                _recordingSessionCoordinator,
                ProjectId,
                out var shellError))
        {
          ErrorMessage = shellError ?? "Cannot prepare recording session.";
          _toastNotificationService?.ShowError(
              ErrorMessage,
              ResourceHelper.GetString("Toast.Title.StartFailed", "Start Failed"));
          return;
        }

        var (preArmOk, _, preArmErr) = await RecordingInputDeviceResolver.TryResolveAsync(
                _recordingClient,
                _recordingDeviceAvailability,
                inputId,
                cancellationToken)
            .ConfigureAwait(true);
        if (!preArmOk)
        {
          ErrorMessage = preArmErr ?? "Microphone is not available.";
          _toastNotificationService?.ShowError(
              ErrorMessage,
              ResourceHelper.GetString("Toast.Title.StartFailed", "Start Failed"));
          return;
        }

        if (!_recordingSessionCoordinator.TryArmTrack(targetTrackId!, inputId, out var armError))
        {
          ErrorMessage = armError ?? "Cannot arm track.";
          _toastNotificationService?.ShowError(
              ErrorMessage,
              ResourceHelper.GetString("Toast.Title.StartFailed", "Start Failed"));
          return;
        }

        var count = _recordingSessionCoordinator.ArmedTrackIds.Count;
        _toastNotificationService?.ShowSuccess(
            $"Armed {count} track(s). Select another timeline track and device, arm again, then Record.",
            "Recording");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task StopRecordingAsync(CancellationToken cancellationToken)
    {
      if (_recordingCaptureFanout != null
          && _recordingSessionCoordinator != null
          && _recordingCaptureFanout.IsActive)
      {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
          _statusTimer.Stop();
          RecordingDuration = _recordingCaptureFanout.MaxLegDuration;
          var assignmentSnapshot = new Dictionary<string, string>(
              _recordingSessionCoordinator.TrackInputAssignments,
              StringComparer.Ordinal);
          var sessionIdSnapshot = _recordingSessionCoordinator.ActiveSessionId;
          var stopResult = await _recordingCaptureFanout.StopAllAsync(cancellationToken).ConfigureAwait(true);
          IsRecording = false;
          RecordingSessionLifecycleGate.NotifyCaptureStopped(_recordingSessionCoordinator);

          string? lastLibraryId = null;
          foreach (var leg in stopResult.Legs)
          {
            if (!leg.CompletedSuccessfully
                || string.IsNullOrWhiteSpace(leg.LocalPath)
                || !File.Exists(leg.LocalPath))
            {
              continue;
            }

            try
            {
              var uploadResult = await _recordingClient.UploadAudioFileAsync(leg.LocalPath).ConfigureAwait(true);
              var hintName = string.IsNullOrWhiteSpace(Filename)
                  ? $"{leg.TrackId}_{Path.GetFileName(leg.LocalPath)}"
                  : $"{Filename}_{leg.TrackId}{Path.GetExtension(leg.LocalPath)}";
              await ApplyPostLibraryUploadSuccessAsync(
                  uploadResult,
                  leg.LocalPath,
                  cancellationToken,
                  hintName).ConfigureAwait(true);
              lastLibraryId = uploadResult.Id;
            }
            catch (Exception uploadEx)
            {
              _logService?.LogError(uploadEx, "UploadRecordingMultitrack");
            }
          }

          if (!string.IsNullOrEmpty(lastLibraryId))
            RecordedAudioId = lastLibraryId;

          StatusMessage = ResourceHelper.FormatString("Recording.RecordingStopped", RecordingDuration.TotalSeconds);
          ApplyMultitrackSessionOutcomeUi(stopResult, userInvokedStop: true);
          await PersistMultitrackRecoveryAsync(
                  assignmentSnapshot,
                  sessionIdSnapshot,
                  stopResult,
                  userInvokedCleanStop: true)
              .ConfigureAwait(true);
          if (stopResult.SessionFaulted)
          {
            ErrorMessage = SessionOutcomeMessage;
            _toastNotificationService?.ShowWarning(
                ErrorMessage,
                ResourceHelper.GetString("Toast.Title.RecordingComplete", "Recording Complete"));
          }
          else
          {
            _toastNotificationService?.ShowSuccess(
                ResourceHelper.FormatString("Recording.RecordingStoppedDetail", RecordingDuration.TotalSeconds),
                ResourceHelper.GetString("Toast.Title.RecordingComplete", "Recording Complete"));
          }
        }
        finally
        {
          IsLoading = false;
        }

        return;
      }

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
        RecordingSessionLifecycleGate.NotifyCaptureStopped(_recordingSessionCoordinator);
        RecordingDuration = _microphoneService.Duration;

        // If there's a recording file, optionally upload to backend library
        if (!string.IsNullOrEmpty(recordingPath) && File.Exists(recordingPath))
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
        RecordingSessionLifecycleGate.NotifyCaptureStartFailed(_recordingSessionCoordinator);
        return;
      }
      catch (Exception ex)
      {
        RecordingSessionLifecycleGate.NotifyCaptureStartFailed(_recordingSessionCoordinator);
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
        CancellationToken cancellationToken,
        string? projectSaveFilenameHint = null)
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
          cancellationToken,
          projectSaveFilenameHint).ConfigureAwait(false);

      var ctx = AppServices.TryGetContextManager();
      if (ctx != null)
        ctx.SetCurrentPlayable(uploadResult.Id, TransportSource.Recording, "Recording");

      var eventAggregator = AppServices.TryGetEventAggregator();
      // GAP-027: canonical panel id so Library can focus assets from Recording uploads
      eventAggregator?.Publish(new AssetAddedEvent(
          PanelIds.Recording,
          uploadResult.Id,
          "audio",
          recordingPath));
    }

    private async Task CancelRecordingAsync(CancellationToken cancellationToken)
    {
      if (_recordingCaptureFanout != null
          && _recordingSessionCoordinator != null
          && _recordingCaptureFanout.IsActive)
      {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
          _statusTimer.Stop();
          await _recordingCaptureFanout.CancelAllAsync().ConfigureAwait(true);
          RecordingId = null;
          IsRecording = false;
          RecordingDuration = TimeSpan.Zero;
          RecordedAudioId = null;
          RecordedAudioUrl = null;
          RecordingSessionLifecycleGate.NotifyCaptureCancelled(_recordingSessionCoordinator);
          StatusMessage = ResourceHelper.GetString("Recording.RecordingCancelled", "Recording cancelled");
          _toastNotificationService?.ShowWarning(
              ResourceHelper.GetString("Recording.RecordingCancelled", "Recording cancelled"),
              ResourceHelper.GetString("Toast.Title.RecordingCancelled", "Recording Cancelled"));
          ClearSessionOutcomeUi();
          try
          {
            var recoveryCancel = AppServices.GetService<IMultitrackRecoveryStateService>();
            if (recoveryCancel != null)
              await recoveryCancel.ClearPendingAndSaveAsync().ConfigureAwait(true);
          }
          catch (Exception ex)
          {
            _logService?.LogError(ex, "MultitrackRecoveryClearOnCancel");
          }
        }
        finally
        {
          IsLoading = false;
        }

        return;
      }

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

        RecordingSessionLifecycleGate.NotifyCaptureCancelled(_recordingSessionCoordinator);

        StatusMessage = ResourceHelper.GetString("Recording.RecordingCancelled", "Recording cancelled");
        _toastNotificationService?.ShowWarning(
            ResourceHelper.GetString("Recording.RecordingCancelled", "Recording cancelled"),
            ResourceHelper.GetString("Toast.Title.RecordingCancelled", "Recording Cancelled"));
      }
      catch (OperationCanceledException)
      {
        RecordingSessionLifecycleGate.NotifyCaptureCancelled(_recordingSessionCoordinator);
        return;
      }
      catch (Exception ex)
      {
        RecordingSessionLifecycleGate.NotifyCaptureCancelled(_recordingSessionCoordinator);
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

        var previousId = SelectedInputDevice?.Id;
        AvailableInputDevices.Clear();
        if (response?.Devices != null)
        {
          foreach (var device in response.Devices)
          {
            AvailableInputDevices.Add(device);
          }
        }

        if (AvailableInputDevices.Count == 0)
        {
          SelectedInputDevice = null;
        }
        else         if (!string.IsNullOrWhiteSpace(previousId))
        {
          var match = AvailableInputDevices.FirstOrDefault(d => d.Id == previousId);
          if (match != null)
          {
            SelectedInputDevice = match;
          }
          else
          {
            SelectedInputDevice = null;
            StatusMessage = ResourceHelper.GetString(
                "Recording.PreviousMicUnavailable",
                "The selected microphone is no longer available. Choose a device before recording.");
          }
        }
        else if (SelectedInputDevice == null)
        {
          SelectedInputDevice = AvailableInputDevices[0];
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
      if (_recordingCaptureFanout != null && _recordingCaptureFanout.IsActive)
      {
        RecordingDuration = _recordingCaptureFanout.MaxLegDuration;
        RecordingDurationDisplay = RecordingDuration.ToString(@"mm\:ss");
        return;
      }

      if (!_microphoneService.IsRecording)
        return;

      // Update duration from local microphone service
      RecordingDuration = _microphoneService.Duration;
      RecordingDurationDisplay = RecordingDuration.ToString(@"mm\:ss");
    }

    private void FanoutAggregateLevelChanged(object? sender, float level)
    {
      Dispatcher.TryEnqueue(() =>
      {
        if (WaveformSamples.Count >= 100)
          WaveformSamples.RemoveAt(0);
        WaveformSamples.Add(level);
      });
    }

    private void FanoutCaptureSessionFaulted(object? sender, RecordingCaptureFaultedEventArgs e)
    {
      Dispatcher.TryEnqueue(() =>
      {
        _statusTimer.Stop();
        var assignmentSnapshot = _recordingSessionCoordinator == null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(_recordingSessionCoordinator.TrackInputAssignments, StringComparer.Ordinal);
        var sessionIdSnapshot = _recordingSessionCoordinator?.ActiveSessionId;
        RecordingSessionLifecycleGate.NotifyCaptureStartFailed(_recordingSessionCoordinator);
        IsRecording = false;
        ApplyMultitrackSessionOutcomeUi(e.StopResult, userInvokedStop: false);
        ErrorMessage = string.IsNullOrWhiteSpace(e.Message) ? SessionOutcomeMessage : e.Message;
        _toastNotificationService?.ShowError(
            ErrorMessage ?? e.Message,
            ResourceHelper.GetString("Toast.Title.RecordingError", "Recording Error"));
        _ = PersistMultitrackRecoveryAsync(
            assignmentSnapshot,
            sessionIdSnapshot,
            e.StopResult,
            userInvokedCleanStop: false);
      });
    }

    private void ClearSessionOutcomeUi()
    {
      IsSessionOutcomeBarOpen = false;
      SessionOutcomeTitle = string.Empty;
      SessionOutcomeMessage = string.Empty;
      SessionOutcomeSeverity = InfoBarSeverity.Informational;
    }

    private void ApplyMultitrackSessionOutcomeUi(RecordingCaptureStopResult stopResult, bool userInvokedStop)
    {
      var ok = stopResult.Legs.Count(l => l.CompletedSuccessfully);
      var n = stopResult.Legs.Count;
      var failedLines = stopResult.Legs
          .Where(l => !l.CompletedSuccessfully)
          .Select(l => $"• Track {l.TrackId}: {l.ErrorMessage ?? "Failed"}")
          .ToList();
      if (stopResult.SessionFaulted || failedLines.Count > 0)
      {
        SessionOutcomeSeverity = InfoBarSeverity.Warning;
        SessionOutcomeTitle = "Recording session incomplete";
        SessionOutcomeMessage =
            $"Recorded {ok} of {n} track(s) successfully." +
            (failedLines.Count > 0 ? "\n" + string.Join("\n", failedLines) : string.Empty) +
            (stopResult.SessionFaulted && !userInvokedStop
                ? "\nA capture error stopped the session; completed takes were preserved on disk when possible."
                : string.Empty) +
            "\nIf VoiceStudio closes before you finish, use Restore on next launch to import preserved takes.";
      }
      else
      {
        SessionOutcomeSeverity = InfoBarSeverity.Success;
        SessionOutcomeTitle = "Recording complete";
        SessionOutcomeMessage = $"All {n} track(s) recorded successfully.";
      }

      IsSessionOutcomeBarOpen = true;
    }

    private async Task PersistMultitrackRecoveryAsync(
        IReadOnlyDictionary<string, string> assignmentSnapshot,
        Guid? sessionIdSnapshot,
        RecordingCaptureStopResult stopResult,
        bool userInvokedCleanStop)
    {
      var recovery = AppServices.GetService<IMultitrackRecoveryStateService>();
      if (recovery == null)
        return;
      try
      {
        var sessionFullyClean = userInvokedCleanStop
            && !stopResult.SessionFaulted
            && stopResult.Legs.All(l => l.CompletedSuccessfully);
        if (MultitrackRecoveryPayloadBuilder.ShouldPersistForRecovery(stopResult, sessionFullyClean))
        {
          // Recoverable snapshots must use EndedCleanly=false so HasPendingPayload (multitrack recovery) is true.
          var payload = MultitrackRecoveryPayloadBuilder.Build(
              ProjectId,
              sessionIdSnapshot,
              assignmentSnapshot,
              stopResult,
              endedCleanly: false);
          await recovery.WritePendingAndSaveAsync(payload).ConfigureAwait(false);
        }
        else
        {
          await recovery.ClearPendingAndSaveAsync().ConfigureAwait(false);
        }
      }
      catch (Exception ex)
      {
        _logService?.LogError(ex, "MultitrackRecoveryPersist");
      }
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
        if (_recordingCaptureFanout != null && _recordingCaptureFanout.IsActive)
          return;
        IsRecording = false;
        _statusTimer.Stop();
        RecordingSessionLifecycleGate.NotifyCaptureStartFailed(_recordingSessionCoordinator);
        
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
      ArmForMultitrackCommand.NotifyCanExecuteChanged();
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
        if (_recordingCaptureFanout != null)
        {
          _recordingCaptureFanout.AggregateLevelChanged -= FanoutAggregateLevelChanged;
          _recordingCaptureFanout.CaptureSessionFaulted -= FanoutCaptureSessionFaulted;
        }

        if (_recordingDeviceAvailability != null)
          _recordingDeviceAvailability.InputDevicesChanged -= OnRecordingHardwareDevicesChanged;
      }
      base.Dispose(disposing);
    }

    partial void OnSelectedInputDeviceChanged(RecordingDevice? value)
    {
      _recordingInputCommandState?.SetSelectedInputSourceId(value?.Id);
    }
  }
}