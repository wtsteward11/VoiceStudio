#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics.DirectX;

namespace VoiceStudio.App.Controls
{
  /// <summary>
  /// Spectrogram visualization: Win2D <see cref="CanvasControl"/> when available, with <see cref="WriteableBitmap"/> CPU fallback (GAP-038 slice 3).
  /// Heatmap pixels are produced by <see cref="SpectrogramHeatmapRasterizer"/> (shared contract for tests and both render paths).
  /// </summary>
  public sealed partial class SpectrogramControl : UserControl
  {
    private CanvasControl? _specCanvas;
    private bool _win2dFailed;
    private bool _win2dDrawFallbackRunning;

    private byte[]? _heatmapBgra;
    private int _heatmapWidth;
    private int _heatmapHeight;
    private WriteableBitmap? _bitmap;
    private double _durationSeconds;

    public SpectrogramControl()
    {
      InitializeComponent();
      SizeChanged += SpectrogramControl_SizeChanged;
      Loaded += SpectrogramControl_Loaded;
    }

    private void SpectrogramControl_Loaded(object sender, RoutedEventArgs e)
    {
      _specCanvas ??= SpectrogramContainer.FindName(nameof(SpecCanvas)) as CanvasControl;
      if (_specCanvas != null)
      {
        _specCanvas.Draw += SpecCanvas_Draw;
        _specCanvas.SizeChanged += SpecCanvas_SizeChanged;
      }

      UpdateSpectrogram();
      UpdatePlaybackPosition();
    }

    private void SpecCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
      _specCanvas?.Invalidate();
    }

    private void SpecCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
      if (_win2dFailed || _heatmapBgra == null || _heatmapWidth <= 0 || _heatmapHeight <= 0)
      {
        return;
      }

      try
      {
        var w = (float)sender.ActualWidth;
        var h = (float)sender.ActualHeight;
        if (w <= 0 || h <= 0)
        {
          return;
        }

        var session = args.DrawingSession;
        session.Clear(Windows.UI.Color.FromArgb(255, 0, 0, 0));

        using var bitmap = CanvasBitmap.CreateFromBytes(
            sender,
            _heatmapBgra,
            _heatmapWidth,
            _heatmapHeight,
            DirectXPixelFormat.B8G8R8A8UIntNormalized);

        session.DrawImage(bitmap, new Rect(0, 0, w, h));
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"[SpectrogramControl] Win2D draw failed; using CPU path. {ex.GetType().Name}: {ex.Message}");
        _win2dFailed = true;
        if (_win2dDrawFallbackRunning)
        {
          return;
        }

        _win2dDrawFallbackRunning = true;
        try
        {
          ApplyCpuHeatmapFromBuffer();
        }
        finally
        {
          _win2dDrawFallbackRunning = false;
        }
      }
    }

    public static readonly DependencyProperty FramesProperty =
        DependencyProperty.Register(
            nameof(Frames),
            typeof(IEnumerable),
            typeof(SpectrogramControl),
            new PropertyMetadata(null, OnFramesChanged));

    public static readonly DependencyProperty ZoomLevelProperty =
        DependencyProperty.Register(
            nameof(ZoomLevel),
            typeof(double),
            typeof(SpectrogramControl),
            new PropertyMetadata(1.0, OnZoomChanged));

    public static readonly DependencyProperty PlaybackPositionProperty =
        DependencyProperty.Register(
            nameof(PlaybackPosition),
            typeof(double),
            typeof(SpectrogramControl),
            new PropertyMetadata(-1.0, OnPlaybackPositionChanged));

    public IEnumerable? Frames
    {
      get => (IEnumerable?)GetValue(FramesProperty);
      set => SetValue(FramesProperty, value);
    }

    public double ZoomLevel
    {
      get => (double)GetValue(ZoomLevelProperty);
      set => SetValue(ZoomLevelProperty, value);
    }

    public double PlaybackPosition
    {
      get => (double)GetValue(PlaybackPositionProperty);
      set => SetValue(PlaybackPositionProperty, value);
    }

    private static void OnFramesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
      if (d is SpectrogramControl control)
      {
        control.UpdateSpectrogram();
      }
    }

    private static void OnZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
      if (d is SpectrogramControl control)
      {
        control.UpdateSpectrogram();
      }
    }

    private static void OnPlaybackPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
      if (d is SpectrogramControl control)
      {
        control.UpdatePlaybackPosition();
      }
    }

    private void SpectrogramControl_SizeChanged(object _, SizeChangedEventArgs e)
    {
      UpdateSpectrogram();
      UpdatePlaybackPosition();
    }

    private void UpdateSpectrogram()
    {
      var frames = ExtractFrames(Frames);
      if (frames.Count == 0)
      {
        ClearVisualization();
        return;
      }

      var rasterFrames = frames
          .Select(f => new SpectrogramHeatmapRasterizer.SpectrogramRasterFrame(f.TimeSeconds, f.Magnitudes))
          .ToList();

      if (!SpectrogramHeatmapRasterizer.TryRasterize(
              rasterFrames,
              ZoomLevel,
              SpectrogramHeatmapRasterizer.DefaultMaxRenderWidth,
              SpectrogramHeatmapRasterizer.DefaultMaxRenderHeight,
              SpectrogramHeatmapRasterizer.DefaultMinRenderWidth,
              SpectrogramHeatmapRasterizer.DefaultMinRenderHeight,
              out var targetWidth,
              out var targetHeight,
              out var pixels,
              out var durationSeconds))
      {
        ClearVisualization();
        return;
      }

      EmptyStateText.Visibility = Visibility.Collapsed;
      _heatmapBgra = pixels;
      _heatmapWidth = targetWidth;
      _heatmapHeight = targetHeight;
      _durationSeconds = durationSeconds;

      var useWin2d = !_win2dFailed && _specCanvas != null;
      if (useWin2d)
      {
        SpectrogramImage.Source = null;
        SpectrogramImage.Visibility = Visibility.Collapsed;
        _specCanvas!.Visibility = Visibility.Visible;
        _specCanvas.Invalidate();
      }
      else
      {
        if (_specCanvas != null)
        {
          _specCanvas.Visibility = Visibility.Collapsed;
          _specCanvas.Invalidate();
        }

        SpectrogramImage.Visibility = Visibility.Visible;
        ApplyCpuHeatmapFromBuffer();
      }

      UpdatePlaybackPosition();
    }

    private void ClearVisualization()
    {
      _heatmapBgra = null;
      _heatmapWidth = 0;
      _heatmapHeight = 0;
      SpectrogramImage.Source = null;
      EmptyStateText.Visibility = Visibility.Visible;
      if (PlaybackLine != null)
      {
        PlaybackLine.Visibility = Visibility.Collapsed;
      }

      if (_specCanvas != null)
      {
        _specCanvas.Visibility = Visibility.Collapsed;
        _specCanvas.Invalidate();
      }

      SpectrogramImage.Visibility = Visibility.Visible;
    }

    private void ApplyCpuHeatmapFromBuffer()
    {
      if (_heatmapBgra == null || _heatmapWidth <= 0 || _heatmapHeight <= 0)
      {
        SpectrogramImage.Source = null;
        return;
      }

      _bitmap = new WriteableBitmap(_heatmapWidth, _heatmapHeight);
      using (var stream = _bitmap.PixelBuffer.AsStream())
      {
        stream.Write(_heatmapBgra, 0, _heatmapBgra.Length);
      }

      SpectrogramImage.Source = _bitmap;
      SpectrogramImage.Visibility = Visibility.Visible;
    }

    private void UpdatePlaybackPosition()
    {
      if (PlaybackLine == null || PlaybackPosition < 0)
      {
        if (PlaybackLine != null)
        {
          PlaybackLine.Visibility = Visibility.Collapsed;
        }

        return;
      }

      var hasImage = SpectrogramImage.Source != null;
      var useCanvas = _specCanvas != null && _specCanvas.Visibility == Visibility.Visible;
      if (!hasImage && !useCanvas)
      {
        PlaybackLine.Visibility = Visibility.Collapsed;
        return;
      }

      var width = ActualWidth > 0
        ? ActualWidth
        : (hasImage ? _bitmap?.PixelWidth ?? 0 : _heatmapWidth);
      if (width <= 0)
      {
        PlaybackLine.Visibility = Visibility.Collapsed;
        return;
      }

      var position = PlaybackPosition;
      if (_durationSeconds > 0 && position > 1.0)
      {
        position /= _durationSeconds;
      }

      position = Math.Clamp(position, 0.0, 1.0);
      var x = position * width;

      PlaybackLine.X1 = x;
      PlaybackLine.Y1 = 0;
      PlaybackLine.X2 = x;
      PlaybackLine.Y2 = ActualHeight > 0 ? ActualHeight : _bitmap?.PixelHeight ?? _heatmapHeight;
      PlaybackLine.Visibility = Visibility.Visible;
    }

    private static List<FrameData> ExtractFrames(IEnumerable? frameSource)
    {
      var result = new List<FrameData>();
      if (frameSource == null)
      {
        return result;
      }

      foreach (var frame in frameSource)
      {
        if (frame == null)
        {
          continue;
        }

        var magnitudes = ExtractMagnitudes(frame);
        if (magnitudes.Count == 0)
        {
          continue;
        }

        var timeSeconds = ExtractTimeSeconds(frame);
        var maxValue = magnitudes.Max();
        result.Add(new FrameData(timeSeconds, magnitudes, maxValue));
      }

      return result;
    }

    private static List<float> ExtractMagnitudes(object frame)
    {
      var magnitudes = ExtractList(frame, "Magnitudes");
      if (magnitudes.Count == 0)
      {
        magnitudes = ExtractList(frame, "Frequencies");
      }

      return magnitudes;
    }

    private static List<float> ExtractList(object frame, string propertyName)
    {
      var property = frame.GetType().GetProperty(propertyName);
      if (property == null)
      {
        return new List<float>();
      }

      var value = property.GetValue(frame);
      if (value == null)
      {
        return new List<float>();
      }

      if (value is IList<float> floatList)
      {
        return floatList.ToList();
      }

      if (value is IList<double> doubleList)
      {
        return doubleList.Select(v => (float)v).ToList();
      }

      if (value is IEnumerable<float> floatEnumerable)
      {
        return floatEnumerable.ToList();
      }

      if (value is IEnumerable<double> doubleEnumerable)
      {
        return doubleEnumerable.Select(v => (float)v).ToList();
      }

      if (value is IEnumerable enumerable)
      {
        var list = new List<float>();
        foreach (var item in enumerable)
        {
          if (item is float f)
          {
            list.Add(f);
          }
          else if (item is double d)
          {
            list.Add((float)d);
          }
          else if (item != null && float.TryParse(item.ToString(), out var parsed))
          {
            list.Add(parsed);
          }
        }

        return list;
      }

      return new List<float>();
    }

    private static double ExtractTimeSeconds(object frame)
    {
      var timeProp = frame.GetType().GetProperty("Time");
      if (timeProp != null)
      {
        var value = timeProp.GetValue(frame);
        if (value is double doubleValue)
        {
          return doubleValue;
        }

        if (value is float floatValue)
        {
          return floatValue;
        }

        if (value != null && double.TryParse(value.ToString(), out var parsed))
        {
          return parsed;
        }
      }

      var timeSecondsProp = frame.GetType().GetProperty("TimeSeconds");
      if (timeSecondsProp != null)
      {
        var value = timeSecondsProp.GetValue(frame);
        if (value is double d)
        {
          return d;
        }

        if (value is float f)
        {
          return f;
        }

        if (value != null && double.TryParse(value.ToString(), out var p))
        {
          return p;
        }
      }

      return 0;
    }
  }

  /// <summary>
  /// Represents a single frame of spectrogram data.
  /// </summary>
  public class SpectrogramFrame
  {
    public double Time { get; set; }
    public List<float> Frequencies { get; set; } = new();
  }

  internal sealed record FrameData(double TimeSeconds, List<float> Magnitudes, float MaxValue);
}
