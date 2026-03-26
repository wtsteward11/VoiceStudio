using System.Collections.Generic;

namespace VoiceStudio.Core.Models
{
  public class MixAnalysisResponse
  {
    public List<MixSuggestionData>? Suggestions { get; set; }
  }

  public class MixSuggestionData
  {
    public string SuggestionId { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public float? CurrentValue { get; set; }
    public float? SuggestedValue { get; set; }
    public float Confidence { get; set; }
  }

  public class MixApplyRequest
  {
    public List<string> SuggestionIds { get; set; } = new();
    public bool ApplyAll { get; set; }
  }

  public class MixApplyResponse
  {
    public int Applied { get; set; }
    public string Message { get; set; } = string.Empty;
  }

  public class MasteringAnalysisRequest
  {
    public string ProjectId { get; set; } = string.Empty;
    public float TargetLoudness { get; set; }
    public string TargetFormat { get; set; } = string.Empty;
  }

  public class MasteringAnalysisResponse
  {
    public string ProjectId { get; set; } = string.Empty;
    public float CurrentLoudness { get; set; }
    public float TargetLoudness { get; set; }
    public float PeakLevel { get; set; }
    public float DynamicRange { get; set; }
    public Dictionary<string, float>? FrequencyBalance { get; set; }
    public List<Dictionary<string, object>>? Suggestions { get; set; }
  }

  public class MasteringApplyRequest
  {
    public string ProjectId { get; set; } = string.Empty;
    public MasteringSettingsData Settings { get; set; } = new();
  }

  public class MasteringSettingsData
  {
    public float Loudness { get; set; }
    public float PeakLimit { get; set; }
  }

  public class MasteringApplyResponse
  {
    public string ProjectId { get; set; } = string.Empty;
    public string OutputAudioId { get; set; } = string.Empty;
    public string OutputAudioUrl { get; set; } = string.Empty;
    public float FinalLoudness { get; set; }
    public string Message { get; set; } = string.Empty;
  }
}
