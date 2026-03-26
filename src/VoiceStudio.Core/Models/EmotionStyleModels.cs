using System.Collections.Generic;

namespace VoiceStudio.Core.Models
{
  public class EmotionStyleEmotionPreset
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Emotion { get; set; } = string.Empty;
    public double Intensity { get; set; }
    public Dictionary<string, double> Parameters { get; set; } = new();
    public string Created { get; set; } = string.Empty;
  }

  public class EmotionStyleStylePreset
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Style { get; set; } = string.Empty;
    public Dictionary<string, double> Parameters { get; set; } = new();
    public string Created { get; set; } = string.Empty;
  }

  public class EmotionStyleApplyRequest
  {
    public string ProfileId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? EmotionPresetId { get; set; }
    public string? StylePresetId { get; set; }
    public string? Emotion { get; set; }
    public string? Style { get; set; }
    public double? Intensity { get; set; }
  }

  public class EmotionStyleApplyResponse
  {
    public string AudioId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
  }
}
