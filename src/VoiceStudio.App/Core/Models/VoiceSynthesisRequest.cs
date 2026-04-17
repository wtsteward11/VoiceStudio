using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VoiceStudio.Core.Models
{
  public class VoiceSynthesisRequest
  {
    public string Engine { get; set; } = "xtts"; // chatterbox, xtts, tortoise
    public string ProfileId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
    public string? Emotion { get; set; }
    public bool EnhanceQuality { get; set; }  // Enable quality enhancement pipeline
    /// <summary>
    /// Optional prosody/engine tuning. When null, properties are omitted from the backend JSON body so the
    /// server uses its defaults (matches minimal HTTP clients). Sending implicit UI defaults as numbers
    /// previously caused extra kwargs to be forwarded to Coqui XTTS and could break synthesis.
    /// </summary>
    public float? Speed { get; set; }
    public float? Pitch { get; set; }
    public float? Stability { get; set; }
    public float? Clarity { get; set; }
    public float? Temperature { get; set; }
  }

  public class VoiceSynthesisResponse
  {
    public string AudioId { get; set; } = string.Empty;
    public string AudioUrl { get; set; } = string.Empty;
    public double Duration { get; set; }
    public double QualityScore { get; set; }
    public QualityMetrics? QualityMetrics { get; set; } // Detailed quality metrics
    /// <summary>GAP-054: Present when SSML was detected or transformed.</summary>
    public SsmlHandlingDiagnostics? SsmlHandling { get; set; }

    /// <summary>GAP-050 consumer: prosody authority outcome after preset apply-extended.</summary>
    [JsonPropertyName("prosody_handling")]
    public ProsodyHandlingDiagnosticsDto? ProsodyHandling { get; set; }

    /// <summary>GAP-050: mapping lane from emotion mapper (e.g. canonical_preset).</summary>
    [JsonPropertyName("emotion_mapping_source")]
    public string EmotionMappingSource { get; set; } = string.Empty;

    /// <summary>
    /// When base synthesis succeeded but apply-extended failed or returned null; base audio ids remain valid.
    /// </summary>
    [JsonPropertyName("emotion_preset_apply_failure_message")]
    public string? EmotionPresetApplyFailureMessage { get; set; }
  }

  public class VoiceAnalysisResponse
  {
    public Dictionary<string, double> Metrics { get; set; } = new();
    public double QualityScore { get; set; }
  }

  public class VoiceCloneRequest
  {
    public string? Text { get; set; }
    public string Engine { get; set; } = "xtts";
    public string QualityMode { get; set; } = "standard"; // fast, standard, high, ultra
    public bool EnhanceQuality { get; set; }  // Apply advanced quality enhancement pipeline
    public bool UseMultiReference { get; set; }  // Use ensemble approach when multiple references provided
    public bool UseRvcPostprocessing { get; set; }  // Apply RVC post-processing for enhanced voice similarity
    public string Language { get; set; } = "en"; // Language code for synthesis
    public Dictionary<string, double>? ProsodyParams { get; set; } // Advanced prosody control: pitch (semitones), tempo (multiplier), formant_shift (factor), energy (multiplier)
    public string? ProjectId { get; set; }  // Optional project association for saved outputs
    public string? ProfileName { get; set; }  // Custom name for the created voice profile
  }

  public class VoiceCloneResponse
  {
    public string ProfileId { get; set; } = string.Empty;
    public string? AudioId { get; set; }
    public string? AudioUrl { get; set; }
    public double QualityScore { get; set; }
    public QualityMetrics? QualityMetrics { get; set; } // Detailed quality metrics
  }
}
