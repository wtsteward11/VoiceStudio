using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// Audio analysis API response models for AudioAnalysis panel.
  /// Matches backend /api/audio-analysis routes (snake_case via BackendApi options).
  /// </summary>
  public class AudioAnalysisResult
  {
    public string AudioId { get; set; } = string.Empty;
    public int SampleRate { get; set; }
    public double Duration { get; set; }
    public int Channels { get; set; }
    public SpectralAnalysis Spectral { get; set; } = new();
    public TemporalAnalysis Temporal { get; set; } = new();
    public PerceptualAnalysis Perceptual { get; set; } = new();
    public string Created { get; set; } = string.Empty;
  }

  public class SpectralAnalysis
  {
    public double Centroid { get; set; }
    public double Rolloff { get; set; }
    public double Flux { get; set; }
    public double ZeroCrossingRate { get; set; }
    public double Bandwidth { get; set; }
    public double Flatness { get; set; }
    public double Kurtosis { get; set; }
    public double Skewness { get; set; }
  }

  public class TemporalAnalysis
  {
    public double Rms { get; set; }
    public double ZeroCrossingRate { get; set; }
    public double? AttackTime { get; set; }
    public double? DecayTime { get; set; }
    public double? SustainLevel { get; set; }
    public double? ReleaseTime { get; set; }
  }

  public class PerceptualAnalysis
  {
    public double LoudnessLufs { get; set; }
    public double PeakLufs { get; set; }
    public double TruePeakDb { get; set; }
    public double DynamicRange { get; set; }
    public double CrestFactor { get; set; }
    public double? Lra { get; set; }
  }

  public class AudioAnalysisQueueResponse
  {
    public string JobId { get; set; } = string.Empty;
    public string AudioId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
  }

  public class AudioComparisonResponse
  {
    [JsonPropertyName("audio_id_1")]
    public string AudioId { get; set; } = string.Empty;

    [JsonPropertyName("audio_id_2")]
    public string ReferenceAudioId { get; set; } = string.Empty;
    [JsonPropertyName("spectral_differences")]
    public Dictionary<string, double> SpectralDifferences { get; set; } = new();

    [JsonPropertyName("temporal_differences")]
    public Dictionary<string, double> TemporalDifferences { get; set; } = new();

    [JsonPropertyName("perceptual_differences")]
    public Dictionary<string, double> PerceptualDifferences { get; set; } = new();

    [JsonPropertyName("overall_similarity")]
    public double OverallSimilarity { get; set; }
    public AudioComparisonSummary? Summary { get; set; }
  }

  public class AudioComparisonSummary
  {
    [JsonPropertyName("most_different_metric")]
    public string? MostDifferentMetric { get; set; }

    [JsonPropertyName("similarity_score")]
    public double? SimilarityScore { get; set; }
  }
}
