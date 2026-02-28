using System;
using System.Collections.Generic;

namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// Training dataset information.
  /// </summary>
  public class TrainingDataset
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string> AudioFiles { get; set; } = new(); // List of audio IDs or file paths
    public List<string>? Transcripts { get; set; } // Optional transcripts for each audio file
    public DateTime Created { get; set; }
    public DateTime Modified { get; set; }
  }

  /// <summary>
  /// Request to start training.
  /// </summary>
  public class TrainingRequest
  {
    public string DatasetId { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string Engine { get; set; } = "xtts"; // xtts, rvc, coqui
    public int Epochs { get; set; } = 100;
    public int BatchSize { get; set; } = 4;
    public double LearningRate { get; set; } = 0.0001;
    public bool Gpu { get; set; } = true;
    public string? OutputPath { get; set; }
  }

  /// <summary>
  /// Training job status.
  /// </summary>
  public class TrainingStatus
  {
    public string Id { get; set; } = string.Empty;
    public string DatasetId { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string Engine { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // pending, running, paused, completed, failed, cancelled
    public double Progress { get; set; } // 0.0 to 1.0
    public int CurrentEpoch { get; set; }
    public int TotalEpochs { get; set; }
    public double? Loss { get; set; }
    public DateTime? Started { get; set; }
    public DateTime? Completed { get; set; }
    public string? ErrorMessage { get; set; }

    // Quality metrics (IDEA 54)
    public double? QualityScore { get; set; }
    public double? ValidationLoss { get; set; }
    public List<TrainingQualityAlert>? QualityAlerts { get; set; }
    public EarlyStoppingRecommendation? EarlyStoppingRecommendation { get; set; }
  }

  /// <summary>
  /// Single log entry from training.
  /// </summary>
  public class TrainingLogEntry
  {
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = string.Empty; // info, warning, error
    public string Message { get; set; } = string.Empty;
    public int? Epoch { get; set; }
    public double? Loss { get; set; }
  }
}