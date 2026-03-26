using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Request/response models for the voice cloning wizard API.
  /// </summary>
  public class VoiceCloningAudioValidationRequest
  {
    public string AudioId { get; set; } = string.Empty;
  }

  public class VoiceCloningAudioValidationResponse
  {
    public bool IsValid { get; set; }
    public double Duration { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public string[] Issues { get; set; } = System.Array.Empty<string>();
    public string[] Recommendations { get; set; } = System.Array.Empty<string>();
    public double? QualityScore { get; set; }
  }

  public class VoiceCloningAudioUploadResponse
  {
    public string AudioId { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public long? FileSize { get; set; }
  }

  public class VoiceCloningWizardStartRequest
  {
    public string ReferenceAudioId { get; set; } = string.Empty;
    public string Engine { get; set; } = "xtts";
    public string QualityMode { get; set; } = "standard";
    public string ProfileName { get; set; } = string.Empty;
    public string? ProfileDescription { get; set; }
  }

  public class VoiceCloningWizardStartResponse
  {
    public string JobId { get; set; } = string.Empty;
    public int Step { get; set; }
    public string Status { get; set; } = string.Empty;
  }

  public class VoiceCloningWizardStatusResponse
  {
    public string JobId { get; set; } = string.Empty;
    public int Step { get; set; }
    public string Status { get; set; } = string.Empty;
    public float Progress { get; set; }
    public string? ProfileId { get; set; }
    public Dictionary<string, object>? QualityMetrics { get; set; }
    public string? TestSynthesisAudioUrl { get; set; }
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("device")]
    public string? Device { get; set; }

    [JsonPropertyName("candidate_metrics")]
    public List<VoiceCloningCandidateMetricDto>? CandidateMetrics { get; set; }
  }

  public class VoiceCloningCandidateMetricDto
  {
    [JsonPropertyName("reference_audio")]
    public string? ReferenceAudio { get; set; }

    [JsonPropertyName("metrics")]
    public Dictionary<string, object>? Metrics { get; set; }

    [JsonPropertyName("score")]
    public double? Score { get; set; }

    [JsonPropertyName("selected")]
    public bool? Selected { get; set; }

    [JsonPropertyName("device")]
    public string? Device { get; set; }

    public Dictionary<string, object> ToDictionary()
    {
      return new Dictionary<string, object>
      {
        { "reference_audio", ReferenceAudio ?? string.Empty },
        { "metrics", Metrics ?? new Dictionary<string, object>() },
        { "score", Score ?? 0.0 },
        { "selected", Selected ?? false },
        { "device", Device ?? string.Empty },
      };
    }
  }

  public class VoiceCloningWizardFinalizeRequest
  {
    public string JobId { get; set; } = string.Empty;
    public string? ProfileName { get; set; }
    public string? ProfileDescription { get; set; }
  }

  public class VoiceCloningWizardFinalizeResponse
  {
    public string ProfileId { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;
    public bool Success { get; set; }
  }
}
