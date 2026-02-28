using System.Collections.Generic;

namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// Information about a stored model.
  /// </summary>
  public class ModelInfo
  {
    public string Engine { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string ModelPath { get; set; } = string.Empty;
    public string Checksum { get; set; } = string.Empty;
    public long Size { get; set; }
    public string? Version { get; set; }
    public string? DownloadedAt { get; set; }
    public string? UpdatedAt { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
  }

  /// <summary>
  /// Model verification response.
  /// </summary>
  public class ModelVerifyResponse
  {
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ExpectedChecksum { get; set; }
    public string? CurrentChecksum { get; set; }
  }

  /// <summary>
  /// Storage statistics.
  /// </summary>
  public class StorageStats
  {
    public int TotalModels { get; set; }
    public long TotalSize { get; set; }
    public double TotalSizeMb { get; set; }
    public double TotalSizeGb { get; set; }
    public Dictionary<string, int> Engines { get; set; } = new();
    public string BaseDir { get; set; } = string.Empty;
  }
}