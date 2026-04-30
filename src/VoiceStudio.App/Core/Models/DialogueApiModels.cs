using System.Text.Json.Serialization;

namespace VoiceStudio.App.Core.Models;

public sealed class RegenerateDialogueSegmentRequest
{
  public string TranscriptId { get; set; } = string.Empty;

  public string ProfileId { get; set; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? TrackId { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Engine { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? ProjectId { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? SessionId { get; set; }

  [JsonPropertyName("replace_existing_clip")]
  public bool ReplaceExistingClip { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [JsonPropertyName("edited_text")]
  public string? EditedText { get; set; }
}

public sealed class RegenerateDialogueSegmentResponse
{
  [JsonPropertyName("project_id")]
  public string? ProjectId { get; set; }

  [JsonPropertyName("session_id")]
  public string SessionId { get; set; } = string.Empty;

  [JsonPropertyName("transcript_id")]
  public string TranscriptId { get; set; } = string.Empty;

  [JsonPropertyName("segment_id")]
  public string SegmentId { get; set; } = string.Empty;

  public string Status { get; set; } = string.Empty;

  [JsonPropertyName("audio_id")]
  public string AudioId { get; set; } = string.Empty;

  [JsonPropertyName("generated_audio_id")]
  public string? GeneratedAudioId { get; set; }

  [JsonPropertyName("library_asset_id")]
  public string LibraryAssetId { get; set; } = string.Empty;

  [JsonPropertyName("timeline_clip_id")]
  public string TimelineClipId { get; set; } = string.Empty;

  [JsonPropertyName("routed_engine")]
  public string RoutedEngine { get; set; } = string.Empty;

  public double Duration { get; set; }

  public DialogueSegmentPayload Segment { get; set; } = new();
}

public sealed class DialogueSegmentPayload
{
  [JsonPropertyName("transcript_id")]
  public string TranscriptId { get; set; } = string.Empty;

  public string Id { get; set; } = string.Empty;

  [JsonPropertyName("timeline_clip_id")]
  public string? TimelineClipId { get; set; }
}
