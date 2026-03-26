using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the VoiceStyleTransferView panel - Voice style transfer from reference audio.
  /// </summary>
  public partial class VoiceStyleTransferViewModel : BaseViewModel, IPanelView
  {
    private readonly IVoiceStyleTransferClient _voiceStyleTransferClient;
    private readonly IProfilesClient _profilesClient;
    private readonly ToastNotificationService? _toastNotificationService;

    public string PanelId => PanelIds.VoiceStyleTransfer;
    public string DisplayName => ResourceHelper.GetString("Panel.VoiceStyleTransfer.DisplayName", "Voice Style Transfer");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private string? referenceAudioId;

    [ObservableProperty]
    private string? referenceAudioUrl;

    [ObservableProperty]
    private StyleProfileItem? styleProfile;

    [ObservableProperty]
    private bool isExtractingStyle;

    [ObservableProperty]
    private string? targetVoiceProfileId;

    [ObservableProperty]
    private ObservableCollection<string> availableVoiceProfiles = new();

    [ObservableProperty]
    private string? targetText;

    [ObservableProperty]
    private float styleIntensity = 0.8f;

    [ObservableProperty]
    private bool isGenerating;

    [ObservableProperty]
    private string? generatedAudioId;

    [ObservableProperty]
    private string? generatedAudioUrl;

    [ObservableProperty]
    private StyleAnalysisItem? styleAnalysis;

    [ObservableProperty]
    private bool showComparison;

    public VoiceStyleTransferViewModel(IViewModelContext context, IVoiceStyleTransferClient voiceStyleTransferClient, IProfilesClient profilesClient)
        : base(context)
    {
      _voiceStyleTransferClient = voiceStyleTransferClient ?? throw new ArgumentNullException(nameof(voiceStyleTransferClient));
      _profilesClient = profilesClient ?? throw new ArgumentNullException(nameof(profilesClient));

      // Get toast notification service using helper (reduces code duplication)
      _toastNotificationService = ServiceInitializationHelper.TryGetService(() => AppServices.TryGetToastNotificationService());

      ExtractStyleCommand = new AsyncRelayCommand(ExtractStyleAsync, () => !string.IsNullOrWhiteSpace(ReferenceAudioId) && !IsExtractingStyle);
      AnalyzeStyleCommand = new AsyncRelayCommand(AnalyzeStyleAsync, () => !string.IsNullOrWhiteSpace(ReferenceAudioId));
      GenerateCommand = new AsyncRelayCommand(GenerateAsync, () => !string.IsNullOrWhiteSpace(TargetVoiceProfileId) && !string.IsNullOrWhiteSpace(TargetText) && !IsGenerating);
      LoadVoiceProfilesCommand = new AsyncRelayCommand(LoadVoiceProfilesAsync);
    }

    public IAsyncRelayCommand ExtractStyleCommand { get; }
    public IAsyncRelayCommand AnalyzeStyleCommand { get; }
    public IAsyncRelayCommand GenerateCommand { get; }
    public IAsyncRelayCommand LoadVoiceProfilesCommand { get; }

    partial void OnReferenceAudioIdChanged(string? value)
    {
      ExtractStyleCommand.NotifyCanExecuteChanged();
      AnalyzeStyleCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsExtractingStyleChanged(bool value)
    {
      ExtractStyleCommand.NotifyCanExecuteChanged();
    }

    partial void OnTargetVoiceProfileIdChanged(string? value)
    {
      GenerateCommand.NotifyCanExecuteChanged();
    }

    partial void OnTargetTextChanged(string? value)
    {
      GenerateCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsGeneratingChanged(bool value)
    {
      GenerateCommand.NotifyCanExecuteChanged();
    }

    private async Task ExtractStyleAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(ReferenceAudioId) || IsExtractingStyle)
      {
        return;
      }

      IsExtractingStyle = true;
      ErrorMessage = null;

      try
      {
        var request = new VoiceStyleTransferExtractRequest
        {
          AudioId = ReferenceAudioId ?? "",
          AnalyzeProsody = true,
          AnalyzeEmotion = true
        };

        var response = await _voiceStyleTransferClient.ExtractStyleAsync(request, cancellationToken);

        if (response != null)
        {
          StyleProfile = new StyleProfileItem(response);
          StatusMessage = ResourceHelper.GetString("VoiceStyleTransfer.StyleExtracted", "Style extracted successfully");
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.GetString("VoiceStyleTransfer.StyleExtracted", "Style extracted successfully"),
              ResourceHelper.GetString("Toast.Title.StyleExtractionComplete", "Style Extraction Complete"));
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "ExtractStyle");
      }
      finally
      {
        IsExtractingStyle = false;
      }
    }

    private async Task AnalyzeStyleAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(ReferenceAudioId))
      {
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new VoiceStyleTransferAnalyzeRequest
        {
          AudioId = ReferenceAudioId ?? ""
        };

        var response = await _voiceStyleTransferClient.AnalyzeStyleAsync(request, cancellationToken);

        if (response != null)
        {
          StyleAnalysis = new StyleAnalysisItem(response);
          StatusMessage = ResourceHelper.GetString("VoiceStyleTransfer.StyleAnalysisComplete", "Style analysis complete");
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("VoiceStyleTransfer.StyleAnalysisCompleteWithMarkers", StyleAnalysis.MarkerCount),
              ResourceHelper.GetString("Toast.Title.StyleAnalysisComplete", "Style Analysis Complete"));
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "AnalyzeStyle");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task GenerateAsync()
    {
      if (string.IsNullOrWhiteSpace(TargetVoiceProfileId) || string.IsNullOrWhiteSpace(TargetText) || IsGenerating)
      {
        return;
      }

      try
      {
        IsGenerating = true;
        ErrorMessage = null;

        var request = new VoiceStyleTransferSynthesizeRequest
        {
          VoiceProfileId = TargetVoiceProfileId ?? "",
          Text = TargetText ?? "",
          ReferenceAudioId = ReferenceAudioId,
          StyleEmbedding = StyleProfile?.StyleEmbedding,
          StyleIntensity = StyleIntensity,
          Language = "en"
        };

        var response = await _voiceStyleTransferClient.SynthesizeStyleAsync(request);

        if (response != null)
        {
          GeneratedAudioId = response.AudioId;
          GeneratedAudioUrl = response.AudioUrl;
          StatusMessage = $"Generated audio with style transfer: {response.Duration:F2}s";
          _toastNotificationService?.ShowSuccess($"Audio generated successfully! Duration: {response.Duration:F2}s", "Style Transfer Complete");
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("VoiceStyleTransfer.GenerateAudioFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("VoiceStyleTransfer.GenerateAudioFailed", ex.Message),
            ResourceHelper.GetString("Toast.Title.GenerationFailed", "Generation Failed"));
      }
      finally
      {
        IsGenerating = false;
      }
    }

    private async Task LoadVoiceProfilesAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var profiles = await _profilesClient.GetProfilesAsync(cancellationToken);

        AvailableVoiceProfiles.Clear();
        foreach (var profile in profiles)
        {
          AvailableVoiceProfiles.Add(profile.Id ?? profile.Name ?? "");
        }
        _toastNotificationService?.ShowInfo($"Loaded {AvailableVoiceProfiles.Count} voice profile(s)", "Profiles Loaded");
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadVoiceProfiles");
      }
      finally
      {
        IsLoading = false;
      }
    }

  }

  // Data models
  public class StyleProfileItem : ObservableObject
  {
    public string AudioId { get; set; }
    public float AveragePitch { get; set; }
    public float PitchVariation { get; set; }
    public float Energy { get; set; }
    public float SpeakingRate { get; set; }
    public string? EmotionTag { get; set; }
    public Dictionary<string, object>? ProsodicFeatures { get; set; }
    public List<float>? StyleEmbedding { get; set; }

    public string PitchDisplay => ResourceHelper.FormatString("VoiceStyleTransfer.PitchDisplay", AveragePitch, PitchVariation);
    public string EnergyDisplay => $"{Energy:P0}";
    public string SpeakingRateDisplay => ResourceHelper.FormatString("VoiceStyleTransfer.SpeakingRateDisplay", SpeakingRate);
    public string EmotionDisplay => EmotionTag ?? ResourceHelper.GetString("VoiceStyleTransfer.Neutral", "Neutral");
    public bool HasEmotion => !string.IsNullOrWhiteSpace(EmotionTag);

    public StyleProfileItem(VoiceStyleTransferProfileResponse response)
    {
      AudioId = response.AudioId;
      AveragePitch = response.AveragePitch;
      PitchVariation = response.PitchVariation;
      Energy = response.Energy;
      SpeakingRate = response.SpeakingRate;
      EmotionTag = response.EmotionTag;
      ProsodicFeatures = response.ProsodicFeatures;
      StyleEmbedding = response.StyleEmbedding;
    }
  }

  public class StyleAnalysisItem : ObservableObject
  {
    public string AudioId { get; set; }
    public List<float>? PitchContour { get; set; }
    public List<float>? EnergyContour { get; set; }
    public Dictionary<string, object>? TimingPatterns { get; set; }
    public List<Dictionary<string, object>>? StyleMarkers { get; set; }

    public int MarkerCount => StyleMarkers?.Count ?? 0;
    public bool HasMarkers => MarkerCount > 0;

    public StyleAnalysisItem(VoiceStyleTransferAnalyzeResponse response)
    {
      AudioId = response.AudioId;
      PitchContour = response.PitchContour;
      EnergyContour = response.EnergyContour;
      TimingPatterns = response.TimingPatterns;
      StyleMarkers = response.StyleMarkers;
    }
  }
}