using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Utilities;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the EmotionStyleControlView panel - Emotion/style control.
  /// </summary>
  public partial class EmotionStyleControlViewModel : BaseViewModel, IPanelView
  {
    private readonly IBackendClient _backendClient;

    public string PanelId => "emotion-style-control";
    public string DisplayName => ResourceHelper.GetString("Panel.EmotionStyleControl.DisplayName", "Emotion & Style Control");
    public PanelRegion Region => PanelRegion.Right;

    [ObservableProperty]
    private ObservableCollection<EmotionStylePresetItem> emotionPresets = new();

    [ObservableProperty]
    private ObservableCollection<StylePresetItem> stylePresets = new();

    [ObservableProperty]
    private EmotionStylePresetItem? selectedEmotionPreset;

    [ObservableProperty]
    private StylePresetItem? selectedStylePreset;

    [ObservableProperty]
    private string? selectedProfileId;

    [ObservableProperty]
    private string text = string.Empty;

    [ObservableProperty]
    private string? customEmotion;

    [ObservableProperty]
    private string? customStyle;

    [ObservableProperty]
    private double intensity = 0.5;

    [ObservableProperty]
    private ObservableCollection<string> availableProfiles = new();

    [ObservableProperty]
    private ObservableCollection<string> availableEmotions = new() { "happy", "sad", "angry", "neutral", "excited", "calm", "fearful", "disgusted", "surprised" };

    [ObservableProperty]
    private ObservableCollection<string> availableStyles = new() { "formal", "casual", "narrative", "conversational", "dramatic", "whisper", "shout" };

    public EmotionStyleControlViewModel(IViewModelContext context, IBackendClient backendClient)
        : base(context)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));

      LoadEmotionPresetsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadEmotionPresets");
        await LoadEmotionPresetsAsync(ct);
      }, () => !IsLoading);
      LoadStylePresetsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadStylePresets");
        await LoadStylePresetsAsync(ct);
      }, () => !IsLoading);
      ApplyCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ApplyEmotionStyle");
        await ApplyEmotionStyleAsync(ct);
      }, () => !IsLoading);
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      }, () => !IsLoading);

      // Load initial data
      _ = LoadEmotionPresetsAsync(CancellationToken.None);
      _ = LoadStylePresetsAsync(CancellationToken.None);
    }

    public IAsyncRelayCommand LoadEmotionPresetsCommand { get; }
    public IAsyncRelayCommand LoadStylePresetsCommand { get; }
    public IAsyncRelayCommand ApplyCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    private async Task LoadEmotionPresetsAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var presets = await _backendClient.SendRequestAsync<object, EmotionPreset[]>(
            "/api/emotion-style/emotions",
            null,
            System.Net.Http.HttpMethod.Get,
            cancellationToken
        );

        EmotionPresets.Clear();
        if (presets != null)
        {
          foreach (var preset in presets)
          {
            EmotionPresets.Add(new EmotionStylePresetItem(preset));
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadEmotionPresets");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadStylePresetsAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var presets = await _backendClient.SendRequestAsync<object, StylePreset[]>(
            "/api/emotion-style/styles",
            null,
            System.Net.Http.HttpMethod.Get,
            cancellationToken
        );

        StylePresets.Clear();
        if (presets != null)
        {
          foreach (var preset in presets)
          {
            StylePresets.Add(new StylePresetItem(preset));
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadStylePresets");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ApplyEmotionStyleAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SelectedProfileId))
      {
        ErrorMessage = ResourceHelper.GetString("EmotionStyleControl.ProfileRequired", "Profile must be selected");
        return;
      }

      if (string.IsNullOrWhiteSpace(Text))
      {
        ErrorMessage = ResourceHelper.GetString("EmotionStyleControl.TextRequired", "Text is required");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new
        {
          profile_id = SelectedProfileId,
          text = Text,
          emotion_preset_id = SelectedEmotionPreset?.Id,
          style_preset_id = SelectedStylePreset?.Id,
          emotion = CustomEmotion,
          style = CustomStyle,
          intensity = Intensity
        };

        var response = await _backendClient.SendRequestAsync<object, EmotionStyleApplyResponse>(
            "/api/emotion-style/apply",
            request,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        StatusMessage = response?.Message ?? ResourceHelper.GetString("EmotionStyleControl.EmotionStyleApplied", "Emotion/style applied");
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "ApplyEmotionStyle");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
      await LoadEmotionPresetsAsync(cancellationToken);
      await LoadStylePresetsAsync(cancellationToken);
      StatusMessage = ResourceHelper.GetString("EmotionStyleControl.PresetsRefreshed", "Presets refreshed");
    }

    // Response models
    private class EmotionStyleApplyResponse
    {
      public string AudioId { get; set; } = string.Empty;
      public string Message { get; set; } = string.Empty;
    }
  }

  // Data models
  public class EmotionPreset
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Emotion { get; set; } = string.Empty;
    public double Intensity { get; set; }
    public System.Collections.Generic.Dictionary<string, double> Parameters { get; set; } = new();
    public string Created { get; set; } = string.Empty;
  }

  public class StylePreset
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Style { get; set; } = string.Empty;
    public System.Collections.Generic.Dictionary<string, double> Parameters { get; set; } = new();
    public string Created { get; set; } = string.Empty;
  }

  public class EmotionStylePresetItem : ObservableObject
  {
    public string Id { get; set; }
    public string Name { get; set; }
    public string Emotion { get; set; }
    public double Intensity { get; set; }
    public System.Collections.Generic.Dictionary<string, double> Parameters { get; set; }
    public string Created { get; set; }

    public EmotionStylePresetItem(EmotionPreset preset)
    {
      Id = preset.Id;
      Name = preset.Name;
      Emotion = preset.Emotion;
      Intensity = preset.Intensity;
      Parameters = preset.Parameters;
      Created = preset.Created;
    }
  }

  public class StylePresetItem : ObservableObject
  {
    public string Id { get; set; }
    public string Name { get; set; }
    public string Style { get; set; }
    public System.Collections.Generic.Dictionary<string, double> Parameters { get; set; }
    public string Created { get; set; }

    public StylePresetItem(StylePreset preset)
    {
      Id = preset.Id;
      Name = preset.Name;
      Style = preset.Style;
      Parameters = preset.Parameters;
      Created = preset.Created;
    }
  }
}