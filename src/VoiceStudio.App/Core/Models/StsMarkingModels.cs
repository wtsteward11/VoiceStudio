using System.Text.Json.Serialization;

namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// Durable transformation + watermark marking status from GET /api/audio/{audio_id}/marking
  /// (GAP-056 slices 2–3).
  /// </summary>
  public sealed class StsMarkingStatus
  {
    [JsonPropertyName("audio_id")]
    public string AudioId { get; set; } = string.Empty;

    [JsonPropertyName("is_transformed")]
    public bool IsTransformed { get; set; }

    [JsonPropertyName("transformation_type")]
    public string? TransformationType { get; set; }

    [JsonPropertyName("source_reference_id")]
    public string? SourceReferenceId { get; set; }

    [JsonPropertyName("marked_at")]
    public string? MarkedAt { get; set; }

    [JsonPropertyName("watermark_applied")]
    public bool WatermarkApplied { get; set; }

    [JsonPropertyName("watermark_verified")]
    public bool? WatermarkVerified { get; set; }

    [JsonPropertyName("watermark_method")]
    public string? WatermarkMethod { get; set; }
  }
}
