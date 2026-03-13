using System;

namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// Image search request.
  /// </summary>
  public class ImageSearchRequest
  {
    public string Query { get; set; } = string.Empty;
    public string? Source { get; set; }
    public string? Category { get; set; }
    public string? Orientation { get; set; }
    public string? Color { get; set; }
    public int Page { get; set; } = 1;
    public int PerPage { get; set; } = 20;
  }

  /// <summary>
  /// Image search response.
  /// </summary>
  public class ImageSearchResponse
  {
    public ImageSearchResult[] Results { get; set; } = Array.Empty<ImageSearchResult>();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PerPage { get; set; }
    public int TotalPages { get; set; }
    public string Query { get; set; } = string.Empty;
    public string? Source { get; set; }
  }

  /// <summary>
  /// Single image search result.
  /// </summary>
  public class ImageSearchResult
  {
    public string ResultId { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Source { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public int? FileSize { get; set; }
    public string? License { get; set; }
    public string? Author { get; set; }
    public string? AuthorUrl { get; set; }
    public string[] Tags { get; set; } = Array.Empty<string>();
  }

  /// <summary>
  /// Image source info from /api/image-search/sources.
  /// </summary>
  public class ImageSourceInfo
  {
    public string SourceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool RequiresApiKey { get; set; }
    public bool IsAvailable { get; set; }
  }
}
