namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// Upscaling engine from /api/upscaling/engines.
  /// </summary>
  public class UpscalingEngineResponse
  {
    public string EngineId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string[] SupportedTypes { get; set; } = System.Array.Empty<string>();
    public double[] SupportedScales { get; set; } = System.Array.Empty<double>();
    public bool IsAvailable { get; set; }
  }

  /// <summary>
  /// Upscaling job from /api/upscaling/jobs.
  /// </summary>
  public class UpscalingJobResponse
  {
    public string JobId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public double Progress { get; set; }
    public string? OutputFile { get; set; }
    public int? OriginalWidth { get; set; }
    public int? OriginalHeight { get; set; }
    public int? UpscaledWidth { get; set; }
    public int? UpscaledHeight { get; set; }
    public string? ErrorMessage { get; set; }
  }

  /// <summary>
  /// Request for upscaling (media_type, engine, scale_factor, output_format).
  /// </summary>
  public class UpscalingUpscaleRequest
  {
    public string MediaType { get; set; } = "image";
    public string Engine { get; set; } = string.Empty;
    public double ScaleFactor { get; set; } = 2.0;
    public string? OutputFormat { get; set; }
  }
}
