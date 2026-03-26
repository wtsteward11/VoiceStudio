using System.Collections.Generic;

namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// Mix assistant API suggestion response.
  /// </summary>
  public class MixSuggestion
  {
    public string SuggestionId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Parameter { get; set; }
    public double? CurrentValue { get; set; }
    public double? SuggestedValue { get; set; }
    public double Confidence { get; set; }
    public string Created { get; set; } = string.Empty;
  }

  /// <summary>
  /// Mix assistant preset response.
  /// </summary>
  public class MixPreset
  {
    public string PresetId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Genre { get; set; }
    public Dictionary<string, object> Settings { get; set; } = new();
    public string Created { get; set; } = string.Empty;
  }
}
