using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for /api/spatial-audio (position, environment, process, preview).
  /// Use instead of IBackendClient for spatial audio panel.
  /// </summary>
  public interface ISpatialAudioClient
  {
    /// <summary>
    /// Sets the 3D position for an audio source.
    /// </summary>
    Task<SpatialConfigResponse?> SetPositionAsync(
      SpatialPositionRequest request,
      CancellationToken cancellationToken = default);

    /// <summary>
    /// Configures the spatial environment (room size, material, reverb).
    /// </summary>
    Task<Dictionary<string, object>?> ConfigureEnvironmentAsync(
      SpatialEnvironmentRequest request,
      CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes audio with spatial effects.
    /// </summary>
    Task<SpatialProcessResponse?> ProcessAudioAsync(
      SpatialProcessRequest request,
      CancellationToken cancellationToken = default);

    /// <summary>
    /// Previews spatial audio with current position and distance.
    /// </summary>
    Task<Dictionary<string, object>?> PreviewAsync(
      string audioId,
      float x,
      float y,
      float z,
      float distance,
      CancellationToken cancellationToken = default);
  }

  /// <summary>
  /// Request for setting spatial position.
  /// </summary>
  public sealed class SpatialPositionRequest
  {
    public string AudioId { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Distance { get; set; }
  }

  /// <summary>
  /// Response from position/config endpoint.
  /// </summary>
  public sealed class SpatialConfigResponse
  {
    public string ConfigId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public SpatialPositionData Position { get; set; } = new();
  }

  /// <summary>
  /// 3D position data.
  /// </summary>
  public sealed class SpatialPositionData
  {
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Distance { get; set; }
  }

  /// <summary>
  /// Request for setting spatial environment.
  /// </summary>
  public sealed class SpatialEnvironmentRequest
  {
    public float RoomSize { get; set; }
    public string Material { get; set; } = string.Empty;
    public float ReverbAmount { get; set; }
    public bool Doppler { get; set; }
  }

  /// <summary>
  /// Request for processing audio with spatial effects.
  /// </summary>
  public sealed class SpatialProcessRequest
  {
    public string AudioId { get; set; } = string.Empty;
    public SpatialPositionData? Position { get; set; }
    public SpatialEnvironmentData? Environment { get; set; }
  }

  /// <summary>
  /// Environment data for spatial processing.
  /// </summary>
  public sealed class SpatialEnvironmentData
  {
    public float RoomSize { get; set; }
    public string Material { get; set; } = string.Empty;
    public float ReverbAmount { get; set; }
    public bool Doppler { get; set; }
  }

  /// <summary>
  /// Response from process endpoint.
  /// </summary>
  public sealed class SpatialProcessResponse
  {
    public string ProcessedAudioId { get; set; } = string.Empty;
    public string ProcessedAudioUrl { get; set; } = string.Empty;
  }
}
