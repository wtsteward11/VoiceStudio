using System;
using System.Collections.Generic;

namespace VoiceStudio.Core.Models
{
  public class AdvancedSpectrogramViewTypesResponse
  {
    public AdvancedSpectrogramViewTypeInfo[] ViewTypes { get; set; } = Array.Empty<AdvancedSpectrogramViewTypeInfo>();
  }

  public class AdvancedSpectrogramViewTypeInfo
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
  }

  public class AdvancedSpectrogramGenerateRequest
  {
    public string AudioId { get; set; } = string.Empty;
    public string ViewType { get; set; } = string.Empty;
    public int WindowSize { get; set; }
    public int HopLength { get; set; }
    public int NFFT { get; set; }
    public AdvancedSpectrogramRange? FrequencyRange { get; set; }
    public AdvancedSpectrogramTimeRange? TimeRange { get; set; }
    public string ColorScheme { get; set; } = string.Empty;
    public bool ApplyFilters { get; set; }
    public string[] Filters { get; set; } = Array.Empty<string>();
  }

  public class AdvancedSpectrogramRange
  {
    public double? Min { get; set; }
    public double? Max { get; set; }
  }

  public class AdvancedSpectrogramTimeRange
  {
    public double? Start { get; set; }
    public double? End { get; set; }
  }

  public class AdvancedSpectrogramGenerateResponse
  {
    public string ViewId { get; set; } = string.Empty;
    public string? DataUrl { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    public string Message { get; set; } = string.Empty;
  }

  public class AdvancedSpectrogramCompareRequest
  {
    public string[] AudioIds { get; set; } = Array.Empty<string>();
    public string ComparisonType { get; set; } = string.Empty;
  }

  public class AdvancedSpectrogramCompareResponse
  {
    public string Id { get; set; } = string.Empty;
    public string[] AudioIds { get; set; } = Array.Empty<string>();
    public string ComparisonType { get; set; } = string.Empty;
    public Dictionary<string, object> ResultData { get; set; } = new();
    public string Created { get; set; } = string.Empty;
  }
}
