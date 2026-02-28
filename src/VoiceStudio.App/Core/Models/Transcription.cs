using System;
using System.Collections.Generic;

namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// Word with timestamp information.
  /// </summary>
  public class WordTimestamp
  {
    public string Word { get; set; } = string.Empty;
    public double Start { get; set; }
    public double End { get; set; }
    public double? Confidence { get; set; }
  }

  /// <summary>
  /// Segment of transcription with timestamps.
  /// </summary>
  public class TranscriptionSegment
  {
    public string Text { get; set; } = string.Empty;
    public double Start { get; set; }
    public double End { get; set; }
    public List<WordTimestamp>? Words { get; set; }
  }

  /// <summary>
  /// Request for transcription.
  /// </summary>
  public class TranscriptionRequest
  {
    public string AudioId { get; set; } = string.Empty;
    public string Engine { get; set; } = "whisper"; // whisper, whisperx, whisper-cpp, vosk
    public string? Language { get; set; } // Auto-detect if null
    public bool WordTimestamps { get; set; }
    public bool Diarization { get; set; }  // Speaker diarization (WhisperX only)
    public bool UseVad { get; set; }  // Voice Activity Detection (VAD) - improves accuracy by detecting voice segments
  }

  /// <summary>
  /// Response from transcription.
  /// </summary>
  public class TranscriptionResponse
  {
    public string Id { get; set; } = string.Empty;
    public string AudioId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public double Duration { get; set; }
    public List<TranscriptionSegment> Segments { get; set; } = new();
    public List<WordTimestamp> WordTimestamps { get; set; } = new();
    public DateTime Created { get; set; }
    public string Engine { get; set; } = string.Empty;
  }

  /// <summary>
  /// Supported language for transcription.
  /// </summary>
  public class SupportedLanguage
  {
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
  }

  /// <summary>
  /// Information about an available transcription engine.
  /// GAP-CS-003: Dynamic engine discovery from backend API.
  /// </summary>
  public class TranscriptionEngine
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool SupportsWordTimestamps { get; set; } = true;
    public bool SupportsDiarization { get; set; } = false;
    public bool SupportsVad { get; set; } = false;
  }
}