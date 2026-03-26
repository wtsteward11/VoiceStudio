using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the VoiceMorphingBlendingView panel - Voice morphing and blending.
  /// </summary>
  public partial class VoiceMorphingBlendingViewModel : BaseViewModel, IPanelView
  {
    private readonly IVoiceMorphingBlendingClient _voiceMorphingBlendingClient;
    private readonly IProfilesClient _profilesClient;
    private readonly ToastNotificationService? _toastNotificationService;

    public string PanelId => PanelIds.VoiceMorphingBlending;
    public string DisplayName => ResourceHelper.GetString("Panel.VoiceMorphingBlending.DisplayName", "Voice Morphing/Blending");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private string selectedMode = "Blend Voices";

    [ObservableProperty]
    private ObservableCollection<string> availableModes = new() { "Blend Voices", "Morph Timeline" };

    // Blend Voices Mode
    [ObservableProperty]
    private string? voiceAId;

    [ObservableProperty]
    private string? voiceBId;

    [ObservableProperty]
    private ObservableCollection<string> availableVoiceProfiles = new();

    [ObservableProperty]
    private float blendRatio = 0.5f;

    [ObservableProperty]
    private string? previewText = ResourceHelper.GetString("VoiceMorphingBlending.PreviewTextDefault", "Hello, this is a preview of the blended voice.");

    [ObservableProperty]
    private bool isBlending;

    [ObservableProperty]
    private string? blendedProfileId;

    [ObservableProperty]
    private string? previewAudioId;

    [ObservableProperty]
    private string? previewAudioUrl;

    [ObservableProperty]
    private bool saveAsProfile;

    // Morph Timeline Mode
    [ObservableProperty]
    private string? sourceAudioId;

    [ObservableProperty]
    private string? morphVoiceAId;

    [ObservableProperty]
    private string? morphVoiceBId;

    [ObservableProperty]
    private float startRatio;

    [ObservableProperty]
    private float endRatio = 1.0f;

    [ObservableProperty]
    private float morphSpeed = 1.0f;

    [ObservableProperty]
    private bool isMorphing;

    [ObservableProperty]
    private string? morphedAudioId;

    [ObservableProperty]
    private string? morphedAudioUrl;

    public VoiceMorphingBlendingViewModel(IViewModelContext context, IVoiceMorphingBlendingClient voiceMorphingBlendingClient, IProfilesClient profilesClient)
        : base(context)
    {
      _voiceMorphingBlendingClient = voiceMorphingBlendingClient ?? throw new ArgumentNullException(nameof(voiceMorphingBlendingClient));
      _profilesClient = profilesClient ?? throw new ArgumentNullException(nameof(profilesClient));

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

      LoadVoiceProfilesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadVoiceProfiles");
        await LoadVoiceProfilesAsync(ct);
      }, () => !IsLoading);
      PreviewBlendCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("PreviewBlend");
        await PreviewBlendAsync(ct);
      }, () => !string.IsNullOrWhiteSpace(VoiceAId) && !string.IsNullOrWhiteSpace(VoiceBId) && !IsBlending && !IsLoading);
      BlendVoicesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("BlendVoices");
        await BlendVoicesAsync(ct);
      }, () => !string.IsNullOrWhiteSpace(VoiceAId) && !string.IsNullOrWhiteSpace(VoiceBId) && !IsBlending && !IsLoading);
      MorphVoiceCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("MorphVoice");
        await MorphVoiceAsync(ct);
      }, () => !string.IsNullOrWhiteSpace(SourceAudioId) && !string.IsNullOrWhiteSpace(MorphVoiceAId) && !string.IsNullOrWhiteSpace(MorphVoiceBId) && !IsMorphing && !IsLoading);
    }

    public IAsyncRelayCommand LoadVoiceProfilesCommand { get; }
    public IAsyncRelayCommand PreviewBlendCommand { get; }
    public IAsyncRelayCommand BlendVoicesCommand { get; }
    public IAsyncRelayCommand MorphVoiceCommand { get; }

    partial void OnVoiceAIdChanged(string? value)
    {
      PreviewBlendCommand.NotifyCanExecuteChanged();
      BlendVoicesCommand.NotifyCanExecuteChanged();
    }

    partial void OnVoiceBIdChanged(string? value)
    {
      PreviewBlendCommand.NotifyCanExecuteChanged();
      BlendVoicesCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBlendingChanged(bool value)
    {
      PreviewBlendCommand.NotifyCanExecuteChanged();
      BlendVoicesCommand.NotifyCanExecuteChanged();
    }

    partial void OnSourceAudioIdChanged(string? value)
    {
      MorphVoiceCommand.NotifyCanExecuteChanged();
    }

    partial void OnMorphVoiceAIdChanged(string? value)
    {
      MorphVoiceCommand.NotifyCanExecuteChanged();
    }

    partial void OnMorphVoiceBIdChanged(string? value)
    {
      MorphVoiceCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsMorphingChanged(bool value)
    {
      MorphVoiceCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadVoiceProfilesAsync(CancellationToken cancellationToken)
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var profiles = await _profilesClient.GetProfilesAsync(cancellationToken);

        AvailableVoiceProfiles.Clear();
        foreach (var profile in profiles)
        {
          AvailableVoiceProfiles.Add(profile.Id ?? profile.Name ?? "");
        }
        _toastNotificationService?.ShowInfo(
            ResourceHelper.FormatString("VoiceMorphingBlending.ProfilesLoaded", AvailableVoiceProfiles.Count),
            ResourceHelper.GetString("Toast.Title.ProfilesLoaded", "Profiles Loaded"));
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("VoiceMorphingBlending.LoadProfilesFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.LoadFailed", "Load Failed"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task PreviewBlendAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(VoiceAId) || string.IsNullOrWhiteSpace(VoiceBId) || IsBlending)
      {
        return;
      }

      IsBlending = true;
      ErrorMessage = null;

      try
      {
        var text = PreviewText ?? ResourceHelper.GetString("VoiceMorphingBlending.PreviewTextDefault", "Hello, this is a preview of the blended voice.");
        var response = await _voiceMorphingBlendingClient.PreviewBlendAsync(VoiceAId!, VoiceBId!, BlendRatio, text, cancellationToken);

        if (response != null)
        {
          PreviewAudioId = response.PreviewAudioId;
          PreviewAudioUrl = response.PreviewAudioUrl;
          StatusMessage = ResourceHelper.FormatString("VoiceMorphingBlending.PreviewGenerated", response.Duration.ToString("F2"));
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("VoiceMorphingBlending.PreviewGeneratedDetail", response.Duration.ToString("F2")),
              ResourceHelper.GetString("Toast.Title.PreviewReady", "Preview Ready"));
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "PreviewBlend");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.PreviewFailed", "Preview Failed"),
            ResourceHelper.FormatString("VoiceMorphingBlending.PreviewBlendFailed", ex.Message));
      }
      finally
      {
        IsBlending = false;
      }
    }

    private async Task BlendVoicesAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(VoiceAId) || string.IsNullOrWhiteSpace(VoiceBId) || IsBlending)
      {
        return;
      }

      IsBlending = true;
      ErrorMessage = null;

      try
      {
        var response = await _voiceMorphingBlendingClient.BlendVoicesAsync(VoiceAId!, VoiceBId!, BlendRatio, PreviewText, SaveAsProfile, cancellationToken);

        if (response != null)
        {
          BlendedProfileId = response.BlendedProfileId;
          PreviewAudioId = response.PreviewAudioId;
          PreviewAudioUrl = response.PreviewAudioUrl;
          StatusMessage = SaveAsProfile && !string.IsNullOrWhiteSpace(BlendedProfileId)
              ? ResourceHelper.FormatString("VoiceMorphingBlending.BlendedVoiceSavedAsProfile", BlendedProfileId)
              : ResourceHelper.GetString("VoiceMorphingBlending.BlendedVoiceCreated", "Blended voice created");

          if (SaveAsProfile && !string.IsNullOrWhiteSpace(BlendedProfileId))
          {
            _toastNotificationService?.ShowSuccess(
                ResourceHelper.FormatString("VoiceMorphingBlending.BlendedVoiceSavedAsProfile", BlendedProfileId),
                ResourceHelper.GetString("Toast.Title.ProfileCreated", "Profile Created"));
          }
          else
          {
            _toastNotificationService?.ShowSuccess(
                ResourceHelper.GetString("VoiceMorphingBlending.BlendedVoiceCreated", "Blended voice created successfully"),
                ResourceHelper.GetString("Toast.Title.BlendComplete", "Blend Complete"));
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "BlendVoices");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.BlendFailed", "Blend Failed"),
            ResourceHelper.FormatString("VoiceMorphingBlending.BlendVoicesFailed", ex.Message));
      }
      finally
      {
        IsBlending = false;
      }
    }

    private async Task MorphVoiceAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(SourceAudioId) || string.IsNullOrWhiteSpace(MorphVoiceAId) || string.IsNullOrWhiteSpace(MorphVoiceBId) || IsMorphing)
      {
        return;
      }

      IsMorphing = true;
      ErrorMessage = null;

      try
      {
        var response = await _voiceMorphingBlendingClient.MorphVoiceAsync(SourceAudioId!, MorphVoiceAId!, MorphVoiceBId!, StartRatio, EndRatio, MorphSpeed, cancellationToken);

        if (response != null)
        {
          MorphedAudioId = response.MorphedAudioId;
          MorphedAudioUrl = response.MorphedAudioUrl;
          StatusMessage = ResourceHelper.FormatString("VoiceMorphingBlending.VoiceMorphed", response.Duration.ToString("F2"));
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("VoiceMorphingBlending.VoiceMorphedDetail", response.Duration.ToString("F2")),
              ResourceHelper.GetString("Toast.Title.MorphComplete", "Morph Complete"));
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "MorphVoice");
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("VoiceMorphingBlending.MorphVoiceFailed", ex.Message),
            ResourceHelper.GetString("Toast.Title.MorphFailed", "Morph Failed"));
      }
      finally
      {
        IsMorphing = false;
      }
    }

    // Request/Response models
    private class VoiceProfileData
    {
      public string? ProfileId { get; set; }
      public string? Name { get; set; }
    }

    // Public for IVoiceMorphingBlendingClient
    public class VoicePreviewRequest
    {
      public string? VoiceAId { get; set; }
      public string? VoiceBId { get; set; }
      public float? BlendRatio { get; set; }
      public string Text { get; set; } = string.Empty;
    }

    public class VoicePreviewResponse
    {
      public string PreviewAudioId { get; set; } = string.Empty;
      public string PreviewAudioUrl { get; set; } = string.Empty;
      public float Duration { get; set; }
    }

    public class VoiceBlendRequest
    {
      public string VoiceAId { get; set; } = string.Empty;
      public string VoiceBId { get; set; } = string.Empty;
      public float BlendRatio { get; set; }
      public string? Text { get; set; }
      public bool SaveProfile { get; set; }
    }

    public class VoiceBlendResponse
    {
      public string? BlendedProfileId { get; set; }
      public string? PreviewAudioId { get; set; }
      public string? PreviewAudioUrl { get; set; }
      public float BlendRatio { get; set; }
    }

    public class VoiceMorphRequest
    {
      public string SourceAudioId { get; set; } = string.Empty;
      public string VoiceAId { get; set; } = string.Empty;
      public string VoiceBId { get; set; } = string.Empty;
      public float StartRatio { get; set; }
      public float EndRatio { get; set; }
      public float MorphSpeed { get; set; }
    }

    public class VoiceMorphResponse
    {
      public string MorphedAudioId { get; set; } = string.Empty;
      public string MorphedAudioUrl { get; set; } = string.Empty;
      public float Duration { get; set; }
    }
  }
}