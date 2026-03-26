using System.Collections.Generic;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Job DTO from /api/jobs.
  /// </summary>
  public class Job
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public double Progress { get; set; }
    public string? CurrentStep { get; set; }
    public int? TotalSteps { get; set; }
    public int? CurrentStepIndex { get; set; }
    public string Created { get; set; } = string.Empty;
    public string? Started { get; set; }
    public string? Completed { get; set; }
    public int? EstimatedTimeRemaining { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ResultId { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
  }

  /// <summary>
  /// Job summary DTO from /api/jobs/summary.
  /// </summary>
  public class JobSummary
  {
    public int Total { get; set; }
    public int Pending { get; set; }
    public int Running { get; set; }
    public int Completed { get; set; }
    public int Failed { get; set; }
    public int Cancelled { get; set; }
    public Dictionary<string, int> ByType { get; set; } = new();
  }
}
