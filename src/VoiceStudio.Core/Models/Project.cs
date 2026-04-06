using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VoiceStudio.Core.Models
{
  public class Project
  {
    /// <summary>On-disk JSON schema (JsonProjectRepository). 0 = legacy file without field.</summary>
    public int PersistedProjectSchemaVersion { get; set; }

    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
    public List<string> VoiceProfileIds { get; set; } = new List<string>();
    public List<AudioTrack> Tracks { get; set; } = new List<AudioTrack>();
    public List<ClipTranscriptLink> ClipTranscriptLinks { get; set; } = new List<ClipTranscriptLink>();

    /// <summary>
    /// GAP-045 last-subtitle restore: backend transcription id last shown on Timeline subtitle overlay for this project (local JSON only).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastSubtitleTranscriptionId { get; set; }
  }
}