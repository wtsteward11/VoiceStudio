using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VoiceStudio.Core.Models
{
  public sealed class LongFormSynthesisRequest
  {
    public string Engine { get; set; } = "xtts";
    public string ProfileId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
    public string? Emotion { get; set; }
    public bool EnhanceQuality { get; set; }
    public float Speed { get; set; } = 1.0f;
    public float Pitch { get; set; }
    public float Stability { get; set; } = 0.72f;
    public float Clarity { get; set; } = 0.58f;
    public float Temperature { get; set; } = 0.35f;
    public int ChunkSizeChars { get; set; } = 1800;
  }

  public sealed class LongFormChunkResultDto
  {
    [JsonPropertyName("chunk_index")]
    public int ChunkIndex { get; set; }

    [JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;
  }

  public sealed class LongFormSynthesisResponse
  {
    [JsonPropertyName("audio_id")]
    public string AudioId { get; set; } = string.Empty;

    [JsonPropertyName("audio_url")]
    public string AudioUrl { get; set; } = string.Empty;

    [JsonPropertyName("duration")]
    public double Duration { get; set; }

    [JsonPropertyName("quality_score")]
    public double QualityScore { get; set; }

    [JsonPropertyName("chunks_total")]
    public int ChunksTotal { get; set; }

    [JsonPropertyName("chunks_succeeded")]
    public int ChunksSucceeded { get; set; }

    [JsonPropertyName("partial_failure")]
    public bool PartialFailure { get; set; }

    [JsonPropertyName("failed_chunks")]
    public List<LongFormChunkResultDto> FailedChunks { get; set; } = new();
  }
}
