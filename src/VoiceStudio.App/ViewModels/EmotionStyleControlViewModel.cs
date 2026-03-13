using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Models;
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
    private readonly IEmotionStyleClient _emotionStyleClient;

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

    public EmotionStyleControlViewModel(IViewModelContext context, IEmotionStyleClient emotionStyleClient)
        : base(context)
    {
      _emotionStyleClient = emotionStyleClient ?? throw new ArgumentNullException(nameof(emotionStyleClient));

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
    }

    /// <summary>
    /// Initialize panel data. Call from view Loaded event (ADR-047).
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
      await LoadEmotionPresetsAsync(ct).ConfigureAwait(false);
      await LoadStylePresetsAsync(ct).ConfigureAwait(false);
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
        var presets = await _emotionStyleClient.GetEmotionPresetsAsync(cancellationToken).ConfigureAwait(false);

        EmotionPresets.Clear();
        foreach (var preset in presets)
        {
          EmotionPresets.Add(new EmotionStylePresetItem(preset));
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
        var presets = await _emotionStyleClient.GetStylePresetsAsync(cancellationToken).ConfigureAwait(false);

        StylePresets.Clear();
        foreach (var preset in presets)
        {
          StylePresets.Add(new StylePresetItem(preset));
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
        var request = new EmotionStyleApplyRequest
        {
          ProfileId = SelectedProfileId ?? string.Empty,
          Text = Text,
          EmotionPresetId = SelectedEmotionPreset?.Id,
          StylePresetId = SelectedStylePreset?.Id,
          Emotion = CustomEmotion,
          Style = CustomStyle,
          Intensity = Intensity
        };

        var response = await _emotionStyleClient.ApplyEmotionStyleAsync(request, cancellationToken).ConfigureAwait(false);

        StatusMessage = response.Message;
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

  }

  // Display models for emotion/style presets (wrap API types for binding)
  public class EmotionStylePresetItem : ObservableObject
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Emotion { get; set; } = string.Empty;
    public double Intensity { get; set; }
    public System.Collections.Generic.Dictionary<string, double> Parameters { get; set; } = new();
    public string Created { get; set; } = string.Empty;

    public EmotionStylePresetItem(EmotionStyleEmotionPreset preset)
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
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Style { get; set; } = string.Empty;
    public System.Collections.Generic.Dictionary<string, double> Parameters { get; set; } = new();
    public string Created { get; set; } = string.Empty;

    public StylePresetItem(EmotionStyleStylePreset preset)
    {
      Id = preset.Id;
      Name = preset.Name;
      Style = preset.Style;
      Parameters = preset.Parameters;
      Created = preset.Created;
    }
  }
}