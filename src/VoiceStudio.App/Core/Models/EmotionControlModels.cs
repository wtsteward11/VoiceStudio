using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// Prosody authority diagnostics (shared shape with /api/prosody/apply, GAP-023).
  /// </summary>
  public class ProsodyHandlingDiagnosticsDto
  {
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("applied_operations")]
    public List<string> AppliedOperations { get; set; } = new();

    [JsonPropertyName("skipped_operations")]
    public List<Dictionary<string, string>> SkippedOperations { get; set; } = new();

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();

    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = new();

    [JsonPropertyName("pitch_factor")]
    public float PitchFactor { get; set; }

    [JsonPropertyName("rate_factor")]
    public float RateFactor { get; set; }

    [JsonPropertyName("volume_factor")]
    public float VolumeFactor { get; set; }

    [JsonPropertyName("context")]
    public string Context { get; set; } = string.Empty;
  }

  /// <summary>
  /// Extended emotion apply request with blending support (/api/emotion/apply-extended).
  /// </summary>
  public class EmotionApplyExtendedRequest
  {
    [JsonPropertyName("audio_id")]
    public string AudioId { get; set; } = string.Empty;

    [JsonPropertyName("primary_emotion")]
    public string PrimaryEmotion { get; set; } = string.Empty;

    [JsonPropertyName("primary_intensity")]
    public float PrimaryIntensity { get; set; }

    [JsonPropertyName("secondary_emotion")]
    public string? SecondaryEmotion { get; set; }

    [JsonPropertyName("secondary_intensity")]
    public float SecondaryIntensity { get; set; }
  }

  /// <summary>
  /// Response from POST /api/emotion/apply-extended and POST /api/emotion/preview (GAP-050 preview authority).
  /// </summary>
  public class EmotionApplyExtendedResponse
  {
    [JsonPropertyName("audio_id")]
    public string AudioId { get; set; } = string.Empty;

    [JsonPropertyName("audio_url")]
    public string AudioUrl { get; set; } = string.Empty;

    [JsonPropertyName("prosody_handling")]
    public ProsodyHandlingDiagnosticsDto? ProsodyHandling { get; set; }

    [JsonPropertyName("emotion_mapping_source")]
    public string EmotionMappingSource { get; set; } = string.Empty;
  }
}
