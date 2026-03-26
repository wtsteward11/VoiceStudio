using System;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Request/response models for the embedding explorer API (/api/embedding-explorer).
  /// </summary>
  public class EmbeddingVector
  {
    public string EmbeddingId { get; set; } = string.Empty;
    public string VoiceProfileId { get; set; } = string.Empty;
    public double[] Vector { get; set; } = Array.Empty<double>();
    public int Dimension { get; set; }
    public string Created { get; set; } = string.Empty;
  }

  public class EmbeddingSimilarity
  {
    public string EmbeddingId1 { get; set; } = string.Empty;
    public string EmbeddingId2 { get; set; } = string.Empty;
    public double Similarity { get; set; }
    public double Distance { get; set; }
  }

  public class EmbeddingVisualization
  {
    public string EmbeddingId { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double? Z { get; set; }
    public string? Color { get; set; }
  }

  public class EmbeddingCluster
  {
    public string ClusterId { get; set; } = string.Empty;
    public string[] EmbeddingIds { get; set; } = Array.Empty<string>();
    public double[] Centroid { get; set; } = Array.Empty<double>();
    public int Size { get; set; }
  }
}
