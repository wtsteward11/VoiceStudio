using System;
using System.Collections.Generic;

namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// DTO for a single SLO metric from the diagnostics API.
  /// </summary>
  public class SloMetricDto
  {
    public string Name { get; set; } = string.Empty;
    public double CurrentValue { get; set; }
    public double Target { get; set; }
    public double WarningThreshold { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string MetricType { get; set; } = string.Empty;
  }

  /// <summary>
  /// Response model for SLO data API (/api/v1/diagnostics/slo).
  /// </summary>
  public class SloDataResponse
  {
    public List<SloMetricDto> Slos { get; set; } = new();
    public DateTime Timestamp { get; set; }
  }
}
