namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// Display model for a transcript segment rendered on the timeline.
  /// Contains both the transcription data and the computed pixel position/width
  /// for rendering in the timeline overlay.
  /// 
  /// Core Workflow Audit Remediation - Phase C (M-1 + X-3).
  /// </summary>
  public class TranscriptSegmentDisplay
  {
    /// <summary>Segment text content.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Backend segment id when transcript is linked (GAP-033).</summary>
    public string? SegmentId { get; set; }

    /// <summary>Start time in seconds.</summary>
    public double StartSeconds { get; set; }

    /// <summary>End time in seconds.</summary>
    public double EndSeconds { get; set; }

    /// <summary>Duration in seconds.</summary>
    public double DurationSeconds => EndSeconds - StartSeconds;

    /// <summary>Computed left position in pixels (based on zoom level).</summary>
    public double PositionPixels { get; set; }

    /// <summary>Computed width in pixels (based on zoom level).</summary>
    public double WidthPixels { get; set; }

    /// <summary>Formatted time range for tooltip display.</summary>
    public string TimeRange => $"{StartSeconds:F1}s - {EndSeconds:F1}s";
  }
}
