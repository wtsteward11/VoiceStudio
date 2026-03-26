using System.Collections.Generic;

namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// Request to extract style from reference audio.
  /// </summary>
  public class VoiceStyleTransferExtractRequest
  {
    public string AudioId { get; set; } = string.Empty;
    public bool AnalyzeProsody { get; set; } = true;
    public bool AnalyzeEmotion { get; set; } = true;
  }

  /// <summary>
  /// Style profile extracted from reference audio.
  /// </summary>
  public class VoiceStyleTransferProfileResponse
  {
    public string AudioId { get; set; } = string.Empty;
    public float AveragePitch { get; set; }
    public float PitchVariation { get; set; }
    public float Energy { get; set; }
    public float SpeakingRate { get; set; }
    public string? EmotionTag { get; set; }
    public Dictionary<string, object>? ProsodicFeatures { get; set; }
    public List<float>? StyleEmbedding { get; set; }
  }

  /// <summary>
  /// Request to analyze style characteristics.
  /// </summary>
  public class VoiceStyleTransferAnalyzeRequest
  {
    public string AudioId { get; set; } = string.Empty;
  }

  /// <summary>
  /// Style analysis response.
  /// </summary>
  public class VoiceStyleTransferAnalyzeResponse
  {
    public string AudioId { get; set; } = string.Empty;
    public List<float>? PitchContour { get; set; }
    public List<float>? EnergyContour { get; set; }
    public Dictionary<string, object>? TimingPatterns { get; set; }
    public List<Dictionary<string, object>>? StyleMarkers { get; set; }
  }

  /// <summary>
  /// Request to synthesize with style transfer.
  /// </summary>
  public class VoiceStyleTransferSynthesizeRequest
  {
    public string VoiceProfileId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? ReferenceAudioId { get; set; }
    public List<float>? StyleEmbedding { get; set; }
    public float StyleIntensity { get; set; } = 0.8f;
    public string Language { get; set; } = "en";
  }

  /// <summary>
  /// Style synthesis response.
  /// </summary>
  public class VoiceStyleTransferSynthesizeResponse
  {
    public string AudioId { get; set; } = string.Empty;
    public string AudioUrl { get; set; } = string.Empty;
    public float Duration { get; set; }
    public bool StyleApplied { get; set; }
  }
}
