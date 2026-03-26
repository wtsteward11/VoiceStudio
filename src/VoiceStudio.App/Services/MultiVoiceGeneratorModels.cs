using System.Collections.Generic;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Request/response models for the multi-voice generator API (/api/voice/multi).
  /// </summary>
  public class MultiVoiceCSVImportRequest
  {
    public string CsvContent { get; set; } = string.Empty;
  }

  public class MultiVoiceCSVImportResponse
  {
    public List<MultiVoiceCSVItem> Items { get; set; } = new();
    public int Count { get; set; }
  }

  public class MultiVoiceCSVItem
  {
    public string ProfileId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Engine { get; set; } = string.Empty;
    public string QualityMode { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string? Emotion { get; set; }
  }

  public class MultiVoiceCSVExportResponse
  {
    public string JobId { get; set; } = string.Empty;
    public string CsvContent { get; set; } = string.Empty;
    public string Filename { get; set; } = string.Empty;
  }

  public class MultiVoiceGenerateRequest
  {
    public string Name { get; set; } = string.Empty;
    public List<Dictionary<string, object>> Items { get; set; } = new();
  }

  public class MultiVoiceGenerateResponse
  {
    public string JobId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int TotalItems { get; set; }
    public string Status { get; set; } = string.Empty;
  }

  public class MultiVoiceJobStatusResponse
  {
    public string JobId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public float Progress { get; set; }
    public int TotalItems { get; set; }
    public int CompletedCount { get; set; }
    public int FailedCount { get; set; }
    public List<MultiVoiceStatusItem> Items { get; set; } = new();
  }

  public class MultiVoiceStatusItem
  {
    public string ItemId { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Engine { get; set; } = string.Empty;
    public string QualityMode { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string? Emotion { get; set; }
    public string Status { get; set; } = string.Empty;
    public float Progress { get; set; }
    public string? AudioId { get; set; }
    public string? AudioUrl { get; set; }
    public float? QualityScore { get; set; }
    public Dictionary<string, object>? QualityMetrics { get; set; }
    public string? ErrorMessage { get; set; }
  }

  public class MultiVoiceResultsResponse
  {
    public string JobId { get; set; } = string.Empty;
    public List<MultiVoiceResultItem> Items { get; set; } = new();
  }

  public class MultiVoiceResultItem
  {
    public string ItemId { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Engine { get; set; } = string.Empty;
    public string QualityMode { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string? Emotion { get; set; }
    public string? AudioId { get; set; }
    public string? AudioUrl { get; set; }
    public float? QualityScore { get; set; }
    public Dictionary<string, object>? QualityMetrics { get; set; }
  }

  public class MultiVoiceCompareRequest
  {
    public List<string> AudioIds { get; set; } = new();
    public string ComparisonType { get; set; } = "quality";
  }

  public class MultiVoiceCompareResponse
  {
    public List<Dictionary<string, object>> Comparisons { get; set; } = new();
    public string? BestAudioId { get; set; }
    public float? BestScore { get; set; }
  }
}
