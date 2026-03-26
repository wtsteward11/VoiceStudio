using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Track info from /api/automation/tracks.
  /// </summary>
  public class AutomationTrackInfo
  {
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
  }

  /// <summary>
  /// Parameter info from /api/automation/tracks/{trackId}/parameters.
  /// </summary>
  public class AutomationParameterInfo
  {
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("min")]
    public double Min { get; set; }
    [JsonPropertyName("max")]
    public double Max { get; set; }
  }

  /// <summary>
  /// Response from /api/automation/tracks/{trackId}/parameters.
  /// </summary>
  public class AutomationTrackParametersResponse
  {
    [JsonPropertyName("parameters")]
    public List<AutomationParameterInfo> Parameters { get; set; } = new();
  }

  /// <summary>
  /// Request body for POST /api/automation.
  /// </summary>
  public class AutomationCreateRequest
  {
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("parameter_id")]
    public string ParameterId { get; set; } = string.Empty;
    [JsonPropertyName("track_id")]
    public string TrackId { get; set; } = string.Empty;
    [JsonPropertyName("interpolation")]
    public string Interpolation { get; set; } = "linear";
  }

  /// <summary>
  /// Point DTO for automation update request.
  /// </summary>
  public class AutomationPointDto
  {
    [JsonPropertyName("time")]
    public double Time { get; set; }
    [JsonPropertyName("value")]
    public double Value { get; set; }
    [JsonPropertyName("bezier_handle_in_x")]
    public double? BezierHandleInX { get; set; }
    [JsonPropertyName("bezier_handle_in_y")]
    public double? BezierHandleInY { get; set; }
    [JsonPropertyName("bezier_handle_out_x")]
    public double? BezierHandleOutX { get; set; }
    [JsonPropertyName("bezier_handle_out_y")]
    public double? BezierHandleOutY { get; set; }
  }

  /// <summary>
  /// Request body for PUT /api/automation/{id}.
  /// </summary>
  public class AutomationUpdateRequest
  {
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("points")]
    public List<AutomationPointDto> Points { get; set; } = new();
    [JsonPropertyName("interpolation")]
    public string Interpolation { get; set; } = "linear";
  }
}
