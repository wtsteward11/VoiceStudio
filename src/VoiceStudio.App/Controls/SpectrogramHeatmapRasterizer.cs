#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace VoiceStudio.App.Controls
{
  /// <summary>
  /// Pure, testable BGRA8 heatmap rasterization for spectrogram frames (GAP-038 slice 3 — shared by CPU and GPU paths).
  /// </summary>
  public static class SpectrogramHeatmapRasterizer
  {
    public const int DefaultMaxRenderWidth = 1024;
    public const int DefaultMaxRenderHeight = 512;
    public const int DefaultMinRenderWidth = 64;
    public const int DefaultMinRenderHeight = 64;

    /// <summary>
    /// One time-frequency column: <see cref="TimeSeconds"/> and per-bin magnitudes (same contract as <see cref="SpectrogramFrame"/>).
    /// </summary>
    public sealed record SpectrogramRasterFrame(double TimeSeconds, IReadOnlyList<float> Magnitudes);

    /// <summary>
    /// Builds a BGRA8 bitmap matching the prior <see cref="SpectrogramControl"/> CPU path semantics.
    /// </summary>
    /// <returns>False when there is no drawable data.</returns>
    public static bool TryRasterize(
      IReadOnlyList<SpectrogramRasterFrame> frames,
      double zoomLevel,
      int maxRenderWidth,
      int maxRenderHeight,
      int minRenderWidth,
      int minRenderHeight,
      out int width,
      out int height,
      out byte[] bgraPixels,
      out double durationSeconds)
    {
      width = 0;
      height = 0;
      bgraPixels = Array.Empty<byte>();
      durationSeconds = 0;

      if (frames == null || frames.Count == 0)
      {
        return false;
      }

      var frameCount = frames.Count;
      var binCount = frames.Max(f => f.Magnitudes?.Count ?? 0);
      if (frameCount == 0 || binCount == 0)
      {
        return false;
      }

      var targetWidth = (int)Math.Clamp(frameCount, minRenderWidth, maxRenderWidth);
      var targetHeight = (int)Math.Clamp(binCount, minRenderHeight, maxRenderHeight);

      var zoom = double.IsFinite(zoomLevel) && zoomLevel > 0 ? zoomLevel : 1.0;
      zoom = Math.Max(0.1, zoom);
      var framesToShow = Math.Max(1, (int)(frameCount / zoom));
      const int frameStart = 0;
      var frameEnd = Math.Min(frameCount, frameStart + framesToShow);

      var maxValue = frames.Max(f => f.Magnitudes != null && f.Magnitudes.Count > 0 ? f.Magnitudes.Max() : 0f);
      if (maxValue <= 0)
      {
        maxValue = 1f;
      }

      durationSeconds = frames.Max(f => f.TimeSeconds);

      var pixels = new byte[targetWidth * targetHeight * 4];
      var frameStep = (double)(frameEnd - frameStart) / targetWidth;
      var binStep = (double)binCount / targetHeight;

      for (var y = 0; y < targetHeight; y++)
      {
        var binIndex = binCount - 1 - Math.Min(binCount - 1, (int)(y * binStep));
        var rowOffset = y * targetWidth * 4;
        for (var x = 0; x < targetWidth; x++)
        {
          var frameIndex = frameStart + Math.Min(frameEnd - frameStart - 1, (int)(x * frameStep));
          var mags = frames[frameIndex].Magnitudes;
          var value = mags != null && binIndex < mags.Count ? mags[binIndex] : 0f;
          var normalized = Math.Clamp(value / maxValue, 0f, 1f);
          var color = GetHeatmapColor(normalized);
          var pixelOffset = rowOffset + (x * 4);
          pixels[pixelOffset] = color.B;
          pixels[pixelOffset + 1] = color.G;
          pixels[pixelOffset + 2] = color.R;
          pixels[pixelOffset + 3] = color.A;
        }
      }

      width = targetWidth;
      height = targetHeight;
      bgraPixels = pixels;
      return true;
    }

    public static Windows.UI.Color GetHeatmapColor(float value)
    {
      value = Math.Clamp(value, 0f, 1f);
      byte r;
      byte g;
      byte b;

      if (value <= 0.25f)
      {
        var t = value / 0.25f;
        r = 0;
        g = (byte)(t * 64);
        b = (byte)(128 + (t * 127));
      }
      else if (value <= 0.5f)
      {
        var t = (value - 0.25f) / 0.25f;
        r = 0;
        g = (byte)(64 + (t * 191));
        b = (byte)(255 - (t * 127));
      }
      else if (value <= 0.75f)
      {
        var t = (value - 0.5f) / 0.25f;
        r = (byte)(t * 255);
        g = (byte)(255 - (t * 64));
        b = (byte)(128 - (t * 128));
      }
      else
      {
        var t = (value - 0.75f) / 0.25f;
        r = 255;
        g = (byte)(191 - (t * 191));
        b = 0;
      }

      return Windows.UI.Color.FromArgb(255, r, g, b);
    }
  }
}
