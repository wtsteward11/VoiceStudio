using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// ViewModel for Advanced Engine Parameter Tuning Interface.
  /// Implements IDEA 51: Advanced Engine Parameter Tuning Interface.
  /// </summary>
  public partial class EngineParameterTuningViewModel : ObservableObject
  {
    private readonly IBackendClient _backendClient;

    [ObservableProperty]
    private ObservableCollection<EngineInfo> availableEngines = new();

    [ObservableProperty]
    private EngineInfo? selectedEngine;

    [ObservableProperty]
    private ObservableCollection<ParameterPreset> parameterPresets = new();

    [ObservableProperty]
    private ParameterPreset? selectedPreset;

    [ObservableProperty]
    private ObservableCollection<EngineParameter> parameters = new();

    [ObservableProperty]
    private double predictedMosScore;

    [ObservableProperty]
    private double predictedSimilarity;

    [ObservableProperty]
    private double predictedNaturalness;

    [ObservableProperty]
    private double predictedSnr;

    [ObservableProperty]
    private double estimatedSpeed = 1.0;

    [ObservableProperty]
    private string qualitySpeedTradeoff = "Balanced";

    [ObservableProperty]
    private string? parameterImpactSummary;

    public bool HasParameters => Parameters.Count > 0;

    public EngineParameterTuningViewModel(IBackendClient backendClient)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
      SavePresetCommand = new RelayCommand(SavePreset, () => Parameters.Count > 0);
      AutoOptimizeCommand = new AsyncRelayCommand(AutoOptimizeAsync, () => Parameters.Count > 0);
      ResetToDefaultsCommand = new RelayCommand(ResetToDefaults, () => Parameters.Count > 0);
      ApplyParametersCommand = new AsyncRelayCommand(ApplyParametersAsync, () => Parameters.Count > 0);

      LoadAvailableEngines();
      LoadParameterPresets();
    }

    public IRelayCommand SavePresetCommand { get; }
    public IAsyncRelayCommand AutoOptimizeCommand { get; }
    public IRelayCommand ResetToDefaultsCommand { get; }
    public IAsyncRelayCommand ApplyParametersCommand { get; }

    partial void OnSelectedEngineChanged(EngineInfo? value)
    {
      if (value != null)
      {
        LoadEngineParameters(value);
      }
    }

    partial void OnSelectedPresetChanged(ParameterPreset? value)
    {
      if (value != null)
      {
        LoadPreset(value);
      }
    }

    partial void OnParametersChanged(ObservableCollection<EngineParameter> value)
    {
      OnPropertyChanged(nameof(HasParameters));
      SavePresetCommand.NotifyCanExecuteChanged();
      AutoOptimizeCommand.NotifyCanExecuteChanged();
      ResetToDefaultsCommand.NotifyCanExecuteChanged();
      ApplyParametersCommand.NotifyCanExecuteChanged();
      UpdateQualityPredictions();
    }

    private void LoadAvailableEngines()
    {
      AvailableEngines.Clear();
      AvailableEngines.Add(new EngineInfo
      {
        Id = "tortoise",
        Name = "Tortoise TTS",
        Type = "TTS"
      });
      AvailableEngines.Add(new EngineInfo
      {
        Id = "xtts_v2",
        Name = "XTTS v2",
        Type = "TTS"
      });
      AvailableEngines.Add(new EngineInfo
      {
        Id = "chatterbox",
        Name = "Chatterbox TTS",
        Type = "TTS"
      });
    }

    private void LoadEngineParameters(EngineInfo engine)
    {
      Parameters.Clear();

      if (engine.Id == "tortoise")
      {
        Parameters.Add(new EngineParameter
        {
          Id = "num_autoregressive_samples",
          Name = "Autoregressive Samples",
          Description = "Number of autoregressive samples. Higher = better quality but slower.",
          Value = 256,
          MinValue = 16,
          MaxValue = 512,
          StepSize = 16,
          DefaultValue = 256,
          QualityImpact = "High impact on quality and speed",
          RelatedParameters = "Affects diffusion_iterations tradeoff"
        });
        Parameters.Add(new EngineParameter
        {
          Id = "diffusion_iterations",
          Name = "Diffusion Iterations",
          Description = "Number of diffusion iterations. Higher = better quality but slower.",
          Value = 200,
          MinValue = 30,
          MaxValue = 400,
          StepSize = 10,
          DefaultValue = 200,
          QualityImpact = "High impact on quality and speed",
          RelatedParameters = "Affects num_autoregressive_samples tradeoff"
        });
      }
      else if (engine.Id == "xtts_v2")
      {
        Parameters.Add(new EngineParameter
        {
          Id = "temperature",
          Name = "Temperature",
          Description = "Sampling temperature. Lower = more deterministic.",
          Value = 0.7,
          MinValue = 0.1,
          MaxValue = 1.5,
          StepSize = 0.1,
          DefaultValue = 0.7,
          QualityImpact = "Affects voice naturalness and variation"
        });
        Parameters.Add(new EngineParameter
        {
          Id = "length_penalty",
          Name = "Length Penalty",
          Description = "Length penalty for generation.",
          Value = 1.0,
          MinValue = 0.5,
          MaxValue = 2.0,
          StepSize = 0.1,
          DefaultValue = 1.0,
          QualityImpact = "Affects output length and quality"
        });
      }
      else if (engine.Id == "chatterbox")
      {
        Parameters.Add(new EngineParameter
        {
          Id = "temperature",
          Name = "Temperature",
          Description = "Sampling temperature for voice generation.",
          Value = 0.8,
          MinValue = 0.1,
          MaxValue = 1.5,
          StepSize = 0.1,
          DefaultValue = 0.8,
          QualityImpact = "Affects voice expressiveness"
        });
        Parameters.Add(new EngineParameter
        {
          Id = "top_p",
          Name = "Top-P",
          Description = "Nucleus sampling parameter.",
          Value = 0.9,
          MinValue = 0.5,
          MaxValue = 1.0,
          StepSize = 0.05,
          DefaultValue = 0.9,
          QualityImpact = "Affects generation diversity"
        });
      }

      UpdateQualityPredictions();
    }

    private void UpdateQualityPredictions()
    {
      if (SelectedEngine == null || Parameters.Count == 0)
      {
        PredictedMosScore = 0;
        PredictedSimilarity = 0;
        PredictedNaturalness = 0;
        PredictedSnr = 0;
        EstimatedSpeed = 1.0;
        QualitySpeedTradeoff = "Unknown";
        ParameterImpactSummary = null;
        return;
      }

      // Calculate predicted quality metrics based on parameters
      if (SelectedEngine.Id == "tortoise")
      {
        var autoregressiveSamples = Parameters.FirstOrDefault(p => p.Id == "num_autoregressive_samples")?.Value ?? 256;
        var diffusionIterations = Parameters.FirstOrDefault(p => p.Id == "diffusion_iterations")?.Value ?? 200;

        // Higher samples/iterations = better quality but slower
        var qualityFactor = autoregressiveSamples / 256.0 * (diffusionIterations / 200.0);
        var speedFactor = 1.0 / qualityFactor;

        PredictedMosScore = Math.Min(5.0, 4.0 + (qualityFactor * 1.0));
        PredictedSimilarity = Math.Min(1.0, 0.85 + (qualityFactor * 0.15));
        PredictedNaturalness = Math.Min(1.0, 0.90 + (qualityFactor * 0.10));
        PredictedSnr = 20 + (qualityFactor * 15);
        EstimatedSpeed = Math.Max(0.1, speedFactor);
        if (qualityFactor > 1.2)
        {
          QualitySpeedTradeoff = "Quality Focused";
        }
        else if (qualityFactor < 0.8)
        {
          QualitySpeedTradeoff = "Speed Focused";
        }
        else
        {
          QualitySpeedTradeoff = "Balanced";
        }

        ParameterImpactSummary = $"Autoregressive samples ({autoregressiveSamples}) and diffusion iterations ({diffusionIterations}) significantly impact quality and speed. Higher values improve quality but reduce speed.";
      }
      else if (SelectedEngine.Id == "xtts_v2" || SelectedEngine.Id == "chatterbox")
      {
        var temperature = Parameters.FirstOrDefault(p => p.Id == "temperature")?.Value ?? 0.7;
        var topP = Parameters.FirstOrDefault(p => p.Id == "top_p")?.Value ?? 0.9;

        PredictedMosScore = 4.2 + ((temperature - 0.7) * 0.5);
        PredictedSimilarity = 0.88 + ((topP - 0.9) * 0.1);
        PredictedNaturalness = 0.85 + ((temperature - 0.7) * 0.2);
        PredictedSnr = 25 + ((topP - 0.9) * 10);
        EstimatedSpeed = 1.0; // These engines are generally fast
        QualitySpeedTradeoff = "Balanced";

        ParameterImpactSummary = $"Temperature ({temperature:F2}) and Top-P ({topP:F2}) affect voice expressiveness and diversity.";
      }
    }

    public void OnParameterValueChanged(EngineParameter parameter)
    {
      UpdateQualityPredictions();
    }

    private void LoadParameterPresets()
    {
      ParameterPresets.Clear();
      ParameterPresets.Add(new ParameterPreset
      {
        Id = "tortoise_fast",
        Name = "Tortoise Fast",
        EngineId = "tortoise",
        Description = "Fast synthesis, moderate quality"
      });
      ParameterPresets.Add(new ParameterPreset
      {
        Id = "tortoise_high_quality",
        Name = "Tortoise High Quality",
        EngineId = "tortoise",
        Description = "High quality synthesis"
      });
      ParameterPresets.Add(new ParameterPreset
      {
        Id = "tortoise_ultra_quality",
        Name = "Tortoise Ultra Quality",
        EngineId = "tortoise",
        Description = "Maximum quality, slow synthesis"
      });
    }

    private void LoadPreset(ParameterPreset preset)
    {
      if (SelectedEngine?.Id != preset.EngineId)
      {
        SelectedEngine = AvailableEngines.FirstOrDefault(e => e.Id == preset.EngineId);
      }

      // Load preset values
      if (preset.Id == "tortoise_fast")
      {
        var param1 = Parameters.FirstOrDefault(p => p.Id == "num_autoregressive_samples");
        var param2 = Parameters.FirstOrDefault(p => p.Id == "diffusion_iterations");
        if (param1 != null) param1.Value = 32;
        if (param2 != null) param2.Value = 50;
      }
      else if (preset.Id == "tortoise_high_quality")
      {
        var param1 = Parameters.FirstOrDefault(p => p.Id == "num_autoregressive_samples");
        var param2 = Parameters.FirstOrDefault(p => p.Id == "diffusion_iterations");
        if (param1 != null) param1.Value = 256;
        if (param2 != null) param2.Value = 200;
      }
      else if (preset.Id == "tortoise_ultra_quality")
      {
        var param1 = Parameters.FirstOrDefault(p => p.Id == "num_autoregressive_samples");
        var param2 = Parameters.FirstOrDefault(p => p.Id == "diffusion_iterations");
        if (param1 != null) param1.Value = 512;
        if (param2 != null) param2.Value = 400;
      }
    }

    private void SavePreset()
    {
      var preset = new ParameterPreset
      {
        Id = Guid.NewGuid().ToString(),
        Name = $"{SelectedEngine?.Name} Custom",
        EngineId = SelectedEngine?.Id ?? "",
        Description = "Custom parameter preset"
      };

      ParameterPresets.Add(preset);
      SelectedPreset = preset;
    }

    private async Task AutoOptimizeAsync()
    {
      if (SelectedEngine == null || Parameters.Count == 0)
        return;

      try
      {
        // Simple optimization: adjust parameters to maximize predicted quality
        // Uses a grid search approach to find better parameter combinations
        var bestParams = new Dictionary<string, double>();
        var bestScore = 0.0;

        // Get current parameter values
        foreach (var param in Parameters)
        {
          bestParams[param.Id] = param.Value;
        }

        // Try variations around current values
        foreach (var param in Parameters)
        {
          if (param.MinValue >= param.MaxValue)
            continue;

          var currentValue = param.Value;
          var step = (param.MaxValue - param.MinValue) / 10.0;
          if (step <= 0) continue;

          // Test values around current
          for (var testValue = Math.Max(param.MinValue, currentValue - (step * 2));
               testValue <= Math.Min(param.MaxValue, currentValue + (step * 2));
               testValue += step)
          {
            param.Value = testValue;
            UpdateQualityPredictions();

            // Score based on predicted metrics
            var score = (PredictedMosScore * 0.4) +
                       (PredictedSimilarity * 0.3) +
                       (PredictedNaturalness * 0.2) +
                       (PredictedSnr / 50.0 * 0.1);

            if (score > bestScore)
            {
              bestScore = score;
              bestParams[param.Id] = testValue;
            }
          }

          // Restore best value found
          param.Value = bestParams[param.Id];
        }

        // Update predictions with optimized values
        UpdateQualityPredictions();
        await Task.CompletedTask;
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Auto-optimization failed: {ex.Message}");
      }
    }

    private void ResetToDefaults()
    {
      foreach (var param in Parameters)
      {
        param.Value = param.DefaultValue;
      }
      UpdateQualityPredictions();
    }

    private async Task ApplyParametersAsync()
    {
      if (SelectedEngine == null || Parameters.Count == 0)
        return;

      try
      {
        // Build parameter dictionary
        var parameterDict = new Dictionary<string, object>();
        foreach (var param in Parameters)
        {
          parameterDict[param.Id] = param.Value;
        }

        // Send parameters to backend to update engine configuration
        var request = new
        {
          engine_id = SelectedEngine.Id,
          parameters = parameterDict
        };

        await _backendClient.SendRequestAsync<object, object>(
            "/api/engines/configure",
            request
        );
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Failed to apply parameters: {ex.Message}");
      }
    }
  }

  public class EngineInfo
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether the engine is actually available for use (GAP-CRIT-005).
    /// </summary>
    public bool IsAvailable { get; set; } = true;
    
    /// <summary>
    /// Reason the engine is unavailable (for UI display).
    /// </summary>
    public string? UnavailableReason { get; set; }
    
    /// <summary>
    /// Display name with availability indicator.
    /// </summary>
    public string DisplayName => IsAvailable ? Name : $"{Name} (Unavailable)";
  }

  public class EngineParameter : ObservableObject
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public double Value { get; set; }
    public double MinValue { get; set; }
    public double MaxValue { get; set; }
    public double StepSize { get; set; }
    public double DefaultValue { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string? QualityImpact { get; set; }
    public string? RelatedParameters { get; set; }

    public string Range => $"{MinValue:F1} - {MaxValue:F1}";
  }

  public class ParameterPreset
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string EngineId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
  }
}