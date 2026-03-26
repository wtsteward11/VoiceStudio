namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// Spatial Audio API models. Matches backend /api/spatial-audio.
  /// </summary>
  public class SpatialConfigInfo
  {
    public string ConfigId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AudioId { get; set; } = string.Empty;
    public SpatialPositionInfo Position { get; set; } = new();
    public double RoomSize { get; set; }
    public double ReverbAmount { get; set; }
    public double Occlusion { get; set; }
    public bool Doppler { get; set; }
    public bool Hrtf { get; set; }
  }

  public class SpatialPositionInfo
  {
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Distance { get; set; }
  }

  public class SpatialConfigCreateRequest
  {
    public string Name { get; set; } = string.Empty;
    public string AudioId { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Distance { get; set; } = 1.0;
    public double RoomSize { get; set; } = 1.0;
    public double ReverbAmount { get; set; }
    public double Occlusion { get; set; }
    public bool Doppler { get; set; }
    public bool Hrtf { get; set; } = true;
  }

  public class SpatialConfigUpdateRequest
  {
    public string? Name { get; set; }
    public string? AudioId { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
    public double? Z { get; set; }
    public double? Distance { get; set; }
    public double? RoomSize { get; set; }
    public double? ReverbAmount { get; set; }
    public double? Occlusion { get; set; }
    public bool? Doppler { get; set; }
    public bool? Hrtf { get; set; }
  }

  public class SpatialApplyResponse
  {
    public string AudioId { get; set; } = string.Empty;
    public string ConfigApplied { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
  }

  public class SpatialPreviewResponse
  {
    public string PreviewUrl { get; set; } = string.Empty;
    public SpatialPositionInfo Position { get; set; } = new();
    public string Message { get; set; } = string.Empty;
  }
}
