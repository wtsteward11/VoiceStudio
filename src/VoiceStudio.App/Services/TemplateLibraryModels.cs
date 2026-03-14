using System.Collections.Generic;

namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// Template DTO from /api/templates.
  /// </summary>
  public class TemplateLibraryTemplate
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public Dictionary<string, object> ProjectData { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public string? Author { get; set; }
    public string Version { get; set; } = "1.0";
    public bool IsPublic { get; set; }
    public int UsageCount { get; set; }
    public string Created { get; set; } = string.Empty;
    public string Modified { get; set; } = string.Empty;
  }

  /// <summary>
  /// Result of applying a template.
  /// </summary>
  public class TemplateApplyResult
  {
    public bool Success { get; set; }
    public string ProjectId { get; set; } = string.Empty;
    public string TemplateId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
  }
}
