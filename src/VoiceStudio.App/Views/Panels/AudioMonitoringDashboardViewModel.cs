using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;
using Windows.UI;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// ViewModel for Audio Monitoring Dashboard.
  /// Implements IDEA 34: Real-Time Audio Monitoring Dashboard.
  /// </summary>
  public partial class AudioMonitoringDashboardViewModel : ObservableObject
  {
    private readonly IBackendClient _backendClient;
    private CancellationTokenSource? _pollingCts;
    private bool _isPolling;
    private double _maxPeakSeen = double.MinValue;
    private double _maxRmsSeen = double.MinValue;
    private double _totalRmsSum;
    private int _rmsSampleCount;

    [ObservableProperty]
    private string? audioId;

    [ObservableProperty]
    private bool isRealTimeEnabled;

    [ObservableProperty]
    private string statusText = "Ready";

    [ObservableProperty]
    private double peakLevel; // in dB

    [ObservableProperty]
    private double rmsLevel; // in dB

    [ObservableProperty]
    private double lufsLevel = -23.0; // in LUFS

    [ObservableProperty]
    private double truePeakLevel; // in dBTP

    [ObservableProperty]
    private ObservableCollection<ChannelMeterItem> channelMeters = new();

    [ObservableProperty]
    private int sampleRate = 44100;

    [ObservableProperty]
    private int channelCount = 2;

    [ObservableProperty]
    private int bitDepth = 16;

    [ObservableProperty]
    private string duration = "0:00";

    [ObservableProperty]
    private string fileSize = "0 B";

    [ObservableProperty]
    private string format = "WAV";

    [ObservableProperty]
    private double maxPeak;

    [ObservableProperty]
    private double maxRms;

    [ObservableProperty]
    private double averageRms;

    [ObservableProperty]
    private double dynamicRange;

    [ObservableProperty]
    private bool clippingDetected;

    [ObservableProperty]
    private ObservableCollection<MonitoringAlert> alerts = new();

    public double PeakLevelNormalized => Math.Max(0.0, Math.Min(1.0, (PeakLevel + 60.0) / 60.0)); // Convert -60dB to 0dB range to 0-1
    public double RmsLevelNormalized => Math.Max(0.0, Math.Min(1.0, (RmsLevel + 60.0) / 60.0));
    public double TruePeakLevelNormalized => Math.Max(0.0, Math.Min(1.0, (TruePeakLevel + 60.0) / 60.0));
    public double LufsProgress => Math.Max(0.0, Math.Min(1.0, (LufsLevel + 60.0) / 60.0)); // -60 to 0 LUFS range

    public Brush PeakLevelColor => GetLevelColor(PeakLevel, -3.0, -6.0);
    public Brush RmsLevelColor => GetLevelColor(RmsLevel, -3.0, -6.0);
    public Brush LufsLevelColor => GetLevelColor(LufsLevel, -23.0, -16.0);
    public Brush TruePeakLevelColor => GetLevelColor(TruePeakLevel, -1.0, -3.0);
    public Brush ClippingColor => ClippingDetected
        ? new SolidColorBrush(Microsoft.UI.Colors.Red)
        : new SolidColorBrush(Microsoft.UI.Colors.Green);

    public bool HasMultiChannel => ChannelMeters.Count > 1;
    public bool HasAlerts => Alerts.Count > 0;

    public AudioMonitoringDashboardViewModel(IBackendClient backendClient)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
      LoadAudioCommand = new AsyncRelayCommand(LoadAudioAsync, () => !string.IsNullOrWhiteSpace(AudioId));
      ToggleRealTimeCommand = new RelayCommand(ToggleRealTime, () => !string.IsNullOrWhiteSpace(AudioId));
      ResetCommand = new RelayCommand(Reset);
    }

    public IAsyncRelayCommand LoadAudioCommand { get; }
    public IRelayCommand ToggleRealTimeCommand { get; }
    public IRelayCommand ResetCommand { get; }

    partial void OnAudioIdChanged(string? value)
    {
      LoadAudioCommand.NotifyCanExecuteChanged();
      ToggleRealTimeCommand.NotifyCanExecuteChanged();

      if (IsRealTimeEnabled)
      {
        StopPolling();
      }
    }

    partial void OnIsRealTimeEnabledChanged(bool value)
    {
      if (value)
      {
        StartPolling();
      }
      else
      {
        StopPolling();
      }
    }

    private void ToggleRealTime()
    {
      IsRealTimeEnabled = !IsRealTimeEnabled;
    }

    private void StartPolling()
    {
      if (_isPolling || string.IsNullOrWhiteSpace(AudioId))
        return;

      _isPolling = true;
      _pollingCts = new CancellationTokenSource();
      _ = PollMetersAsync(_pollingCts.Token);
    }

    private void StopPolling()
    {
      _isPolling = false;
      _pollingCts?.Cancel();
      _pollingCts?.Dispose();
      _pollingCts = null;
    }

    private async Task PollMetersAsync(CancellationToken cancellationToken)
    {
      while (!cancellationToken.IsCancellationRequested && _isPolling)
      {
        try
        {
          await LoadMetersAsync();
          await Task.Delay(100, cancellationToken); // 10fps updates
        }
        catch (TaskCanceledException)
        {
          break;
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine($"Error polling meters: {ex.Message}");
          await Task.Delay(1000, cancellationToken);
        }
      }
    }

    private async Task LoadAudioAsync()
    {
      if (string.IsNullOrWhiteSpace(AudioId))
        return;

      try
      {
        StatusText = "Loading audio...";
        ResetStatistics();

        // Load meters to get initial data
        await LoadMetersAsync();

        // Load loudness data for LUFS
        try
        {
          var loudnessData = await _backendClient.GetLoudnessDataAsync(AudioId);
          if (loudnessData?.IntegratedLufs.HasValue == true)
          {
            LufsLevel = loudnessData.IntegratedLufs.Value;
          }
        }
        catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "AudioMonitoringDashboardViewModel.LoadAudioAsync");
      }

        StatusText = "Monitoring active";
      }
      catch (Exception ex)
      {
        StatusText = $"Error: {ex.Message}";
        AddAlert("Error", $"Failed to load audio: {ex.Message}");
      }
    }

    private async Task LoadMetersAsync()
    {
      if (string.IsNullOrWhiteSpace(AudioId))
        return;

      try
      {
        var meters = await _backendClient.GetAudioMetersAsync(AudioId);

        if (meters != null)
        {
          // Use overall peak and RMS if available, otherwise use first channel
          if (meters.Peak > 0 || meters.Rms > 0)
          {
            // Convert normalized values (0-1) to dB
            PeakLevel = meters.Peak > 0 ? 20.0 * Math.Log10(meters.Peak) : -60.0;
            RmsLevel = meters.Rms > 0 ? 20.0 * Math.Log10(meters.Rms) : -60.0;
            TruePeakLevel = PeakLevel; // Use peak as true peak approximation
          }

          // Update channel meters
          if (meters.Channels?.Count > 0)
          {
            ChannelMeters.Clear();
            for (int i = 0; i < meters.Channels.Count; i++)
            {
              var channel = meters.Channels[i];
              var peakDb = channel.Peak > 0 ? 20.0 * Math.Log10(channel.Peak) : -60.0;
              var rmsDb = channel.Rms > 0 ? 20.0 * Math.Log10(channel.Rms) : -60.0;

              ChannelMeters.Add(new ChannelMeterItem
              {
                ChannelName = $"Ch {i + 1}",
                PeakLevel = Math.Max(0.0, Math.Min(1.0, (peakDb + 60.0) / 60.0)),
                RmsLevel = Math.Max(0.0, Math.Min(1.0, (rmsDb + 60.0) / 60.0)),
                PeakLevelDb = peakDb
              });
            }

            ChannelCount = meters.Channels.Count;
          }
          else
          {
            ChannelCount = 1; // Mono
          }

          // Update statistics
          UpdateStatistics();

          // Check for clipping
          CheckClipping();
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Error loading meters: {ex.Message}");
      }
    }

    private void UpdateStatistics()
    {
      // Track max values
      if (PeakLevel > _maxPeakSeen)
      {
        _maxPeakSeen = PeakLevel;
        MaxPeak = _maxPeakSeen;
      }

      if (RmsLevel > _maxRmsSeen)
      {
        _maxRmsSeen = RmsLevel;
        MaxRms = _maxRmsSeen;
      }

      // Track average RMS
      _totalRmsSum += RmsLevel;
      _rmsSampleCount++;
      if (_rmsSampleCount > 0)
      {
        AverageRms = _totalRmsSum / _rmsSampleCount;
      }

      // Calculate dynamic range
      DynamicRange = MaxPeak - AverageRms;

      OnPropertyChanged(nameof(PeakLevelNormalized));
      OnPropertyChanged(nameof(RmsLevelNormalized));
      OnPropertyChanged(nameof(TruePeakLevelNormalized));
      OnPropertyChanged(nameof(LufsProgress));
      OnPropertyChanged(nameof(PeakLevelColor));
      OnPropertyChanged(nameof(RmsLevelColor));
      OnPropertyChanged(nameof(LufsLevelColor));
      OnPropertyChanged(nameof(TruePeakLevelColor));
      OnPropertyChanged(nameof(HasMultiChannel));
    }

    private void CheckClipping()
    {
      bool wasClipping = ClippingDetected;
      ClippingDetected = PeakLevel >= 0.0 || TruePeakLevel >= 0.0;

      if (ClippingDetected && !wasClipping)
      {
        AddAlert("Clipping Detected", $"Audio is clipping at {PeakLevel:F2} dB");
      }

      OnPropertyChanged(nameof(ClippingColor));
    }

    private void ResetStatistics()
    {
      _maxPeakSeen = double.MinValue;
      _maxRmsSeen = double.MinValue;
      _totalRmsSum = 0.0;
      _rmsSampleCount = 0;
      MaxPeak = 0.0;
      MaxRms = 0.0;
      AverageRms = 0.0;
      DynamicRange = 0.0;
      ClippingDetected = false;
      Alerts.Clear();
    }

    private void Reset()
    {
      StopPolling();
      IsRealTimeEnabled = false;
      ResetStatistics();
      PeakLevel = 0.0;
      RmsLevel = 0.0;
      LufsLevel = -23.0;
      TruePeakLevel = 0.0;
      ChannelMeters.Clear();
      StatusText = "Ready";
    }

    private void AddAlert(string title, string message)
    {
      Alerts.Insert(0, new MonitoringAlert
      {
        Title = title,
        Message = message,
        Timestamp = DateTime.Now
      });

      // Keep only last 10 alerts
      while (Alerts.Count > 10)
      {
        Alerts.RemoveAt(Alerts.Count - 1);
      }

      OnPropertyChanged(nameof(HasAlerts));
    }

    private Brush GetLevelColor(double level, double warningThreshold, double dangerThreshold)
    {
      if (level >= dangerThreshold)
        return new SolidColorBrush(Microsoft.UI.Colors.Red);
      if (level >= warningThreshold)
        return new SolidColorBrush(Microsoft.UI.Colors.Orange);
      return new SolidColorBrush(Microsoft.UI.Colors.Green);
    }
  }

  public class ChannelMeterItem
  {
    public string ChannelName { get; set; } = string.Empty;
    public double PeakLevel { get; set; }
    public double RmsLevel { get; set; }
    public double PeakLevelDb { get; set; }
  }

  public class MonitoringAlert
  {
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
  }
}