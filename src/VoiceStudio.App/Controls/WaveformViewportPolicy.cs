#nullable enable

using System;
using System.Collections.Generic;

namespace VoiceStudio.App.Controls;

/// <summary>
/// Pure viewport/window policy for timeline waveform virtualization (GAP-038 slice 2).
/// Timeline presentation seam computes normalized windows; <see cref="WaveformControl"/> stays render-only.
/// </summary>
public static class WaveformViewportPolicy
{
  private const double FullViewportEpsilon = 1e-12;

  /// <summary>
  /// Computes a normalized time window <c>[start, start+width]</c> ⊆ <c>[0,1]</c> over the reference duration.
  /// When <paramref name="referenceDurationSeconds"/> ≤ 0 or non-finite, returns the full window <c>(0,1)</c> (no virtualization).
  /// </summary>
  /// <param name="focusTimeSeconds">Typically current playback time in seconds.</param>
  /// <param name="referenceDurationSeconds">Typically <see cref="VoiceStudio.Core.Services.IAudioPlayerService.Duration"/>.</param>
  /// <param name="timelineZoom">Timeline zoom; visible fraction is <c>min(1, 1/zoom)</c>.</param>
  public static (double StartNormalized, double WidthNormalized) ComputeNormalizedViewport(
      double focusTimeSeconds,
      double referenceDurationSeconds,
      double timelineZoom)
  {
    if (referenceDurationSeconds <= 0 ||
        double.IsNaN(referenceDurationSeconds) ||
        double.IsInfinity(referenceDurationSeconds))
    {
      return (0, 1);
    }

    var zoom = timelineZoom <= 0 || double.IsNaN(timelineZoom) || double.IsInfinity(timelineZoom)
        ? 1.0
        : timelineZoom;

    var widthNorm = Math.Min(1.0, 1.0 / zoom);
    var centerNorm = Math.Clamp(focusTimeSeconds / referenceDurationSeconds, 0, 1);
    var half = widthNorm * 0.5;
    var start = centerNorm - half;
    if (start < 0)
    {
      start = 0;
    }

    if (start + widthNorm > 1.0)
    {
      start = Math.Max(0.0, 1.0 - widthNorm);
    }

    return (start, widthNorm);
  }

  /// <summary>
  /// Copies samples for the inclusive index range derived from normalized window. Clamps indices; returns at least one sample when input is non-empty and width &gt; 0.
  /// </summary>
  public static List<float> SliceSamples(IReadOnlyList<float> samples, double startNorm, double widthNorm)
  {
    ArgumentNullException.ThrowIfNull(samples);
    var total = samples.Count;
    if (total == 0)
    {
      return new List<float>();
    }

    startNorm = SanitizeNorm(startNorm);
    widthNorm = SanitizeWidth(widthNorm);

    var startIdx = (int)Math.Floor(startNorm * total);
    startIdx = Math.Clamp(startIdx, 0, Math.Max(0, total - 1));
    var endExclusive = (int)Math.Ceiling((startNorm + widthNorm) * total);
    endExclusive = Math.Clamp(endExclusive, startIdx + 1, total);

    var list = new List<float>(endExclusive - startIdx);
    for (var i = startIdx; i < endExclusive; i++)
    {
      list.Add(samples[i]);
    }

    return list;
  }

  /// <summary>
  /// Playback position mapped into the current viewport: <c>0..1</c> inside the window, or <c>-1</c> if hidden (no duration or playhead outside window).
  /// </summary>
  public static double ComputePlaybackNormalizedInViewport(
      double positionSeconds,
      double referenceDurationSeconds,
      double viewportStartNorm,
      double viewportWidthNorm)
  {
    if (referenceDurationSeconds <= 0 ||
        double.IsNaN(referenceDurationSeconds) ||
        double.IsInfinity(referenceDurationSeconds))
    {
      return -1;
    }

    viewportWidthNorm = SanitizeWidth(viewportWidthNorm);
    viewportStartNorm = SanitizeNorm(viewportStartNorm);

    var posNorm = Math.Clamp(positionSeconds / referenceDurationSeconds, 0, 1);
    var local = (posNorm - viewportStartNorm) / viewportWidthNorm;
    if (local < 0 || local > 1)
    {
      return -1;
    }

    return local;
  }

  /// <summary>
  /// True when the viewport covers (within epsilon) the full normalized timeline.
  /// </summary>
  public static bool IsFullViewport(double startNorm, double widthNorm) =>
      widthNorm >= 1.0 - FullViewportEpsilon && startNorm <= FullViewportEpsilon;

  private static double SanitizeNorm(double value)
  {
    if (double.IsNaN(value) || double.IsInfinity(value))
    {
      return 0;
    }

    return Math.Clamp(value, 0, 1);
  }

  private static double SanitizeWidth(double widthNorm)
  {
    if (double.IsNaN(widthNorm) || double.IsInfinity(widthNorm) || widthNorm <= 0)
    {
      return 1.0;
    }

    return Math.Clamp(widthNorm, FullViewportEpsilon, 1.0);
  }
}
