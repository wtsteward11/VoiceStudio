using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Logging;
using VoiceStudio.App.Utilities;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Specialized WebSocket client for audio metering updates.
  /// Provides real-time level data for effects mixer visualization.
  /// Backend-Frontend Integration Plan - Phase 3.
  /// </summary>
  public class MeterWebSocketClient : IMeterClient
  {
    private readonly IWebSocketService _webSocketService;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _isSubscribed;
    private bool _disposed;
    private const string MeterTopic = "meters";

    /// <summary>
    /// Event fired when meter levels are updated.
    /// </summary>
    public event EventHandler<MeterLevelUpdate>? LevelsUpdated;

    /// <summary>
    /// Event fired when peak levels are detected.
    /// </summary>
    public event EventHandler<PeakLevelUpdate>? PeakDetected;

    /// <summary>
    /// Event fired when clipping occurs.
    /// </summary>
    public event EventHandler<ClipDetectedUpdate>? ClipDetected;

    /// <summary>
    /// Event fired when spectrum analysis data is available.
    /// </summary>
    public event EventHandler<SpectrumUpdate>? SpectrumUpdated;

    public MeterWebSocketClient(IWebSocketService webSocketService)
    {
      _webSocketService = webSocketService ?? throw new ArgumentNullException(nameof(webSocketService));
      // Align with WebSocketService + backend: snake_case wire (GOV-VOICESTUDIO-REALTIME-METERING-01 §2).
      _jsonOptions = JsonSerializerOptionsFactory.BackendApi;

      // Subscribe to WebSocket messages
      _webSocketService.MessageReceived += OnWebSocketMessageReceived;
    }

    /// <summary>
    /// Connects and subscribes to meter updates.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
      if (_disposed)
        throw new ObjectDisposedException(nameof(MeterWebSocketClient));

      if (!_webSocketService.IsConnected)
      {
        await _webSocketService.ConnectAsync(new[] { MeterTopic }, cancellationToken);
      }

      if (!_isSubscribed)
      {
        await _webSocketService.SubscribeAsync(MeterTopic);
        _isSubscribed = true;
      }
    }

    /// <summary>
    /// Gets whether the WebSocket client is connected and subscribed.
    /// </summary>
    public bool IsConnected => _webSocketService.IsConnected && _isSubscribed;

    /// <summary>
    /// Unsubscribes from meter updates.
    /// </summary>
    public async Task DisconnectAsync()
    {
      if (_isSubscribed)
      {
        try
        {
          await _webSocketService.UnsubscribeAsync(MeterTopic);
        }
        catch (Exception ex)
        {
          ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "MeterWebSocketClient.DisconnectAsync");
        }
        _isSubscribed = false;
      }
    }

    /// <summary>
    /// Starts metering for a specific channel or audio source.
    /// </summary>
    public async Task StartMeteringAsync(string channelId, CancellationToken cancellationToken = default)
    {
      if (!_isSubscribed)
        await ConnectAsync(cancellationToken);

      await _webSocketService.SendMessageAsync(new
      {
        action = "start_metering",
        channel_id = channelId
      });
    }

    /// <summary>
    /// Stops metering for a specific channel.
    /// </summary>
    public async Task StopMeteringAsync(string channelId)
    {
      if (_isSubscribed)
      {
        await _webSocketService.SendMessageAsync(new
        {
          action = "stop_metering",
          channel_id = channelId
        });
      }
    }

    /// <summary>
    /// Configures metering parameters.
    /// </summary>
    public async Task ConfigureMeteringAsync(MeterConfiguration config)
    {
      if (_isSubscribed)
      {
        await _webSocketService.SendMessageAsync(new
        {
          action = "configure_metering",
          config = new
          {
            update_rate_ms = config.UpdateRateMs,
            peak_hold_ms = config.PeakHoldMs,
            clip_threshold_db = config.ClipThresholdDb,
            enable_spectrum = config.EnableSpectrum,
            spectrum_bands = config.SpectrumBands
          }
        });
      }
    }

    private void OnWebSocketMessageReceived(object? sender, WebSocketMessage message)
    {
      if (_disposed)
        return;

      if (message.Topic != MeterTopic)
        return;

      try
      {
        if (message.Payload is JsonElement directElement)
        {
          DispatchPayloadElement(directElement);
          return;
        }

        if (message.Payload is null)
          return;

        var payloadJson = JsonSerializer.Serialize(message.Payload, _jsonOptions);
        using var doc = JsonDocument.Parse(payloadJson);
        DispatchPayloadElement(doc.RootElement);
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Failed to process meter message: {ex.Message}");
      }
    }

    private void DispatchPayloadElement(JsonElement root)
    {
      var messageType = root.TryGetProperty("type", out var typeProp)
          ? typeProp.GetString()
          : "levels";

      switch (messageType?.ToLowerInvariant())
      {
        case "levels":
          HandleLevelsUpdate(root);
          break;
        case "peak":
          HandlePeakUpdate(root);
          break;
        case "clip":
          HandleClipUpdate(root);
          break;
        case "spectrum":
          HandleSpectrumUpdate(root);
          break;
      }
    }

    private void HandleLevelsUpdate(JsonElement root)
    {
      try
      {
        var update = JsonSerializer.Deserialize<MeterLevelUpdate>(root.GetRawText(), _jsonOptions);
        if (update != null &&
            (!string.IsNullOrEmpty(update.ChannelId) ||
             !string.IsNullOrEmpty(update.ProjectId)))
        {
          LevelsUpdated?.Invoke(this, update);
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "MeterWebSocketClient.HandleLevelsUpdate");
      }
    }

    private void HandlePeakUpdate(JsonElement root)
    {
      try
      {
        var update = JsonSerializer.Deserialize<PeakLevelUpdate>(root.GetRawText(), _jsonOptions);
        if (update != null)
        {
          PeakDetected?.Invoke(this, update);
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "MeterWebSocketClient.HandlePeakUpdate");
      }
    }

    private void HandleClipUpdate(JsonElement root)
    {
      try
      {
        var update = JsonSerializer.Deserialize<ClipDetectedUpdate>(root.GetRawText(), _jsonOptions);
        if (update != null)
        {
          ClipDetected?.Invoke(this, update);
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "MeterWebSocketClient.HandleClipUpdate");
      }
    }

    private void HandleSpectrumUpdate(JsonElement root)
    {
      try
      {
        var update = JsonSerializer.Deserialize<SpectrumUpdate>(root.GetRawText(), _jsonOptions);
        if (update != null)
        {
          SpectrumUpdated?.Invoke(this, update);
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "MeterWebSocketClient.HandleSpectrumUpdate");
      }
    }

    public void Dispose()
    {
      if (!_disposed)
      {
        _disposed = true;
        _webSocketService.MessageReceived -= OnWebSocketMessageReceived;
        DisconnectAsync().GetAwaiter().GetResult();
      }
    }
  }

  /// <summary>
  /// Configuration for audio metering.
  /// </summary>
  public class MeterConfiguration
  {
    /// <summary>
    /// Update rate in milliseconds (default: 50ms for 20fps).
    /// </summary>
    public int UpdateRateMs { get; set; } = 50;

    /// <summary>
    /// Peak hold time in milliseconds.
    /// </summary>
    public int PeakHoldMs { get; set; } = 2000;

    /// <summary>
    /// Clipping threshold in dB (default: -0.1).
    /// </summary>
    public double ClipThresholdDb { get; set; } = -0.1;

    /// <summary>
    /// Whether to enable spectrum analysis.
    /// </summary>
    public bool EnableSpectrum { get; set; }

    /// <summary>
    /// Number of spectrum bands (default: 32).
    /// </summary>
    public int SpectrumBands { get; set; } = 32;
  }

  /// <summary>
  /// Real-time meter level update.
  /// Uses explicit <c>JsonPropertyName</c> (no dual snake/Pascal shadow props) so <see cref="JsonSerializerOptionsFactory.BackendApi"/>
  /// snake_case naming does not collide with wire keys (GAP-036 deserialization).
  /// </summary>
  public class MeterLevelUpdate
  {
    [System.Text.Json.Serialization.JsonPropertyName("channel_id")]
    public string ChannelId { get; set; } = string.Empty;

    /// <summary>Left channel level in dB.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("left_db")]
    public double LeftDb { get; set; }

    /// <summary>Right channel level in dB.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("right_db")]
    public double RightDb { get; set; }

    /// <summary>Left channel RMS level in dB.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("left_rms_db")]
    public double LeftRmsDb { get; set; }

    /// <summary>Right channel RMS level in dB.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("right_rms_db")]
    public double RightRmsDb { get; set; }

    /// <summary>Backend mixer scope (<c>broadcast_meter_updates</c>).</summary>
    [System.Text.Json.Serialization.JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = "";

    /// <summary>Normalized peak 0–1 from backend <c>peak_level</c>.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("peak_level")]
    public double PeakLevelLinear { get; set; }

    /// <summary>Normalized RMS 0–1 from backend <c>rms_level</c>.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("rms_level")]
    public double RmsLevelLinear { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
  }

  /// <summary>
  /// Peak level notification.
  /// </summary>
  public class PeakLevelUpdate
  {
    [System.Text.Json.Serialization.JsonPropertyName("channel_id")]
    public string ChannelId { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("peak_db")]
    public double PeakDb { get; set; }

    /// <summary>Channel (left/right/mono).</summary>
    [System.Text.Json.Serialization.JsonPropertyName("channel")]
    public string Channel { get; set; } = "mono";

    [System.Text.Json.Serialization.JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
  }

  /// <summary>
  /// Clip detection notification.
  /// </summary>
  public class ClipDetectedUpdate
  {
    [System.Text.Json.Serialization.JsonPropertyName("channel_id")]
    public string ChannelId { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("clip_level_db")]
    public double ClipLevelDb { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("clip_count")]
    public int ClipCount { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("channel")]
    public string Channel { get; set; } = "mono";

    [System.Text.Json.Serialization.JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
  }

  /// <summary>
  /// Spectrum analysis update.
  /// </summary>
  public class SpectrumUpdate
  {
    [System.Text.Json.Serialization.JsonPropertyName("channel_id")]
    public string ChannelId { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("bands")]
    public double[] Bands { get; set; } = Array.Empty<double>();

    [System.Text.Json.Serialization.JsonPropertyName("frequencies")]
    public double[] Frequencies { get; set; } = Array.Empty<double>();

    [System.Text.Json.Serialization.JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
  }

  /// <summary>
  /// Interface for meter client for DI.
  /// </summary>
  public interface IMeterClient : IDisposable
  {
    event EventHandler<MeterLevelUpdate>? LevelsUpdated;
    event EventHandler<PeakLevelUpdate>? PeakDetected;
    event EventHandler<ClipDetectedUpdate>? ClipDetected;
    event EventHandler<SpectrumUpdate>? SpectrumUpdated;
    
    bool IsConnected { get; }
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task StartMeteringAsync(string channelId, CancellationToken cancellationToken = default);
    Task StopMeteringAsync(string channelId);
    Task ConfigureMeteringAsync(MeterConfiguration config);
  }
}
