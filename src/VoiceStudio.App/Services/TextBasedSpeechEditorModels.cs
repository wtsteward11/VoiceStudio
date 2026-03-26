using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Request/response models for the text-based speech editor API (/api/transcribe, /api/edit).
  /// Prefixed TextEdit to avoid conflict with VoiceStudio.Core.Models.TranscriptionRequest/TranscriptionResponse.
  /// </summary>
  public class TextEditTranscriptionRequest
  {
    public string AudioId { get; set; } = string.Empty;
    public string Engine { get; set; } = "whisper";
    public string? Language { get; set; }
    public bool WordTimestamps { get; set; }
  }

  public class TextEditTranscriptionResponse
  {
    public string Text { get; set; } = string.Empty;
    public List<TextEditTranscriptionSegmentData>? Segments { get; set; }
  }

  public class TextEditTranscriptionSegmentData
  {
    public string Text { get; set; } = string.Empty;
    public double Start { get; set; }
    public double End { get; set; }
    public List<TextEditWordTimestampData>? Words { get; set; }
  }

  public class TextEditWordTimestampData
  {
    public string Word { get; set; } = string.Empty;
    public double Start { get; set; }
    public double End { get; set; }
    public double? Confidence { get; set; }
  }

  public class EditSessionCreateRequest
  {
    public string AudioId { get; set; } = string.Empty;
    public string Transcript { get; set; } = string.Empty;
  }

  public class EditSessionCreateResponse
  {
    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = string.Empty;
  }

  public class AlignRequest
  {
    public string AudioId { get; set; } = string.Empty;
    public string Transcript { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
  }

  public class AlignResponse
  {
    public List<AlignSegmentData> Segments { get; set; } = new();
    public float AlignmentConfidence { get; set; }
  }

  public class AlignSegmentData
  {
    public string Text { get; set; } = string.Empty;
    public float StartTime { get; set; }
    public float EndTime { get; set; }
    public List<AlignWordData> Words { get; set; } = new();
  }

  public class AlignWordData
  {
    public string Word { get; set; } = string.Empty;
    public float StartTime { get; set; }
    public float EndTime { get; set; }
    public float Confidence { get; set; }
  }

  public class ReplaceWordRequest
  {
    public string SessionId { get; set; } = string.Empty;
    public int SegmentIndex { get; set; }
    public int WordIndex { get; set; }
    public string NewText { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string Engine { get; set; } = "xtts";
    public string QualityMode { get; set; } = "standard";
  }

  public class ReplaceWordResponse
  {
    public string ReplacedAudioId { get; set; } = string.Empty;
    public string ReplacedAudioUrl { get; set; } = string.Empty;
    public float Duration { get; set; }
    public List<TextEditTranscriptionSegmentData> UpdatedSegments { get; set; } = new();
  }

  public class InsertTextRequest
  {
    public string SessionId { get; set; } = string.Empty;
    public float Position { get; set; }
    public string Text { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string Engine { get; set; } = "xtts";
    public string QualityMode { get; set; } = "standard";
  }

  public class InsertTextResponse
  {
    public string InsertedAudioId { get; set; } = string.Empty;
    public string InsertedAudioUrl { get; set; } = string.Empty;
    public float Duration { get; set; }
    public List<TextEditTranscriptionSegmentData> NewSegments { get; set; } = new();
  }

  public class RemoveFillerWordsRequest
  {
    public string SessionId { get; set; } = string.Empty;
    public List<string> FillerWords { get; set; } = new();
  }

  public class RemoveFillerWordsResponse
  {
    public string UpdatedTranscript { get; set; } = string.Empty;
    public int RemovedCount { get; set; }
    public List<string> RemovedWords { get; set; } = new();
  }

  public class ApplyEditsRequest
  {
    public string SessionId { get; set; } = string.Empty;
    public string? OutputName { get; set; }
  }

  public class ApplyEditsResponse
  {
    public string FinalAudioId { get; set; } = string.Empty;
    public string FinalAudioUrl { get; set; } = string.Empty;
    public float Duration { get; set; }
    public int EditCount { get; set; }
  }
}
