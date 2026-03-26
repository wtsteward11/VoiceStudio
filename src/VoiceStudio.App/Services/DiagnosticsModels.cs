using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Represents a span within a trace for timeline visualization.
  /// Phase 5.1.3: Distributed Tracing Visualization
  /// </summary>
  public class SpanEntry : ObservableObject
  {
    public string SpanId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double DurationMs { get; set; }
    public string Status { get; set; } = "Unknown";
    public string? ParentSpanId { get; set; }

    /// <summary>Gets the visual width percentage for timeline bar.</summary>
    public double WidthPercent => Math.Max(8, Math.Min(100, DurationMs / 10.0));

    /// <summary>Gets the status color for visualization.</summary>
    public string StatusColor => Status switch
    {
      "OK" or "Success" => "#4CAF50",
      "Error" => "#F44336",
      "Pending" => "#FFC107",
      _ => "#2196F3"
    };

    /// <summary>Gets the tooltip text for the span.</summary>
    public string TooltipText => $"{Name}: {DurationMs:F0}ms ({Status})";
  }

  /// <summary>
  /// Represents a distributed trace entry for timeline visualization.
  /// Phase 5.1.3: Distributed Tracing Visualization
  /// </summary>
  public class TraceEntry : ObservableObject
  {
    public string TraceId { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public double DurationMs { get; set; }
    public string Status { get; set; } = "Unknown";
    public string OperationName { get; set; } = string.Empty;
    public ObservableCollection<SpanEntry> Spans { get; set; } = new();

    /// <summary>Gets the shortened trace ID for display.</summary>
    public string ShortTraceId => TraceId.Length > 12
        ? $"{TraceId[..12]}..."
        : TraceId;

    /// <summary>Gets the formatted start time.</summary>
    public string StartTimeFormatted => StartTime.ToString("HH:mm:ss.fff");

    /// <summary>Gets the formatted duration string.</summary>
    public string DurationFormatted => DurationMs < 1000
        ? $"{DurationMs:F0}ms"
        : $"{DurationMs / 1000:F2}s";

    /// <summary>Gets the duration as a percentage for progress bar (scaled for visibility).</summary>
    public double DurationPercent => Math.Min(100, DurationMs / 50.0);

    /// <summary>Gets the status color for visualization.</summary>
    public string StatusColor => Status switch
    {
      "Success" or "OK" => "#4CAF50",
      "Error" => "#F44336",
      "Pending" => "#FFC107",
      _ => "#9E9E9E"
    };
  }

  /// <summary>
  /// Response model for trace list API.
  /// Phase 5.1.3: Distributed Tracing Visualization
  /// </summary>
  public class TraceListResponse
  {
    public List<TraceEntry> Traces { get; set; } = new();
    public int TotalCount { get; set; }
  }
}
