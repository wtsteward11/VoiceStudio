using System.Collections.Generic;

namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// Represents an audio track in the timeline.
  /// Contains multiple audio clips arranged in time.
  /// </summary>
  public class AudioTrack
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public List<AudioClip> Clips { get; set; } = new List<AudioClip>();
    public int TrackNumber { get; set; }
    public string? Engine { get; set; } // Engine used for this track
    public bool IsMuted { get; set; } = false; // Track mute state
    public bool IsSolo { get; set; } = false; // Track solo state
  }
}