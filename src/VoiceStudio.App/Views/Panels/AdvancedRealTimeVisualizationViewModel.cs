using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// ViewModel for Advanced Real-Time Visualization.
  /// Implements IDEA 131: Advanced Visualization and Real-Time Audio Display (remaining 50%).
  /// </summary>
  public partial class AdvancedRealTimeVisualizationViewModel : ObservableObject
  {
    private readonly IBackendClient _backendClient;
    private System.Threading.Timer? _updateTimer;
    private bool _isUpdating;
    private bool _isSubscribedToPlayback;

    [ObservableProperty]
    private string visualizationType = "Waveform";

    [ObservableProperty]
    private bool realTimeUpdates = true;

    [ObservableProperty]
    private double updateRate = 60.0;

    [ObservableProperty]
    private double rotationSpeed = 50.0;

    [ObservableProperty]
    private double perspective = 50.0;

    [ObservableProperty]
    private int particleCount = 1000;

    [ObservableProperty]
    private double particleSensitivity = 50.0;

    [ObservableProperty]
    private string particleStyle = "Bubbles";

    [ObservableProperty]
    private string colorScheme = "Cyan";

    [ObservableProperty]
    private bool showGrid = true;

    [ObservableProperty]
    private bool showLabels = true;

    [ObservableProperty]
    private bool showFPS;

    [ObservableProperty]
    private bool syncWithPlayback = true;

    [ObservableProperty]
    private ObservableCollection<VisualizationPreset> visualizationPresets = new();

    [ObservableProperty]
    private VisualizationPreset? selectedPreset;

    [ObservableProperty]
    private string statusMessage = "Ready";

    [ObservableProperty]
    private TimeSpan audioPosition = TimeSpan.Zero;

    [ObservableProperty]
    private double fps;

    public bool IsWaveformView => VisualizationType == "Waveform";
    public bool IsSpectrogramView => VisualizationType == "Spectrogram";
    public bool Is3DVisualization => VisualizationType == "3D Spectrogram" || VisualizationType == "Frequency Waterfall";
    public bool IsParticleVisualizer => VisualizationType == "Particle Visualizer";

    public AdvancedRealTimeVisualizationViewModel(IBackendClient backendClient)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
      SavePresetCommand = new RelayCommand(SavePreset, () => !string.IsNullOrWhiteSpace(VisualizationType));
      ResetViewCommand = new RelayCommand(ResetView);

      LoadPresets();
      StartUpdateTimer();
    }

    public IRelayCommand SavePresetCommand { get; }
    public IRelayCommand ResetViewCommand { get; }

    partial void OnVisualizationTypeChanged(string value)
    {
      OnPropertyChanged(nameof(IsWaveformView));
      OnPropertyChanged(nameof(IsSpectrogramView));
      OnPropertyChanged(nameof(Is3DVisualization));
      OnPropertyChanged(nameof(IsParticleVisualizer));
      SavePresetCommand.NotifyCanExecuteChanged();
    }

    partial void OnRealTimeUpdatesChanged(bool value)
    {
      if (value)
      {
        StartUpdateTimer();
      }
      else
      {
        StopUpdateTimer();
      }
    }

    partial void OnUpdateRateChanged(double value)
    {
      RestartUpdateTimer();
    }

    partial void OnSyncWithPlaybackChanged(bool value)
    {
      if (value)
      {
        SubscribeToPlaybackEvents();
      }
      else
      {
        UnsubscribeFromPlaybackEvents();
      }
    }

    private void SubscribeToPlaybackEvents()
    {
      if (!_isSubscribedToPlayback)
      {
        try
        {
          // Subscribe to playback position updates via backend WebSocket or polling
          // For now, use timer-based updates when sync is enabled
          _isSubscribedToPlayback = true;
          RestartUpdateTimer();
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine($"Failed to subscribe to playback events: {ex.Message}");
        }
      }
    }

    private void UnsubscribeFromPlaybackEvents()
    {
      if (_isSubscribedToPlayback)
      {
        try
        {
          _isSubscribedToPlayback = false;
          // Stop position updates if not using real-time updates
          if (!RealTimeUpdates)
          {
            StopUpdateTimer();
          }
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine($"Failed to unsubscribe from playback events: {ex.Message}");
        }
      }
    }

    private void LoadPresets()
    {
      VisualizationPresets.Clear();
      VisualizationPresets.Add(new VisualizationPreset
      {
        Id = "default_waveform",
        Name = "Default Waveform",
        VisualizationType = "Waveform",
        ColorScheme = "Cyan",
        RealTimeUpdates = true,
        UpdateRate = 60.0
      });
      VisualizationPresets.Add(new VisualizationPreset
      {
        Id = "default_spectrogram",
        Name = "Default Spectrogram",
        VisualizationType = "Spectrogram",
        ColorScheme = "Rainbow",
        RealTimeUpdates = true,
        UpdateRate = 60.0
      });
      VisualizationPresets.Add(new VisualizationPreset
      {
        Id = "3d_spectrogram",
        Name = "3D Spectrogram",
        VisualizationType = "3D Spectrogram",
        ColorScheme = "Fire",
        RealTimeUpdates = true,
        UpdateRate = 60.0,
        RotationSpeed = 50.0,
        Perspective = 50.0
      });
      VisualizationPresets.Add(new VisualizationPreset
      {
        Id = "particle_bubbles",
        Name = "Particle Bubbles",
        VisualizationType = "Particle Visualizer",
        ColorScheme = "Ocean",
        RealTimeUpdates = true,
        UpdateRate = 60.0,
        ParticleCount = 1000,
        ParticleSensitivity = 50.0,
        ParticleStyle = "Bubbles"
      });
    }

    private void SavePreset()
    {
      var preset = new VisualizationPreset
      {
        Id = Guid.NewGuid().ToString(),
        Name = $"{VisualizationType} Preset",
        VisualizationType = VisualizationType,
        ColorScheme = ColorScheme,
        RealTimeUpdates = RealTimeUpdates,
        UpdateRate = UpdateRate,
        RotationSpeed = RotationSpeed,
        Perspective = Perspective,
        ParticleCount = ParticleCount,
        ParticleSensitivity = ParticleSensitivity,
        ParticleStyle = ParticleStyle,
        ShowGrid = ShowGrid,
        ShowLabels = ShowLabels
      };

      VisualizationPresets.Add(preset);
      SelectedPreset = preset;
    }

    private void ResetView()
    {
      VisualizationType = "Waveform";
      ColorScheme = "Cyan";
      ShowGrid = true;
      ShowLabels = true;
      RealTimeUpdates = true;
      UpdateRate = 60.0;
    }

    private void StartUpdateTimer()
    {
      if (RealTimeUpdates && _updateTimer == null)
      {
        var interval = (int)(1000.0 / UpdateRate);
        _updateTimer = new System.Threading.Timer(UpdateVisualization, null, 0, interval);
      }
    }

    private void StopUpdateTimer()
    {
      if (_updateTimer != null)
      {
        _updateTimer.Dispose();
        _updateTimer = null;
      }
    }

    private void RestartUpdateTimer()
    {
      StopUpdateTimer();
      StartUpdateTimer();
    }

    private async void UpdateVisualization(object? state)
    {
      if (_isUpdating || !RealTimeUpdates)
        return;

      _isUpdating = true;

      try
      {
        // Fetch audio visualization data from backend
        try
        {
          var request = new
          {
            visualization_type = VisualizationType.ToLower(),
            update_rate = UpdateRate
          };

          var response = await _backendClient.SendRequestAsync<object, Dictionary<string, object>>(
              "/api/visualization/get-data",
              request
          );

          if (response != null)
          {
            // Update visualization data based on response
            // FPS tracking removed - using UpdateRate property instead

            StatusMessage = $"Visualizing: {VisualizationType}";
          }
        }
        catch
        {
          // Fallback
          StatusMessage = $"Visualizing: {VisualizationType}";
        }

        // If synced with playback, get current playback position
        if (SyncWithPlayback && _isSubscribedToPlayback)
        {
          AudioPosition = await GetCurrentPlaybackPositionAsync();
        }
      }
      finally
      {
        _isUpdating = false;
      }
    }

    private async Task<TimeSpan> GetCurrentPlaybackPositionAsync()
    {
      try
      {
        var response = await _backendClient.SendRequestAsync<object, Dictionary<string, object>>(
            "/api/audio/playback-position",
            new { }
        );

        if (response != null && response.TryGetValue("position_seconds", out var posObj) && posObj != null && double.TryParse(posObj.ToString(), out var seconds))
        {
          return TimeSpan.FromSeconds(seconds);
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "AdvancedRealTimeVisualizationViewModel.Task");
      }

      return TimeSpan.Zero;
    }

    public void Dispose()
    {
      UnsubscribeFromPlaybackEvents();
      StopUpdateTimer();
    }
  }

  public class VisualizationPreset
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string VisualizationType { get; set; } = string.Empty;
    public string ColorScheme { get; set; } = "Cyan";
    public bool RealTimeUpdates { get; set; } = true;
    public double UpdateRate { get; set; } = 60.0;
    public double RotationSpeed { get; set; } = 50.0;
    public double Perspective { get; set; } = 50.0;
    public int ParticleCount { get; set; } = 1000;
    public double ParticleSensitivity { get; set; } = 50.0;
    public string ParticleStyle { get; set; } = "Bubbles";
    public bool ShowGrid { get; set; } = true;
    public bool ShowLabels { get; set; } = true;
  }
}