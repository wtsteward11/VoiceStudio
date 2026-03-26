using System.Text.Json.Serialization;

namespace VoiceStudio.Core.Models
{
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
  /// Response from emotion preview (/api/emotion/preview).
  /// </summary>
  public class EmotionPreviewResponse
  {
    [JsonPropertyName("audio_id")]
    public string AudioId { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
  }
}
