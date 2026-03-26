using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Request body for POST /api/scenes.
  /// </summary>
  public class SceneCreateRequest
  {
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = string.Empty;
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();
  }

  /// <summary>
  /// Track DTO for scene update request.
  /// </summary>
  public class SceneTrackDto
  {
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("track_number")]
    public int TrackNumber { get; set; }
    [JsonPropertyName("clips")]
    public List<Dictionary<string, object>> Clips { get; set; } = new();
    [JsonPropertyName("effects")]
    public List<Dictionary<string, object>> Effects { get; set; } = new();
    [JsonPropertyName("automation")]
    public List<Dictionary<string, object>> Automation { get; set; } = new();
  }

  /// <summary>
  /// Request body for PUT /api/scenes/{id}.
  /// </summary>
  public class SceneUpdateRequest
  {
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    [JsonPropertyName("tracks")]
    public List<SceneTrackDto> Tracks { get; set; } = new();
    [JsonPropertyName("master_effects")]
    public List<Dictionary<string, object>> MasterEffects { get; set; } = new();
    [JsonPropertyName("duration")]
    public double Duration { get; set; }
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();
  }

  /// <summary>
  /// Response from POST /api/scenes/{id}/apply.
  /// </summary>
  public class SceneApplyResponse
  {
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
  }
}
