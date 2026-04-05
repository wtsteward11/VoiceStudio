using System;
using System.Collections.Generic;

namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// Represents an audio clip in the timeline.
  /// A single audio segment that can be placed on a track.
  /// </summary>
  public class AudioClip
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string AudioId { get; set; } = string.Empty; // Backend audio ID
    public string AudioUrl { get; set; } = string.Empty; // Backend audio URL
    public TimeSpan Duration { get; set; }
    public double StartTime { get; set; } // Position in timeline (seconds)
    public double EndTime => StartTime + Duration.TotalSeconds;

    /// <summary>Non-destructive offset into source media in seconds (trim-in).</summary>
    public double SourceStartSeconds { get; set; }

    /// <summary>Linear fade-in duration for export/mixdown.</summary>
    public double FadeInSeconds { get; set; }

    /// <summary>Linear fade-out duration for export/mixdown.</summary>
    public double FadeOutSeconds { get; set; }

    public string? Engine { get; set; } // Engine used for synthesis
    public double? QualityScore { get; set; } // Quality score from synthesis
    public List<float>? WaveformSamples { get; set; } // Waveform data for visualization (normalized -1.0 to 1.0)

    /// <summary>GAP-045 Option B: persisted transcript truth vs this clip's audio (project JSON authority).</summary>
    public TranscriptTruthState TranscriptTruth { get; set; }

    /// <summary>GAP-040: optional split lineage — id of the pre-split clip this row was derived from (typically right segment only).</summary>
    public string? DerivedFromClipId { get; set; }
  }
}
