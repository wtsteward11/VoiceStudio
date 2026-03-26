using System.Collections.Generic;

namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// Style transfer preset from /api/style-transfer/presets.
  /// </summary>
  public class StyleTransferPresetResponse
  {
    public string PresetId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? VoiceProfileId { get; set; }
    public Dictionary<string, object> StyleCharacteristics { get; set; } = new();
    public string Created { get; set; } = string.Empty;
  }

  /// <summary>
  /// Style transfer job from /api/style-transfer/jobs.
  /// </summary>
  public class StyleTransferJobResponse
  {
    public string JobId { get; set; } = string.Empty;
    public string SourceAudioId { get; set; } = string.Empty;
    public string TargetStyleId { get; set; } = string.Empty;
    public double TransferStrength { get; set; }
    public string Status { get; set; } = string.Empty;
    public double Progress { get; set; }
    public string? OutputAudioId { get; set; }
    public string? ErrorMessage { get; set; }
    public string Created { get; set; } = string.Empty;
    public string? Completed { get; set; }
  }

  /// <summary>
  /// Request to create a style transfer job.
  /// </summary>
  public class StyleTransferCreateRequest
  {
    public string SourceAudioId { get; set; } = string.Empty;
    public string TargetStyleId { get; set; } = string.Empty;
    public double TransferStrength { get; set; } = 0.8;
    public bool PreserveContent { get; set; } = true;
    public bool PreserveEmotion { get; set; }
    public string OutputFormat { get; set; } = "wav";
  }
}
