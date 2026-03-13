using System;

namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// Ultimate dashboard API response.
  /// </summary>
  public class UltimateDashboardData
  {
    public UltimateDashboardSummary Summary { get; set; } = new();
    public UltimateQuickStat[] QuickStats { get; set; } = Array.Empty<UltimateQuickStat>();
    public UltimateRecentActivity[] RecentActivities { get; set; } = Array.Empty<UltimateRecentActivity>();
    public string[] SystemAlerts { get; set; } = Array.Empty<string>();
  }

  /// <summary>
  /// Dashboard summary section.
  /// </summary>
  public class UltimateDashboardSummary
  {
    public int TotalProjects { get; set; }
    public int TotalProfiles { get; set; }
    public int TotalAudioFiles { get; set; }
    public int ActiveJobs { get; set; }
    public int CompletedJobsToday { get; set; }
    public string SystemStatus { get; set; } = string.Empty;
    public bool GpuAvailable { get; set; }
    public double GpuUtilization { get; set; }
    public double CpuUtilization { get; set; }
    public double MemoryUsagePercent { get; set; }
  }

  /// <summary>
  /// Quick stat item.
  /// </summary>
  public class UltimateQuickStat
  {
    public string StatId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Trend { get; set; }
    public double? TrendValue { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }
  }

  /// <summary>
  /// Recent activity item.
  /// </summary>
  public class UltimateRecentActivity
  {
    public string ActivityId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Timestamp { get; set; } = string.Empty;
  }
}
