using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Events;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.Services;
using Windows.Storage;
using Windows.Storage.Pickers;
using System.Runtime.InteropServices.WindowsRuntime;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the VoiceQuickCloneView panel - Streamlined, one-click voice cloning interface.
  /// </summary>
  public partial class VoiceQuickCloneViewModel : BaseViewModel, IPanelView
  {
    private readonly IBackendClient _backendClient;
    private readonly IEventAggregator? _eventAggregator;
    private ISubscriptionToken? _cloneReferenceSubscription;

    public string PanelId => "voice-quick-clone";
    public string DisplayName => ResourceHelper.GetString("Panel.VoiceQuickClone.DisplayName", "Quick Clone");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private StorageFile? selectedAudioFile;

    [ObservableProperty]
    private string? audioFileName;

    [ObservableProperty]
    private string? detectedEngine;

    [ObservableProperty]
    private string? detectedQualityMode;

    [ObservableProperty]
    private string? profileName;

    [ObservableProperty]
    private float processingProgress;

    [ObservableProperty]
    private string? processingStatus;

    [ObservableProperty]
    private string? createdProfileId;

    [ObservableProperty]
    private string? createdProfileUrl;

    [ObservableProperty]
    private float? qualityScore;

    [ObservableProperty]
    private bool isProcessing;

    public VoiceQuickCloneViewModel(IViewModelContext context, IBackendClient backendClient)
        : base(context)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
      
      // Subscribe to CloneReferenceSelectedEvent for inter-panel workflow
      _eventAggregator = AppServices.TryGetEventAggregator();
      _cloneReferenceSubscription = _eventAggregator?.Subscribe<CloneReferenceSelectedEvent>(OnCloneReferenceSelected);

      BrowseAudioCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("BrowseAudio");
        await BrowseAudioAsync(ct);
      }, () => !IsProcessing);
      QuickCloneCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("QuickClone");
        await QuickCloneAsync(ct);
      }, () => SelectedAudioFile != null && !IsProcessing);
      ResetCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Reset");
        await ResetAsync(ct);
      }, () => !IsProcessing);
    }

    public IAsyncRelayCommand BrowseAudioCommand { get; }
    public IAsyncRelayCommand QuickCloneCommand { get; }
    public IAsyncRelayCommand ResetCommand { get; }

    partial void OnSelectedAudioFileChanged(StorageFile? value)
    {
      if (value != null)
      {
        AudioFileName = value.Name;
        _ = AutoDetectSettingsAsync(CancellationToken.None);
      }
      else
      {
        AudioFileName = null;
        DetectedEngine = null;
        DetectedQualityMode = null;
      }
      QuickCloneCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsProcessingChanged(bool value)
    {
      QuickCloneCommand.NotifyCanExecuteChanged();
    }

    private async Task BrowseAudioAsync(CancellationToken cancellationToken)
    {
      try
      {
        cancellationToken.ThrowIfCancellationRequested();
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

        var file = await picker.PickSingleFileAsync();
        cancellationToken.ThrowIfCancellationRequested();
        if (file != null)
        {
          SelectedAudioFile = file;
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "BrowseAudio");
      }
    }

    private async Task AutoDetectSettingsAsync(CancellationToken cancellationToken)
    {
      if (SelectedAudioFile == null)
      {
        return;
      }

      try
      {
        // Auto-detect engine and quality based on file properties
        // In a real implementation, this would analyze the audio file
        var fileSize = (await SelectedAudioFile.GetBasicPropertiesAsync()).Size;

        cancellationToken.ThrowIfCancellationRequested();

        // Simple heuristics for auto-detection
        if (fileSize > 5_000_000) // > 5MB
        {
          DetectedEngine = "xtts";
          DetectedQualityMode = "high";
        }
        else if (fileSize > 1_000_000) // > 1MB
        {
          DetectedEngine = "xtts";
          DetectedQualityMode = "standard";
        }
        else
        {
          DetectedEngine = "xtts";
          DetectedQualityMode = "fast";
        }

        // Generate default profile name from filename
        if (string.IsNullOrWhiteSpace(ProfileName))
        {
          var nameWithoutExtension = SelectedAudioFile.Name;
          var lastDot = nameWithoutExtension.LastIndexOf('.');
          if (lastDot > 0)
          {
            nameWithoutExtension = nameWithoutExtension.Substring(0, lastDot);
          }
          ProfileName = $"Quick Clone - {nameWithoutExtension}";
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception)
      {
        // Auto-detection failure is not critical - don't report as error
        DetectedEngine = "xtts";
        DetectedQualityMode = "standard";
      }
    }

    private async Task QuickCloneAsync(CancellationToken cancellationToken)
    {
      var selectedAudioFile = SelectedAudioFile;
      if (selectedAudioFile == null || IsProcessing)
      {
        return;
      }

      try
      {
        IsProcessing = true;
        IsLoading = true;
        ErrorMessage = null;
        cancellationToken.ThrowIfCancellationRequested();
        ProcessingStatus = ResourceHelper.GetString("VoiceQuickClone.UploadingAudio", "Uploading audio...");
        ProcessingProgress = 0.1f;

        // Upload audio file
        await using var audioStream = await selectedAudioFile.OpenStreamForReadAsync();
        cancellationToken.ThrowIfCancellationRequested();
        ProcessingProgress = 0.2f;
        ProcessingStatus = ResourceHelper.GetString("VoiceQuickClone.AnalyzingAudio", "Analyzing audio...");

        // Clone voice using the existing endpoint
        var engine = DetectedEngine ?? "xtts";
        var qualityMode = DetectedQualityMode ?? "standard";
        if (string.IsNullOrWhiteSpace(ProfileName))
        {
          ProfileName = $"Quick Clone - {DateTime.Now:yyyyMMdd_HHmmss}";
        }

        ProcessingProgress = 0.3f;
        ProcessingStatus = ResourceHelper.GetString("VoiceQuickClone.CloningVoice", "Cloning voice...");

        // Create clone request
        var cloneRequest = new VoiceCloneRequest
        {
          Engine = engine,
          QualityMode = qualityMode,
          Text = null, // Quick clone doesn't synthesize text
          ProfileName = ProfileName // Pass user-provided profile name
        };

        // Call the clone endpoint
        var cloneResponse = await _backendClient.CloneVoiceAsync(
            audioStream,
            cloneRequest,
            cancellationToken
        );

        if (cloneResponse != null)
        {
          ProcessingProgress = 0.9f;
          ProcessingStatus = ResourceHelper.GetString("VoiceQuickClone.Finalizing", "Finalizing...");

          CreatedProfileId = cloneResponse.ProfileId;
          CreatedProfileUrl = cloneResponse.AudioUrl;
          QualityScore = (float)cloneResponse.QualityScore;

          ProcessingProgress = 1.0f;
          ProcessingStatus = ResourceHelper.GetString("VoiceQuickClone.Complete", "Complete!");
          StatusMessage = ResourceHelper.FormatString("VoiceQuickClone.CloningSuccess", CreatedProfileId);
        }
      }
      catch (OperationCanceledException)
      {
        ProcessingStatus = ResourceHelper.GetString("VoiceQuickClone.Cancelled", "Cancelled");
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "QuickClone");
        ProcessingStatus = ResourceHelper.GetString("VoiceQuickClone.Failed", "Failed");
      }
      finally
      {
        IsProcessing = false;
        IsLoading = false;
      }
    }

    private async Task ResetAsync(CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();

      SelectedAudioFile = null;
      AudioFileName = null;
      DetectedEngine = null;
      DetectedQualityMode = null;
      ProfileName = null;
      ProcessingProgress = 0.0f;
      ProcessingStatus = null;
      CreatedProfileId = null;
      CreatedProfileUrl = null;
      QualityScore = null;
      ErrorMessage = null;
      StatusMessage = null;

      await Task.CompletedTask;
    }

    /// <summary>
    /// Handles the CloneReferenceSelectedEvent from Library panel.
    /// Loads the selected audio file for quick cloning.
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
          "VoiceQuickClone.LoadedFromLibrary",
          $"Loaded '{e.AssetName ?? Path.GetFileName(e.AssetPath)}' for cloning"
        );
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Error loading clone reference: {ex.Message}");
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
      SelectedAudioFile = file;
      
      // Set profile name from display name or filename
      if (!string.IsNullOrWhiteSpace(displayName))
      {
        ProfileName = $"Quick Clone - {displayName}";
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
  }
}