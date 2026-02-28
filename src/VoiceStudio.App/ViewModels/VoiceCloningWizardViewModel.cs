using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;
using Windows.Storage;
using Windows.Storage.Pickers;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the VoiceCloningWizardView panel - Step-by-step voice cloning wizard.
  /// </summary>
  public partial class VoiceCloningWizardViewModel : BaseViewModel, IPanelView
  {
    private readonly IBackendClient _backendClient;
    private readonly ToastNotificationService? _toastNotificationService;
    private readonly IEventAggregator? _eventAggregator;
    private ISubscriptionToken? _cloneReferenceSubscription;

    public string PanelId => "voice-cloning-wizard";
    public string DisplayName => ResourceHelper.GetString("Panel.VoiceCloningWizard.DisplayName", "Voice Cloning Wizard");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private int currentStep = 1; // 1=Upload, 2=Configure, 3=Process, 4=Review

    [ObservableProperty]
    private StorageFile? selectedAudioFile;

    [ObservableProperty]
    private ObservableCollection<StorageFile> selectedAudioFiles = new();

    [ObservableProperty]
    private string? audioFileName;

    [ObservableProperty]
    private ObservableCollection<AudioValidationItem> audioValidations = new();

    [ObservableProperty]
    private AudioValidationItem? audioValidation;

    [ObservableProperty]
    private string? selectedEngine = "xtts";

    [ObservableProperty]
    private string? selectedQualityMode = "standard";

    [ObservableProperty]
    private ObservableCollection<string> availableEngines = new() { "xtts", "chatterbox", "tortoise" };

    [ObservableProperty]
    private ObservableCollection<string> qualityModes = new() { "fast", "standard", "high", "ultra" };

    [ObservableProperty]
    private string? profileName;

    [ObservableProperty]
    private string? profileDescription;

    [ObservableProperty]
    private string? wizardJobId;

    [ObservableProperty]
    private float processingProgress;

    [ObservableProperty]
    private string? processingStatus;

    [ObservableProperty]
    private string? createdProfileId;

    [ObservableProperty]
    private QualityMetricsItem? qualityMetrics;

    [ObservableProperty]
    private string? testSynthesisAudioUrl;

    [ObservableProperty]
    private string? uploadedAudioId;

    [ObservableProperty]
    private ObservableCollection<string> uploadedAudioIds = new();

    [ObservableProperty]
    private ObservableCollection<QualityCandidateItem> candidateMetrics = new();

    [ObservableProperty]
    private string? device;

    public VoiceCloningWizardViewModel(IViewModelContext context, IBackendClient backendClient)
        : base(context)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));

      // Get services using helper (reduces code duplication)
      _toastNotificationService = ServiceInitializationHelper.TryGetService(() => AppServices.TryGetToastNotificationService());
      
      // Subscribe to CloneReferenceSelectedEvent for inter-panel workflow
      _eventAggregator = AppServices.TryGetEventAggregator();
      _cloneReferenceSubscription = _eventAggregator?.Subscribe<CloneReferenceSelectedEvent>(OnCloneReferenceSelected);

      SelectedAudioFiles.CollectionChanged += SelectedAudioFiles_CollectionChanged;
      AudioValidations.CollectionChanged += AudioValidations_CollectionChanged;

      BrowseAudioCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("BrowseAudio");
        await BrowseAudioAsync(ct);
      });
      ValidateAudioCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ValidateAudio");
        await ValidateAudioAsync(ct);
      }, () => SelectedAudioFiles.Count > 0);
      NextStepCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("NextStep");
        await NextStepAsync(ct);
      }, () => CanProceedToNextStep);
      PreviousStepCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("PreviousStep");
        await PreviousStepAsync(ct);
      }, () => CurrentStep > 1);
      StartProcessingCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("StartProcessing");
        await StartProcessingAsync(ct);
      }, () => CanStartProcessing);
      FinalizeWizardCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("FinalizeWizard");
        await FinalizeWizardAsync(ct);
      }, () => CanFinalize);
      CancelWizardCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CancelWizard");
        await CancelWizardAsync(ct);
      });
    }

    public IAsyncRelayCommand BrowseAudioCommand { get; }
    public IAsyncRelayCommand ValidateAudioCommand { get; }
    public IAsyncRelayCommand NextStepCommand { get; }
    public IAsyncRelayCommand PreviousStepCommand { get; }
    public IAsyncRelayCommand StartProcessingCommand { get; }
    public IAsyncRelayCommand FinalizeWizardCommand { get; }
    public IAsyncRelayCommand CancelWizardCommand { get; }

    private bool CanProceedToNextStep
    {
      get
      {
        return CurrentStep switch
        {
          1 => AudioValidations.Count > 0 && AudioValidations.All(v => v.IsValid), // Step 1: Must have valid audio (all clips)
          2 => !string.IsNullOrWhiteSpace(ProfileName) && !string.IsNullOrWhiteSpace(SelectedEngine), // Step 2: Must have name and engine
          3 => ProcessingStatus == "completed", // Step 3: Must be completed
          _ => false
        };
      }
    }

    private bool CanStartProcessing => CurrentStep == 2 && !string.IsNullOrWhiteSpace(ProfileName) && !string.IsNullOrWhiteSpace(SelectedEngine) && UploadedAudioIds.Count > 0;

    private bool CanFinalize => CurrentStep == 4 && ProcessingStatus == "completed" && !string.IsNullOrWhiteSpace(CreatedProfileId);

    partial void OnSelectedAudioFileChanged(StorageFile? value)
    {
      AudioFileName = value?.Name;
      ValidateAudioCommand.NotifyCanExecuteChanged();
    }

    partial void OnCurrentStepChanged(int value)
    {
      NextStepCommand.NotifyCanExecuteChanged();
      PreviousStepCommand.NotifyCanExecuteChanged();
      StartProcessingCommand.NotifyCanExecuteChanged();
      FinalizeWizardCommand.NotifyCanExecuteChanged();
    }

    partial void OnProfileNameChanged(string? value)
    {
      NextStepCommand.NotifyCanExecuteChanged();
      StartProcessingCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedEngineChanged(string? value)
    {
      NextStepCommand.NotifyCanExecuteChanged();
      StartProcessingCommand.NotifyCanExecuteChanged();
      OnPropertyChanged(nameof(MultiReferenceWarning));
    }

    /// <summary>
    /// Warning message shown when multiple reference files are selected
    /// but the chosen engine only uses the first one.
    /// Audit remediation M-7: Multi-reference behavior documentation.
    /// </summary>
    public string? MultiReferenceWarning
    {
      get
      {
        if (SelectedAudioFiles.Count <= 1)
          return null;

        // Only XTTS supports multi-reference ensemble
        var engine = SelectedEngine?.ToLowerInvariant() ?? "";
        if (engine == "xtts" || engine == "xtts_v2")
          return null;

        return $"Note: {SelectedEngine ?? "This engine"} uses only the first reference file. "
               + "Only XTTS v2 benefits from multiple reference files (ensemble cloning). "
               + "Additional files will be ignored.";
      }
    }

    partial void OnProcessingStatusChanged(string? value)
    {
      NextStepCommand.NotifyCanExecuteChanged();
      FinalizeWizardCommand.NotifyCanExecuteChanged();
    }

    private void SelectedAudioFiles_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
      ValidateAudioCommand.NotifyCanExecuteChanged();
      NextStepCommand.NotifyCanExecuteChanged();
    }

    private void AudioValidations_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
      NextStepCommand.NotifyCanExecuteChanged();
    }

    private async Task BrowseAudioAsync(CancellationToken cancellationToken)
    {
      try
      {
        var picker = new FileOpenPicker();
        picker.ViewMode = PickerViewMode.List;
        picker.SuggestedStartLocation = PickerLocationId.MusicLibrary;
        
        // Add all supported media formats (audio + video for audio extraction)
        foreach (var ext in Core.Audio.AudioFileFormats.GetMediaFileTypeChoices())
        {
          picker.FileTypeFilter.Add(ext);
        }

        // WinUI 3 requires initializing the picker with the window handle
        var window = App.MainWindowInstance;
        if (window != null)
        {
          var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
          WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        var files = await picker.PickMultipleFilesAsync();
        cancellationToken.ThrowIfCancellationRequested();

        if (files?.Any() == true)
        {
          SelectedAudioFiles.Clear();
          foreach (var f in files)
          {
            SelectedAudioFiles.Add(f);
          }

          // Keep backwards-compatible primary selection fields
          SelectedAudioFile = SelectedAudioFiles.FirstOrDefault();
          AudioFileName = string.Join(", ", SelectedAudioFiles.Select(f => f.Name));
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("VoiceCloningWizard.BrowseAudioFileFailed", ex.Message);
        await HandleErrorAsync(ex, "BrowseAudio");
      }
    }

    private async Task ValidateAudioAsync(CancellationToken cancellationToken)
    {
      if (SelectedAudioFiles == null || SelectedAudioFiles.Count == 0)
      {
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        UploadedAudioIds.Clear();
        UploadedAudioId = null;
        if (SelectedAudioFiles.Count > 0)
        {
          foreach (var file in SelectedAudioFiles)
          {
            var uploadedId = await UploadAudioFileAsync(file, cancellationToken);
            if (!string.IsNullOrEmpty(uploadedId))
            {
              UploadedAudioIds.Add(uploadedId);
            }
          }
        }

        AudioValidations.Clear();

        foreach (var audioId in UploadedAudioIds)
        {
          var request = new AudioValidationRequest
          {
            AudioId = audioId
          };

          var validation = await _backendClient.SendRequestAsync<AudioValidationRequest, AudioValidationResponse>(
              "/api/voice/clone/wizard/validate-audio",
              request,
              System.Net.Http.HttpMethod.Post,
              cancellationToken
          );

          if (validation != null)
          {
            var item = new AudioValidationItem(validation) { SourceAudioId = audioId };
            AudioValidations.Add(item);
          }
        }

        AudioValidation = AudioValidations.FirstOrDefault();

        if (AudioValidations.Count > 0)
        {
          var validCount = AudioValidations.Count(v => v.IsValid);
          var total = AudioValidations.Count;

          StatusMessage = validCount == total
              ? ResourceHelper.GetString("VoiceCloningWizard.AudioValidatedSuccess", "Audio validated successfully")
              : ResourceHelper.GetString("VoiceCloningWizard.AudioValidationFoundIssues", "Audio validation found issues");

          if (validCount == total)
          {
            _toastNotificationService?.ShowSuccess(
                ResourceHelper.GetString("VoiceCloningWizard.AudioValidated", "Audio Validated"),
                ResourceHelper.GetString("VoiceCloningWizard.AudioValidationPassed", "Audio file validation passed"));
          }
          else
          {
            var issueCount = AudioValidations.Sum(v => v.Issues?.Length ?? 0);
            _toastNotificationService?.ShowError(
                ResourceHelper.GetString("VoiceCloningWizard.ValidationFailed", "Validation Failed"),
                ResourceHelper.FormatString("VoiceCloningWizard.ValidationFoundIssues", issueCount));
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("VoiceCloningWizard.ValidateAudioFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("VoiceCloningWizard.ValidationFailed", "Validation Failed"),
            ex.Message);
        await HandleErrorAsync(ex, "ValidateAudio");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task<string?> UploadAudioFileAsync(StorageFile file, CancellationToken cancellationToken)
    {
      try
      {
        cancellationToken.ThrowIfCancellationRequested();

        // Use the centralized IBackendClient for file upload
        var uploadResponse = await _backendClient.UploadFileWithProgressAsync<AudioUploadResponse>(
            "/api/audio/upload",
            file.Path,
            "file",
            additionalData: null,
            progress: null,
            timeout: null,
            cancellationToken);

        return uploadResponse?.AudioId;
      }
      catch (OperationCanceledException)
      {
        return null;
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("VoiceCloningWizard.UploadAudioFileFailed", ex.Message);
        return null;
      }
    }

    private async Task NextStepAsync(CancellationToken cancellationToken)
    {
      try
      {
        if (CurrentStep == 2 && CanStartProcessing)
        {
          // Start processing before moving to step 3
          await StartProcessingAsync(cancellationToken);
        }
        else if (CurrentStep < 4)
        {
          CurrentStep++;
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
    }

    private Task PreviousStepAsync(CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();

      if (CurrentStep > 1)
      {
        CurrentStep--;
      }

      return Task.CompletedTask;
    }

    private async Task StartProcessingAsync(CancellationToken cancellationToken)
    {
      if (SelectedAudioFile == null)
      {
        return;
      }

      var profileNameValue = ProfileName?.Trim();
      if (string.IsNullOrWhiteSpace(profileNameValue))
      {
        return;
      }

      IsLoading = true;
      ErrorMessage = null;
      ProcessingStatus = ResourceHelper.GetString("VoiceCloningWizard.Processing", "processing");
      ProcessingProgress = 0.0f;

      try
      {
        if (UploadedAudioIds.Count == 0)
        {
          if (SelectedAudioFiles.Count == 0 && SelectedAudioFile != null)
          {
            SelectedAudioFiles.Clear();
            SelectedAudioFiles.Add(SelectedAudioFile);
          }

          foreach (var file in SelectedAudioFiles)
          {
            var uploadedId = await UploadAudioFileAsync(file, cancellationToken);
            if (string.IsNullOrEmpty(uploadedId))
            {
              ErrorMessage = ResourceHelper.GetString("VoiceCloningWizard.UploadAudioFailed", "Failed to upload audio file");
              ProcessingStatus = ResourceHelper.GetString("VoiceCloningWizard.Failed", "failed");
              return;
            }
            UploadedAudioIds.Add(uploadedId);
          }
        }

        UploadedAudioId = UploadedAudioIds.FirstOrDefault();

        var request = new WizardStartRequest
        {
          ReferenceAudioId = UploadedAudioId ?? string.Empty,
          Engine = SelectedEngine ?? "xtts",
          QualityMode = SelectedQualityMode ?? "standard",
          ProfileName = profileNameValue,
          ProfileDescription = ProfileDescription
        };

        var response = await _backendClient.SendRequestAsync<WizardStartRequest, WizardStartResponse>(
            "/api/voice/clone/wizard/start",
            request,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        if (response != null)
        {
          WizardJobId = response.JobId;
          CurrentStep = 3;

          // Start processing
          await _backendClient.SendRequestAsync<object, object>(
              $"/api/voice/clone/wizard/{WizardJobId}/process",
              null,
              System.Net.Http.HttpMethod.Post,
              cancellationToken
          );

          // Poll for status (with linked cancellation token)
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.GetString("VoiceCloningWizard.ProcessingStarted", "Voice cloning processing has started"),
              ResourceHelper.GetString("Toast.Title.ProcessingStarted", "Processing Started"));
          await PollProcessingStatusAsync(cancellationToken);
        }
      }
      catch (OperationCanceledException)
      {
        ProcessingStatus = ResourceHelper.GetString("VoiceCloningWizard.Cancelled", "cancelled");
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("VoiceCloningWizard.StartProcessingFailed", ex.Message);
        ProcessingStatus = ResourceHelper.GetString("VoiceCloningWizard.Failed", "failed");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("VoiceCloningWizard.ProcessingFailed", "Processing Failed"),
            ex.Message);
        await HandleErrorAsync(ex, "StartProcessing");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task PollProcessingStatusAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(WizardJobId))
      {
        return;
      }

      try
      {
        while (ProcessingStatus == "processing" && !cancellationToken.IsCancellationRequested)
        {
          await Task.Delay(1000, cancellationToken); // Poll every second

          var status = await _backendClient.SendRequestAsync<object, WizardStatusResponse>(
              $"/api/voice/clone/wizard/{WizardJobId}/status",
              null,
              System.Net.Http.HttpMethod.Get,
              cancellationToken
          );

          if (status != null)
          {
            ProcessingProgress = status.Progress;
            ProcessingStatus = status.Status;

            if (status.Status == "completed")
            {
              CreatedProfileId = status.ProfileId;
              if (status.QualityMetrics != null)
              {
                QualityMetrics = new QualityMetricsItem(status.QualityMetrics);
              }
              else
              {
                QualityMetrics = null;
              }

              // Device and candidate metrics can arrive at top-level (preferred) or inside quality metrics fallback.
              Device = status.Device ?? QualityMetrics?.Device;

              CandidateMetrics.Clear();
              if (status.CandidateMetrics?.Count > 0)
              {
                foreach (var c in status.CandidateMetrics)
                {
                  var item = QualityCandidateItem.FromDictionary(c.ToDictionary());
                  if (item != null)
                  {
                    CandidateMetrics.Add(item);
                  }
                }
              }
              else if (QualityMetrics != null)
              {
                foreach (var c in QualityMetrics.Candidates)
                {
                  CandidateMetrics.Add(c);
                }
              }

              TestSynthesisAudioUrl = status.TestSynthesisAudioUrl;
              CurrentStep = 4;
              StatusMessage = ResourceHelper.GetString("VoiceCloningWizard.CloningCompleted", "Voice cloning completed successfully");

              Dispatcher.TryEnqueue(() =>
              {
                var profileName = ProfileName ?? "Unknown Profile";
                _toastNotificationService?.ShowSuccess(
                                  ResourceHelper.FormatString("VoiceCloningWizard.CloningCompletedForProfile", profileName),
                                  ResourceHelper.GetString("Toast.Title.ProcessingComplete", "Processing Complete"));
              });
              break;
            }
            else if (status.Status == "failed")
            {
              ErrorMessage = status.ErrorMessage ?? ResourceHelper.GetString("VoiceCloningWizard.ProcessingFailedStatus", "Processing failed");

              Dispatcher.TryEnqueue(() =>
              {
                _toastNotificationService?.ShowError(
                                  ResourceHelper.GetString("VoiceCloningWizard.ProcessingFailed", "Processing Failed"),
                                  status.ErrorMessage ?? ResourceHelper.GetString("VoiceCloningWizard.ProcessingFailedDetail", "Voice cloning processing failed"));
              });
              break;
            }
          }
        }
      }
      catch (OperationCanceledException)
      {
        ProcessingStatus = ResourceHelper.GetString("VoiceCloningWizard.Cancelled", "cancelled");
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("VoiceCloningWizard.PollStatusFailed", ex.Message);
        ProcessingStatus = ResourceHelper.GetString("VoiceCloningWizard.Failed", "failed");
        await HandleErrorAsync(ex, "PollProcessingStatus");
      }
    }

    private async Task FinalizeWizardAsync(CancellationToken cancellationToken)
    {
      var wizardJobIdValue = WizardJobId;
      if (string.IsNullOrWhiteSpace(wizardJobIdValue))
      {
        return;
      }

      var profileNameValue = ProfileName?.Trim();

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new WizardFinalizeRequest
        {
          JobId = wizardJobIdValue,
          ProfileName = profileNameValue,
          ProfileDescription = ProfileDescription
        };

        var response = await _backendClient.SendRequestAsync<WizardFinalizeRequest, WizardFinalizeResponse>(
            $"/api/voice/clone/wizard/{wizardJobIdValue}/finalize",
            request,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        if (response?.Success == true)
        {
          StatusMessage = ResourceHelper.FormatString("VoiceCloningWizard.ProfileCreatedSuccess", response.ProfileName);
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.GetString("VoiceCloningWizard.WizardComplete", "Wizard Complete"),
              ResourceHelper.FormatString("VoiceCloningWizard.ProfileCreatedSuccess", response.ProfileName));

          // Publish ProfileCreatedEvent so Library refreshes (X-2)
          // and other panels know a new profile is available
          var eventAggregator = AppServices.TryGetEventAggregator();
          if (eventAggregator != null && !string.IsNullOrEmpty(response.ProfileId))
          {
            eventAggregator.Publish(new ProfileCreatedEvent(
                PanelId,
                response.ProfileId,
                response.ProfileName ?? ProfileName ?? "Cloned Voice"));

            // Navigate to Synthesis panel with the new profile pre-selected (X-5)
            eventAggregator.Publish(new NavigateToEvent(
                PanelId,
                "voice-synthesis",
                new Dictionary<string, object>
                {
                  { "selectedProfileId", response.ProfileId },
                  { "selectedProfileName", response.ProfileName ?? ProfileName ?? "" }
                }));
          }

          // Reset wizard for next use
          await ResetWizardAsync();
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("VoiceCloningWizard.FinalizeWizardFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.FinalizationFailed", "Finalization Failed"),
            ex.Message);
        await HandleErrorAsync(ex, "FinalizeWizard");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task CancelWizardAsync(CancellationToken cancellationToken)
    {
      try
      {
        if (!string.IsNullOrWhiteSpace(WizardJobId))
        {
          try
          {
            await _backendClient.SendRequestAsync<object, object>(
                $"/api/voice/clone/wizard/{WizardJobId}",
                null,
                System.Net.Http.HttpMethod.Delete,
                cancellationToken
            );
          }
          catch (Exception ex)
          {
            ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "VoiceCloningWizardViewModel.CancelWizardAsync");
          }
        }

        await ResetWizardAsync();
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
    }

    private Task ResetWizardAsync()
    {
      CurrentStep = 1;
      SelectedAudioFile = null;
      SelectedAudioFiles.Clear();
      AudioFileName = null;
      AudioValidation = null;
      AudioValidations.Clear();
      SelectedEngine = "xtts";
      SelectedQualityMode = "standard";
      ProfileName = null;
      ProfileDescription = null;
      WizardJobId = null;
      ProcessingProgress = 0.0f;
      ProcessingStatus = null;
      CreatedProfileId = null;
      QualityMetrics = null;
      CandidateMetrics.Clear();
      Device = null;
      TestSynthesisAudioUrl = null;
      UploadedAudioId = null;
      UploadedAudioIds.Clear();
      ErrorMessage = null;
      StatusMessage = null;

      return Task.CompletedTask;
    }

    /// <summary>
    /// Handles the CloneReferenceSelectedEvent from Library panel.
    /// Adds the selected audio file to the wizard's audio collection.
    /// </summary>
    private async void OnCloneReferenceSelected(CloneReferenceSelectedEvent e)
    {
      try
      {
        if (string.IsNullOrEmpty(e.AssetPath))
        {
          return;
        }

        // Load the audio file from the asset path
        await LoadAudioFromPathAsync(e.AssetPath, e.AssetName);

        StatusMessage = ResourceHelper.FormatString(
          "VoiceCloningWizard.LoadedFromLibrary",
          $"Added '{e.AssetName ?? Path.GetFileName(e.AssetPath)}' for cloning"
        );
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Error loading clone reference in wizard: {ex.Message}");
        ErrorMessage = $"Failed to load audio: {ex.Message}";
      }
    }

    /// <summary>
    /// Loads an audio file from a file path (used by inter-panel workflow).
    /// </summary>
    private async Task LoadAudioFromPathAsync(string path, string? displayName = null)
    {
      if (string.IsNullOrEmpty(path) || !File.Exists(path))
      {
        throw new FileNotFoundException($"Audio file not found: {path}");
      }

      // Get StorageFile from path
      var file = await StorageFile.GetFileFromPathAsync(path);

      // Add to the audio files collection
      if (!SelectedAudioFiles.Any(f => f.Path == file.Path))
      {
        SelectedAudioFiles.Add(file);
      }

      // Set profile name from display name or filename if not already set
      if (string.IsNullOrWhiteSpace(ProfileName) && !string.IsNullOrWhiteSpace(displayName))
      {
        ProfileName = displayName;
      }

      // Reset to step 1 if we're adding audio
      if (CurrentStep > 1 && ProcessingStatus != "processing")
      {
        CurrentStep = 1;
      }
    }

    /// <summary>
    /// Cleanup subscriptions when view model is disposed.
    /// </summary>
    /// <param name="disposing">True if called from Dispose(), false if called from finalizer</param>
    protected override void Dispose(bool disposing)
    {
      if (disposing)
      {
        _cloneReferenceSubscription?.Dispose();
        _cloneReferenceSubscription = null;
      }
      base.Dispose(disposing);
    }

    // Request/Response models
    private class AudioValidationRequest
    {
      public string AudioId { get; set; } = string.Empty;
    }

    public class AudioValidationResponse
    {
      public bool IsValid { get; set; }
      public double Duration { get; set; }
      public int SampleRate { get; set; }
      public int Channels { get; set; }
      public string[] Issues { get; set; } = Array.Empty<string>();
      public string[] Recommendations { get; set; } = Array.Empty<string>();
      public double? QualityScore { get; set; }
    }

    private class WizardStartRequest
    {
      public string ReferenceAudioId { get; set; } = string.Empty;
      public string Engine { get; set; } = "xtts";
      public string QualityMode { get; set; } = "standard";
      public string ProfileName { get; set; } = string.Empty;
      public string? ProfileDescription { get; set; }
    }

    private class WizardStartResponse
    {
      public string JobId { get; set; } = string.Empty;
      public int Step { get; set; }
      public string Status { get; set; } = string.Empty;
    }

    private class WizardStatusResponse
    {
      public string JobId { get; set; } = string.Empty;
      public int Step { get; set; }
      public string Status { get; set; } = string.Empty;
      public float Progress { get; set; }
      public string? ProfileId { get; set; }
      public Dictionary<string, object>? QualityMetrics { get; set; }
      public string? TestSynthesisAudioUrl { get; set; }
      public string? ErrorMessage { get; set; }

      [JsonPropertyName("device")]
      public string? Device { get; set; }

      [JsonPropertyName("candidate_metrics")]
      public List<CandidateMetricDto>? CandidateMetrics { get; set; }
    }

    private class WizardFinalizeRequest
    {
      public string JobId { get; set; } = string.Empty;
      public string? ProfileName { get; set; }
      public string? ProfileDescription { get; set; }
    }

    private class WizardFinalizeResponse
    {
      public string ProfileId { get; set; } = string.Empty;
      public string ProfileName { get; set; } = string.Empty;
      public bool Success { get; set; }
    }

    private class AudioUploadResponse
    {
      public string AudioId { get; set; } = string.Empty;
      public string? FileName { get; set; }
      public long? FileSize { get; set; }
    }
  }

  // Data models
  public class AudioValidationItem : ObservableObject
  {
    public string? SourceAudioId { get; set; }
    public bool IsValid { get; set; }
    public double Duration { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public string[] Issues { get; set; }
    public string[] Recommendations { get; set; }
    public double? QualityScore { get; set; }

    public string DurationDisplay => $"{Duration:F1}s";
    public string SampleRateDisplay => $"{SampleRate} Hz";
    public string ChannelsDisplay => Channels == 1 ? "Mono" : $"{Channels} channels";

    public AudioValidationItem(VoiceCloningWizardViewModel.AudioValidationResponse validation)
    {
      IsValid = validation.IsValid;
      Duration = validation.Duration;
      SampleRate = validation.SampleRate;
      Channels = validation.Channels;
      Issues = validation.Issues ?? Array.Empty<string>();
      Recommendations = validation.Recommendations ?? Array.Empty<string>();
      QualityScore = validation.QualityScore;
    }
  }

  public class QualityMetricsItem : ObservableObject
  {
    public double? MosScore { get; set; }
    public double? Similarity { get; set; }
    public double? Naturalness { get; set; }
    public double? SnrDb { get; set; }
    public double? SilenceRatio { get; set; }
    public double? SpeakingRate { get; set; }
    public double? ArtifactScore { get; set; }
    public bool? HasClicks { get; set; }
    public bool? HasDistortion { get; set; }
    public string? Device { get; set; }

    public ObservableCollection<QualityCandidateItem> Candidates { get; } = new();

    public string MosScoreDisplay => MosScore.HasValue ? $"{MosScore.Value:F2}/5.0" : "N/A";
    public string SimilarityDisplay => Similarity.HasValue ? $"{Similarity.Value:P0}" : "N/A";
    public string NaturalnessDisplay => Naturalness.HasValue ? $"{Naturalness.Value:P0}" : "N/A";
    public string SnrDbDisplay => SnrDb.HasValue ? $"{SnrDb.Value:F1} dB" : "N/A";
    public string SilenceRatioDisplay => SilenceRatio.HasValue ? $"{SilenceRatio.Value:P0}" : "N/A";
    public string SpeakingRateDisplay => SpeakingRate.HasValue ? $"{SpeakingRate.Value:F2} / s" : "N/A";
    public string ArtifactScoreDisplay => ArtifactScore.HasValue ? $"{ArtifactScore.Value:F4}" : "N/A";
    public string HasClicksDisplay => HasClicks.HasValue ? (HasClicks.Value ? "Yes" : "No") : "N/A";
    public string HasDistortionDisplay => HasDistortion.HasValue ? (HasDistortion.Value ? "Yes" : "No") : "N/A";
    public string DeviceDisplay => string.IsNullOrWhiteSpace(Device) ? "N/A" : Device;

    public QualityMetricsItem(Dictionary<string, object> metrics)
    {
      if (metrics.TryGetValue("mos_score", out var mos) && mos != null)
      {
        MosScore = ReadDouble(mos);
      }
      if (metrics.TryGetValue("similarity", out var sim) && sim != null)
      {
        Similarity = ReadDouble(sim);
      }
      if (metrics.TryGetValue("naturalness", out var nat) && nat != null)
      {
        Naturalness = ReadDouble(nat);
      }
      if (metrics.TryGetValue("snr_db", out var snr) && snr != null)
      {
        SnrDb = ReadDouble(snr);
      }
      if (metrics.TryGetValue("silence_ratio", out var silence) && silence != null)
      {
        SilenceRatio = ReadDouble(silence);
      }
      if (metrics.TryGetValue("speaking_rate", out var rate) && rate != null)
      {
        SpeakingRate = ReadDouble(rate);
      }
      if (metrics.TryGetValue("artifact_score", out var artifact) && artifact != null)
      {
        ArtifactScore = ReadDouble(artifact);
      }
      if (metrics.TryGetValue("has_clicks", out var clicks) && clicks != null)
      {
        HasClicks = ReadBool(clicks);
      }
      if (metrics.TryGetValue("has_distortion", out var distortion) && distortion != null)
      {
        HasDistortion = ReadBool(distortion);
      }

      if (metrics.TryGetValue("device", out var device) && device != null)
      {
        Device = ReadString(device);
      }

      if (metrics.TryGetValue("candidate_metrics", out var candidatesRaw) && candidatesRaw != null)
      {
        foreach (var candidate in ParseCandidates(candidatesRaw))
        {
          Candidates.Add(candidate);
        }
      }
    }

    internal static double? ReadDouble(object value)
    {
      if (value is null)
      {
        return null;
      }

      if (value is JsonElement element)
      {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var number))
        {
          return number;
        }
        if (element.ValueKind == JsonValueKind.String
            && double.TryParse(element.GetString(), out var parsed))
        {
          return parsed;
        }
        return null;
      }

      if (value is double doubleValue)
      {
        return doubleValue;
      }
      if (value is float floatValue)
      {
        return floatValue;
      }
      if (value is int intValue)
      {
        return intValue;
      }
      if (value is long longValue)
      {
        return longValue;
      }
      if (value is decimal decimalValue)
      {
        return (double)decimalValue;
      }

      try
      {
        return Convert.ToDouble(value);
      }
      catch (Exception)
      {
        return null;
      }
    }

    private static bool? ReadBool(object value)
    {
      if (value is null)
      {
        return null;
      }

      if (value is JsonElement element)
      {
        if (element.ValueKind == JsonValueKind.True)
        {
          return true;
        }
        if (element.ValueKind == JsonValueKind.False)
        {
          return false;
        }
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var number))
        {
          return Math.Abs(number) > 0.0;
        }
        if (element.ValueKind == JsonValueKind.String
            && bool.TryParse(element.GetString(), out var parsedBool))
        {
          return parsedBool;
        }
        return null;
      }

      if (value is bool boolValue)
      {
        return boolValue;
      }

      if (value is int intValue)
      {
        return intValue != 0;
      }
      if (value is long longValue)
      {
        return longValue != 0;
      }

      if (value is string stringValue)
      {
        if (bool.TryParse(stringValue, out var parsed))
        {
          return parsed;
        }
        if (double.TryParse(stringValue, out var parsedNumber))
        {
          return Math.Abs(parsedNumber) > 0.0;
        }
      }

      return null;
    }

    private static string? ReadString(object value)
    {
      if (value is null)
      {
        return null;
      }

      if (value is JsonElement element)
      {
        if (element.ValueKind == JsonValueKind.String)
        {
          return element.GetString();
        }

        return element.ToString();
      }

      return value.ToString();
    }

    private static IEnumerable<QualityCandidateItem> ParseCandidates(object raw)
    {
      var results = new List<QualityCandidateItem>();
      try
      {
        if (raw is JsonElement element && element.ValueKind == JsonValueKind.Array)
        {
          foreach (var item in element.EnumerateArray())
          {
            var dict = new Dictionary<string, object>();
            foreach (var prop in item.EnumerateObject())
            {
              dict[prop.Name] = prop.Value;
            }
            var candidate = QualityCandidateItem.FromDictionary(dict);
            if (candidate != null)
            {
              results.Add(candidate);
            }
          }
        }
        else if (raw is IEnumerable<object> enumerable)
        {
          foreach (var obj in enumerable)
          {
            if (obj is Dictionary<string, object> dict)
            {
              var candidate = QualityCandidateItem.FromDictionary(dict);
              if (candidate != null)
              {
                results.Add(candidate);
              }
            }
          }
        }
      }
      catch
      {
        return Array.Empty<QualityCandidateItem>();
      }

      return results;
    }
  }

  public class QualityCandidateItem
  {
    public string? Label { get; set; }
    public double? Score { get; set; }
    public string? Device { get; set; }
    public Dictionary<string, object>? Metrics { get; set; }
    public string? ReferenceAudio { get; set; }
    public bool? Selected { get; set; }

    public string LabelDisplay => string.IsNullOrWhiteSpace(Label) ? "Candidate" : Label;
    public string ScoreDisplay => Score.HasValue ? $"{Score.Value:F3}" : "N/A";
    public string DeviceDisplay => string.IsNullOrWhiteSpace(Device) ? "N/A" : Device;
    public string SelectedDisplay => Selected.HasValue ? (Selected.Value ? "Selected" : "Not selected") : "Unknown";

    public static QualityCandidateItem? FromDictionary(Dictionary<string, object> dict)
    {
      try
      {
        dict.TryGetValue("label", out var label);
        if (label == null)
        {
          dict.TryGetValue("name", out label);
        }
        if (label == null)
        {
          dict.TryGetValue("candidate", out label);
        }

        dict.TryGetValue("score", out var score);
        if (score == null && dict.TryGetValue("mos", out var mosScore))
        {
          score = mosScore;
        }

        dict.TryGetValue("device", out var device);
        if (device == null && dict.TryGetValue("device_name", out var deviceName))
        {
          device = deviceName;
        }

        dict.TryGetValue("metrics", out var metrics);
        dict.TryGetValue("reference_audio", out var referenceAudio);
        dict.TryGetValue("selected", out var selected);

        return new QualityCandidateItem
        {
          Label = label?.ToString(),
          Score = score == null ? null : QualityMetricsItem.ReadDouble(score),
          Device = device?.ToString(),
          ReferenceAudio = referenceAudio?.ToString(),
          Metrics = metrics as Dictionary<string, object>,
          Selected = selected is bool b ? b : null
        };
      }
      catch
      {
        return null;
      }
    }
  }

  public class CandidateMetricDto
  {
    [JsonPropertyName("reference_audio")]
    public string? ReferenceAudio { get; set; }

    [JsonPropertyName("metrics")]
    public Dictionary<string, object>? Metrics { get; set; }

    [JsonPropertyName("score")]
    public double? Score { get; set; }

    [JsonPropertyName("selected")]
    public bool? Selected { get; set; }

    [JsonPropertyName("device")]
    public string? Device { get; set; }

    public Dictionary<string, object> ToDictionary()
    {
      return new Dictionary<string, object>
            {
                { "reference_audio", ReferenceAudio ?? string.Empty },
                { "metrics", Metrics ?? new Dictionary<string, object>() },
                { "score", Score ?? 0.0 },
                { "selected", Selected ?? false },
                { "device", Device ?? string.Empty },
            };
    }
  }
}