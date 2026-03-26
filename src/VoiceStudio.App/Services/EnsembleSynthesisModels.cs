using System;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Request/response models for the ensemble synthesis API (/api/ensemble).
  /// </summary>
  public class EnsembleSynthesisRequest
  {
    public EnsembleVoiceRequest[] Voices { get; set; } = Array.Empty<EnsembleVoiceRequest>();
    public string? ProjectId { get; set; }
    public string MixMode { get; set; } = "sequential";
    public string OutputFormat { get; set; } = "wav";
  }

  public class EnsembleVoiceRequest
  {
    public string ProfileId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Engine { get; set; } = "xtts";
    public string Language { get; set; } = "en";
    public string? Emotion { get; set; }
  }

  public class EnsembleSynthesisResponse
  {
    public string JobId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string[] AudioIds { get; set; } = Array.Empty<string>();
    public string Message { get; set; } = string.Empty;
  }

  public class EnsembleJobStatus
  {
    public string JobId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public double Progress { get; set; }
    public int CompletedVoices { get; set; }
    public int TotalVoices { get; set; }
    public string[] AudioIds { get; set; } = Array.Empty<string>();
    public string? Error { get; set; }
    public string Created { get; set; } = string.Empty;
    public string Updated { get; set; } = string.Empty;
  }
}
