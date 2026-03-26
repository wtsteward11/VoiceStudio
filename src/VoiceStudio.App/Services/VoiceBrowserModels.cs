using System;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Response models for the voice browser API (/api/voice-browser).
  /// </summary>
  public class VoiceSearchResponse
  {
    public VoiceProfileSummary[] Voices { get; set; } = Array.Empty<VoiceProfileSummary>();
    public int Total { get; set; }
    public int Limit { get; set; }
    public int Offset { get; set; }
  }

  public class VoiceProfileSummary
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Language { get; set; } = string.Empty;
    public string? Gender { get; set; }
    public string? AgeRange { get; set; }
    public double QualityScore { get; set; }
    public int SampleCount { get; set; }
    public string[] Tags { get; set; } = Array.Empty<string>();
    public string? PreviewAudioId { get; set; }
    public string Created { get; set; } = string.Empty;
  }

  public class LanguagesResponse
  {
    public string[] Languages { get; set; } = Array.Empty<string>();
  }

  public class TagsResponse
  {
    public string[] Tags { get; set; } = Array.Empty<string>();
  }
}
