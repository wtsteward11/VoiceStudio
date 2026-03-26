using System;

namespace VoiceStudio.Core.Models
{
  public class AdvancedWaveformData
  {
    public string AudioId { get; set; } = string.Empty;
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public double Duration { get; set; }
    public double[][] Samples { get; set; } = Array.Empty<double[]>();
    public double[]? RmsValues { get; set; }
    public double[]? PeakValues { get; set; }
    public int[]? ZeroCrossings { get; set; }
    public double[] TimePoints { get; set; } = Array.Empty<double>();
  }

  public class AdvancedWaveformConfigRequest
  {
    public string AudioId { get; set; } = string.Empty;
    public double ZoomLevel { get; set; }
    public int[] ShowChannels { get; set; } = Array.Empty<int>();
    public bool ShowRms { get; set; }
    public bool ShowPeak { get; set; }
    public bool ShowZeroCrossings { get; set; }
    public string ColorScheme { get; set; } = string.Empty;
    public AdvancedWaveformTimeRange? TimeRange { get; set; }
  }

  public class AdvancedWaveformTimeRange
  {
    public double Start { get; set; }
    public double End { get; set; }
  }

  public class AdvancedWaveformConfigResponse
  {
    public string AudioId { get; set; } = string.Empty;
    public double ZoomLevel { get; set; }
    public int[] ShowChannels { get; set; } = Array.Empty<int>();
    public bool ShowRms { get; set; }
    public bool ShowPeak { get; set; }
    public bool ShowZeroCrossings { get; set; }
    public string ColorScheme { get; set; } = string.Empty;
    public AdvancedWaveformTimeRange? TimeRange { get; set; }
  }

  public class AdvancedWaveformAnalysis
  {
    public string AudioId { get; set; } = string.Empty;
    public double PeakAmplitude { get; set; }
    public double RmsAmplitude { get; set; }
    public double DynamicRange { get; set; }
    public double CrestFactor { get; set; }
    public double ZeroCrossingRate { get; set; }
    public double DcOffset { get; set; }
  }
}
