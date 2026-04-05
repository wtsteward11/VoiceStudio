using System.Text.Json.Serialization;

namespace VoiceStudio.Core.Models
{
  public class EffectProcessResponse
  {
    public bool Success { get; set; }

    [JsonPropertyName("output_audio_id")]
    public string? OutputAudioId { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    public string? AudioId { get; set; }
    public string? AudioUrl { get; set; }
    public string? ErrorMessage { get; set; }
  }
}
