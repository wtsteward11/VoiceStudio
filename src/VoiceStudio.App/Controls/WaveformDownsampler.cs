#nullable enable

using System;
using System.Collections.Generic;

namespace VoiceStudio.App.Controls;

/// <summary>
/// Deterministic waveform downsampling shared by CPU Path and Win2D render paths (GAP-038 slice 1).
/// </summary>
public static class WaveformDownsampler
{
  /// <summary>
  /// Downsamples <paramref name="samples"/> to at most <paramref name="targetCount"/> points using peak or RMS bucketing.
  /// </summary>
  public static List<float> Downsample(IReadOnlyList<float> samples, int targetCount, string mode)
  {
    ArgumentNullException.ThrowIfNull(samples);
    if (targetCount <= 0)
    {
      return new List<float>();
    }

    if (samples.Count <= targetCount)
    {
      var copy = new List<float>(samples.Count);
      foreach (var s in samples)
      {
        copy.Add(s);
      }

      return copy;
    }

    var result = new List<float>(targetCount);
    var step = (double)samples.Count / targetCount;
    var isPeak = string.Equals(mode, "peak", StringComparison.OrdinalIgnoreCase);

    for (var i = 0; i < targetCount; i++)
    {
      var startIdx = (int)(i * step);
      var endIdx = Math.Min((int)((i + 1) * step), samples.Count);

      if (isPeak)
      {
        var max = 0f;
        for (var j = startIdx; j < endIdx; j++)
        {
          max = Math.Max(max, Math.Abs(samples[j]));
        }

        result.Add(max * Math.Sign(samples[startIdx]));
      }
      else
      {
        var sum = 0f;
        var count = endIdx - startIdx;
        for (var j = startIdx; j < endIdx; j++)
        {
          sum += samples[j];
        }

        result.Add(count > 0 ? sum / count : 0f);
      }
    }

    return result;
  }
}
