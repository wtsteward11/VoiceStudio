using System;
using System.Collections.Generic;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// API response model for edit session.
  /// </summary>
  public class EditorSession
  {
    public string SessionId { get; set; } = string.Empty;
    public string? ProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public TextSegment[] Segments { get; set; } = Array.Empty<TextSegment>();
    public string? AudioId { get; set; }
    public string Language { get; set; } = "en";
    public string Created { get; set; } = string.Empty;
    public string Modified { get; set; } = string.Empty;
  }

  /// <summary>
  /// API model for text segment.
  /// </summary>
  public class TextSegment
  {
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public double StartTime { get; set; }
    public double EndTime { get; set; }
    public string? Speaker { get; set; }
    public Dictionary<string, object>? Prosody { get; set; }
    public string[]? Phonemes { get; set; }
    public string? Notes { get; set; }
  }

  /// <summary>
  /// API response for session synthesize.
  /// </summary>
  public class TextSpeechSynthesisResponse
  {
    public string AudioId { get; set; } = string.Empty;
    public double Duration { get; set; }
    public string Message { get; set; } = string.Empty;
  }

  /// <summary>
  /// API response for SSML preview.
  /// </summary>
  public class SSMLPreviewResponse
  {
    public string AudioId { get; set; } = string.Empty;
    public double Duration { get; set; }
    public string Message { get; set; } = string.Empty;
  }
}
