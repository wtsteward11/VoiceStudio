using System.Text.Json.Serialization;

namespace VoiceStudio.Core.Models
{
  public sealed class SpeechToSpeechRequest
  {
    [JsonPropertyName("source_audio_id")]
    public string SourceAudioId { get; set; } = string.Empty;

    [JsonPropertyName("target_voice_profile_id")]
    public string TargetVoiceProfileId { get; set; } = string.Empty;

    [JsonPropertyName("engine_id")]
    public string? EngineId { get; set; }

    [JsonPropertyName("pitch_shift")]
    public double PitchShift { get; set; }

    [JsonPropertyName("index_rate")]
    public double IndexRate { get; set; } = 0.5;

    [JsonPropertyName("protect")]
    public double Protect { get; set; } = 0.33;

    [JsonPropertyName("consent_acknowledged")]
    public bool ConsentAcknowledged { get; set; }

    [JsonPropertyName("consent_id")]
    public string? ConsentId { get; set; }
  }

  public sealed class SpeechToSpeechResponse
  {
    [JsonPropertyName("audio_id")]
    public string AudioId { get; set; } = string.Empty;

    [JsonPropertyName("audio_url")]
    public string AudioUrl { get; set; } = string.Empty;

    [JsonPropertyName("duration")]
    public double Duration { get; set; }

    [JsonPropertyName("quality_score")]
    public double? QualityScore { get; set; }

    [JsonPropertyName("engine_used")]
    public string EngineUsed { get; set; } = "rvc";

    [JsonPropertyName("is_transformed")]
    public bool IsTransformed { get; set; }

    [JsonPropertyName("transformation_type")]
    public string TransformationType { get; set; } = string.Empty;

    [JsonPropertyName("source_audio_id")]
    public string? SourceAudioId { get; set; }

    [JsonPropertyName("disclosure_text")]
    public string? DisclosureText { get; set; }
  }
}
