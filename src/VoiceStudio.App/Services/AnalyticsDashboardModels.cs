using System.Collections.Generic;

namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// Analytics API response models for AnalyticsDashboard panel.
  /// </summary>
  public class AnalyticsDashboardSummary
  {
    public string PeriodStart { get; set; } = string.Empty;
    public string PeriodEnd { get; set; } = string.Empty;
    public int TotalSynthesis { get; set; }
    public int TotalProjects { get; set; }
    public int TotalAudioProcessed { get; set; }
    public double TotalProcessingTime { get; set; }
    public double AverageQualityScore { get; set; }
    public AnalyticsDashboardCategory[] Categories { get; set; } = System.Array.Empty<AnalyticsDashboardCategory>();
  }

  public class AnalyticsDashboardCategory
  {
    public string Category { get; set; } = string.Empty;
    public double Total { get; set; }
    public int Count { get; set; }
    public double Average { get; set; }
    public double MinValue { get; set; }
    public double MaxValue { get; set; }
    public string Trend { get; set; } = string.Empty;
  }

  public class AnalyticsDashboardMetric
  {
    public string Timestamp { get; set; } = string.Empty;
    public double Value { get; set; }
    public string? Label { get; set; }
  }

  public class AnalyticsDashboardStatisticalResponse
  {
    public double Mean { get; set; }
    public double Median { get; set; }
    public double? Mode { get; set; }
    public double StandardDeviation { get; set; }
    public double Variance { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
    public double Range { get; set; }
    public double Q1 { get; set; }
    public double Q3 { get; set; }
    public double IQR { get; set; }
    public double Skewness { get; set; }
    public double Kurtosis { get; set; }
    public int SampleSize { get; set; }
    public Dictionary<string, double>? Correlations { get; set; }
    public Dictionary<string, AnalyticsDashboardStatisticalTestResult>? TestResults { get; set; }
  }

  public class AnalyticsDashboardStatisticalTestResult
  {
    public string TestName { get; set; } = string.Empty;
    public double TestStatistic { get; set; }
    public double PValue { get; set; }
    public bool Significant { get; set; }
    public string Interpretation { get; set; } = string.Empty;
  }
}
