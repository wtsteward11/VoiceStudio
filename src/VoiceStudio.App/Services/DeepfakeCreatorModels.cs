namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// Deepfake Creator API models. Matches backend /api/deepfake-creator routes.
  /// </summary>
  public class DeepfakeEngine
  {
    public string EngineId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string[] SupportedTypes { get; set; } = System.Array.Empty<string>();
    public bool RequiresConsent { get; set; }
    public bool WatermarkRequired { get; set; }
    public bool IsAvailable { get; set; }
  }

  public class DeepfakeJob
  {
    public string JobId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public double Progress { get; set; }
    public string? OutputFile { get; set; }
    public bool ConsentGiven { get; set; }
    public bool WatermarkApplied { get; set; }
    public string? ErrorMessage { get; set; }
  }

  public class DeepfakeJobResponse
  {
    public string JobId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
  }
}
