using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// ViewModel for Emotion/Style Preset Visual Editor.
  /// Implements IDEA 31: Emotion/Style Preset Visual Editor.
  /// </summary>
  public partial class EmotionStylePresetEditorViewModel : ObservableObject
  {
    private readonly IBackendClient _backendClient;

    [ObservableProperty]
    private ObservableCollection<EmotionStylePreset> presets = new();

    [ObservableProperty]
    private EmotionStylePreset? selectedPreset;

    [ObservableProperty]
    private string? searchQuery;

    [ObservableProperty]
    private string presetName = string.Empty;

    [ObservableProperty]
    private string presetDescription = string.Empty;

    [ObservableProperty]
    private ObservableCollection<SelectedEmotion> selectedEmotions = new();

    [ObservableProperty]
    private double speakingRate = 1.0;

    [ObservableProperty]
    private double pitch;

    [ObservableProperty]
    private double energy = 50.0;

    [ObservableProperty]
    private double pauseDuration = 100.0;

    [ObservableProperty]
    private string previewText = "Hello, this is a preview of the emotion and style preset.";

    public bool HasSelectedEmotions => SelectedEmotions.Count > 0;

    private static readonly Dictionary<string, EmotionInfo> EmotionDefinitions = new()
        {
            { "neutral", new EmotionInfo { Name = "Neutral", Description = "Neutral, balanced tone" } },
            { "happy", new EmotionInfo { Name = "Happy", Description = "Cheerful, upbeat tone" } },
            { "sad", new EmotionInfo { Name = "Sad", Description = "Melancholic, somber tone" } },
            { "excited", new EmotionInfo { Name = "Excited", Description = "Energetic, enthusiastic tone" } },
            { "angry", new EmotionInfo { Name = "Angry", Description = "Intense, forceful tone" } },
            { "calm", new EmotionInfo { Name = "Calm", Description = "Peaceful, relaxed tone" } }
        };

    public EmotionStylePresetEditorViewModel(IBackendClient backendClient)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
      CreatePresetCommand = new AsyncRelayCommand(CreatePresetAsync, () => !string.IsNullOrWhiteSpace(PresetName));
      SavePresetCommand = new AsyncRelayCommand(SavePresetAsync, () => SelectedPreset != null && !string.IsNullOrWhiteSpace(PresetName));
      DeletePresetCommand = new AsyncRelayCommand(DeletePresetAsync, () => SelectedPreset != null);
      PreviewPresetCommand = new AsyncRelayCommand(PreviewPresetAsync, () => !string.IsNullOrWhiteSpace(PreviewText));
      ApplyToSynthesisCommand = new RelayCommand(ApplyToSynthesis, () => SelectedPreset != null);

      _ = LoadPresetsAsync();
    }

    public IAsyncRelayCommand CreatePresetCommand { get; }
    public IAsyncRelayCommand SavePresetCommand { get; }
    public IAsyncRelayCommand DeletePresetCommand { get; }
    public IAsyncRelayCommand PreviewPresetCommand { get; }
    public IRelayCommand ApplyToSynthesisCommand { get; }

    partial void OnPresetNameChanged(string value)
    {
      CreatePresetCommand.NotifyCanExecuteChanged();
      SavePresetCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedPresetChanged(EmotionStylePreset? value)
    {
      if (value != null)
      {
        PresetName = value.Name;
        PresetDescription = value.Description ?? string.Empty;
        SpeakingRate = value.SpeakingRate;
        Pitch = value.Pitch;
        Energy = value.Energy;
        PauseDuration = value.PauseDuration;

        SelectedEmotions.Clear();
        foreach (var emotion in value.Emotions)
        {
          SelectedEmotions.Add(new SelectedEmotion
          {
            Name = emotion.Name,
            Description = EmotionDefinitions.GetValueOrDefault(emotion.Name.ToLower())?.Description ?? string.Empty,
            Intensity = emotion.Intensity
          });
        }

        OnPropertyChanged(nameof(HasSelectedEmotions));
      }
      else
      {
        PresetName = string.Empty;
        PresetDescription = string.Empty;
        SelectedEmotions.Clear();
        OnPropertyChanged(nameof(HasSelectedEmotions));
      }

      SavePresetCommand.NotifyCanExecuteChanged();
      DeletePresetCommand.NotifyCanExecuteChanged();
      ApplyToSynthesisCommand.NotifyCanExecuteChanged();
    }

    partial void OnPreviewTextChanged(string value)
    {
      PreviewPresetCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadPresetsAsync()
    {
      try
      {
        var backendPresets = await _backendClient.GetEmotionPresetsAsync();

        Presets.Clear();
        foreach (var backendPreset in backendPresets)
        {
          var preset = ConvertFromBackendPreset(backendPreset);
          Presets.Add(preset);
        }

        // If no presets exist, add a default one
        if (Presets.Count == 0)
        {
          Presets.Add(new EmotionStylePreset
          {
            Id = "default-neutral",
            Name = "Neutral",
            Description = "Default neutral emotion preset",
            Emotions = new List<EmotionSetting> { new() { Name = "neutral", Intensity = 100 } },
            SpeakingRate = 1.0,
            Pitch = 0.0,
            Energy = 50.0,
            PauseDuration = 100.0
          });
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Error loading emotion presets: {ex.Message}");
        // Fallback to default preset on error
        Presets.Clear();
        Presets.Add(new EmotionStylePreset
        {
          Id = "default-neutral",
          Name = "Neutral",
          Description = "Default neutral emotion preset",
          Emotions = new List<EmotionSetting> { new() { Name = "neutral", Intensity = 100 } },
          SpeakingRate = 1.0,
          Pitch = 0.0,
          Energy = 50.0,
          PauseDuration = 100.0
        });
      }
    }

    private EmotionStylePreset ConvertFromBackendPreset(EmotionPreset backendPreset)
    {
      var emotions = new List<EmotionSetting>();
      if (!string.IsNullOrEmpty(backendPreset.PrimaryEmotion))
      {
        emotions.Add(new EmotionSetting
        {
          Name = backendPreset.PrimaryEmotion,
          Intensity = (int)backendPreset.PrimaryIntensity
        });
      }
      if (!string.IsNullOrEmpty(backendPreset.SecondaryEmotion))
      {
        emotions.Add(new EmotionSetting
        {
          Name = backendPreset.SecondaryEmotion,
          Intensity = (int)backendPreset.SecondaryIntensity
        });
      }

      return new EmotionStylePreset
      {
        Id = backendPreset.PresetId,
        Name = backendPreset.Name,
        Description = backendPreset.Description,
        Emotions = emotions,
        SpeakingRate = 1.0, // Backend doesn't store style parameters yet
        Pitch = 0.0,
        Energy = 50.0,
        PauseDuration = 100.0
      };
    }

    private EmotionPresetCreateRequest ConvertToBackendCreateRequest(EmotionStylePreset preset)
    {
      var primaryEmotion = preset.Emotions.FirstOrDefault()?.Name ?? "neutral";
      var primaryIntensity = preset.Emotions.FirstOrDefault()?.Intensity ?? 100;
      var secondaryEmotion = preset.Emotions.Count > 1 ? preset.Emotions[1].Name : null;
      var secondaryIntensity = preset.Emotions.Count > 1 ? preset.Emotions[1].Intensity : 0;

      return new EmotionPresetCreateRequest
      {
        Name = preset.Name,
        Description = preset.Description,
        PrimaryEmotion = primaryEmotion,
        PrimaryIntensity = primaryIntensity,
        SecondaryEmotion = secondaryEmotion,
        SecondaryIntensity = secondaryIntensity
      };
    }

    private EmotionPresetUpdateRequest ConvertToBackendUpdateRequest(EmotionStylePreset preset)
    {
      var primaryEmotion = preset.Emotions.FirstOrDefault()?.Name ?? "neutral";
      var primaryIntensity = preset.Emotions.FirstOrDefault()?.Intensity ?? 100;
      var secondaryEmotion = preset.Emotions.Count > 1 ? preset.Emotions[1].Name : null;
      var secondaryIntensity = preset.Emotions.Count > 1 ? preset.Emotions[1].Intensity : 0;

      return new EmotionPresetUpdateRequest
      {
        Name = preset.Name,
        Description = preset.Description,
        PrimaryEmotion = primaryEmotion,
        PrimaryIntensity = primaryIntensity,
        SecondaryEmotion = secondaryEmotion,
        SecondaryIntensity = secondaryIntensity
      };
    }

    private async Task CreatePresetAsync()
    {
      try
      {
        var newPreset = new EmotionStylePreset
        {
          Id = Guid.NewGuid().ToString(),
          Name = PresetName,
          Description = PresetDescription,
          Emotions = SelectedEmotions.Select(e => new EmotionSetting { Name = e.Name, Intensity = e.Intensity }).ToList(),
          SpeakingRate = SpeakingRate,
          Pitch = Pitch,
          Energy = Energy,
          PauseDuration = PauseDuration
        };

        // Save to backend
        var createRequest = ConvertToBackendCreateRequest(newPreset);
        var backendPreset = await _backendClient.CreateEmotionPresetAsync(createRequest);

        // Update with backend ID
        newPreset.Id = backendPreset.PresetId;
        Presets.Add(newPreset);
        SelectedPreset = newPreset;

        PresetName = string.Empty;
        PresetDescription = string.Empty;
        SelectedEmotions.Clear();
        SpeakingRate = 1.0;
        Pitch = 0.0;
        Energy = 50.0;
        PauseDuration = 100.0;
        OnPropertyChanged(nameof(HasSelectedEmotions));
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Error creating emotion preset: {ex.Message}");
        // Show error to user (could use ToastNotificationService)
      }
    }

    private async Task SavePresetAsync()
    {
      if (SelectedPreset == null)
        return;

      try
      {
        SelectedPreset.Name = PresetName;
        SelectedPreset.Description = PresetDescription;
        SelectedPreset.Emotions = SelectedEmotions.Select(e => new EmotionSetting { Name = e.Name, Intensity = e.Intensity }).ToList();
        SelectedPreset.SpeakingRate = SpeakingRate;
        SelectedPreset.Pitch = Pitch;
        SelectedPreset.Energy = Energy;
        SelectedPreset.PauseDuration = PauseDuration;

        // Update in backend
        var updateRequest = ConvertToBackendUpdateRequest(SelectedPreset);
        await _backendClient.UpdateEmotionPresetAsync(SelectedPreset.Id, updateRequest);

        OnPropertyChanged(nameof(Presets));
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Error saving emotion preset: {ex.Message}");
        // Show error to user (could use ToastNotificationService)
      }
    }

    private async Task DeletePresetAsync()
    {
      if (SelectedPreset == null)
        return;

      try
      {
        // Delete from backend
        await _backendClient.DeleteEmotionPresetAsync(SelectedPreset.Id);

        Presets.Remove(SelectedPreset);
        SelectedPreset = null;
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Error deleting emotion preset: {ex.Message}");
        // Show error to user (could use ToastNotificationService)
      }
    }

    public void AddEmotion(string emotionName)
    {
      if (EmotionDefinitions.ContainsKey(emotionName.ToLower()) &&
          !SelectedEmotions.Any(e => e.Name.Equals(emotionName, StringComparison.OrdinalIgnoreCase)))
      {
        var emotionInfo = EmotionDefinitions[emotionName.ToLower()];
        SelectedEmotions.Add(new SelectedEmotion
        {
          Name = emotionInfo.Name,
          Description = emotionInfo.Description,
          Intensity = 100
        });
        OnPropertyChanged(nameof(HasSelectedEmotions));
      }
    }

    public void RemoveEmotion(SelectedEmotion emotion)
    {
      SelectedEmotions.Remove(emotion);
      OnPropertyChanged(nameof(HasSelectedEmotions));
    }

    private async Task PreviewPresetAsync()
    {
      if (string.IsNullOrWhiteSpace(PreviewText))
        return;

      try
      {
        // Get the primary emotion from the preset or current selection
        var primaryEmotion = SelectedEmotions.FirstOrDefault()?.Name ?? "neutral";

        // Build synthesis request with emotion parameter
        var request = new VoiceSynthesisRequest
        {
          Text = PreviewText,
          ProfileId = string.Empty,  // Use default profile for preview
          Engine = "piper",  // Use fast engine for preview
          Emotion = primaryEmotion,
          EnhanceQuality = false  // Skip quality enhancement for quick preview
        };

        // Synthesize preview audio
        var response = await _backendClient.SynthesizeVoiceAsync(request);
        
        if (response != null && !string.IsNullOrEmpty(response.AudioId))
        {
          // Log the preview audio ID for playback
          // In a full implementation, this would play the audio via IAudioPlayerService
          System.Diagnostics.Debug.WriteLine($"Preview generated: {response.AudioId}, Duration: {response.Duration}s");
          
          // Future enhancement: Store the audio ID and play via audio player service
          // _lastPreviewAudioId = response.AudioId;
          // await _audioPlayer.PlayAsync(response.AudioUrl);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Error previewing preset: {ex.Message}");
      }
    }

    private void ApplyToSynthesis()
    {
      if (SelectedPreset == null)
        return;

      // Apply preset to VoiceSynthesisView
      // This would set the emotion and style parameters in the synthesis view
      // In a full implementation, this would:
      // 1. Get reference to VoiceSynthesisViewModel
      // 2. Set emotion parameters from preset
      // 3. Set style parameters (speaking rate, pitch, energy, pause duration)

      System.Diagnostics.Debug.WriteLine($"Apply preset '{SelectedPreset.Name}' to synthesis");
    }
  }

  public class EmotionStylePreset
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<EmotionSetting> Emotions { get; set; } = new();
    public double SpeakingRate { get; set; } = 1.0;
    public double Pitch { get; set; }
    public double Energy { get; set; } = 50.0;
    public double PauseDuration { get; set; } = 100.0;

    public string EmotionSummary => string.Join(", ", Emotions.Select(e => $"{e.Name} ({e.Intensity}%)"));
  }

  public class EmotionSetting
  {
    public string Name { get; set; } = string.Empty;
    public int Intensity { get; set; } = 100;
  }

  public class SelectedEmotion
  {
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Intensity { get; set; } = 100;
  }

  public class EmotionInfo
  {
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
  }
}