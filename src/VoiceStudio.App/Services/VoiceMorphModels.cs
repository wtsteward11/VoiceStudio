using System.Collections.Generic;

namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// Voice blend configuration from /api/voice-morph.
  /// </summary>
  public class VoiceMorphBlend
  {
    public string VoiceProfileId { get; set; } = string.Empty;
    public double Weight { get; set; }
  }

  /// <summary>
  /// Morph config DTO from /api/voice-morph/configs.
  /// </summary>
  public class VoiceMorphConfig
  {
    public string ConfigId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SourceAudioId { get; set; } = string.Empty;
    public VoiceMorphBlend[] TargetVoices { get; set; } = System.Array.Empty<VoiceMorphBlend>();
    public double MorphStrength { get; set; }
    public bool PreserveEmotion { get; set; }
    public bool PreserveProsody { get; set; }
    public string OutputFormat { get; set; } = "wav";
  }

  /// <summary>
  /// Response from POST /api/voice-morph/apply.
  /// </summary>
  public class VoiceMorphApplyResponse
  {
    public string AudioId { get; set; } = string.Empty;
    public string ConfigApplied { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
  }
}
