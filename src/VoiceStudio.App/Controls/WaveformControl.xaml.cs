#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace VoiceStudio.App.Controls
{
  /// <summary>
  /// Waveform visualization: Win2D <see cref="CanvasControl"/> when available, with Path-based CPU fallback (GAP-038 slice 1).
  /// </summary>
  public sealed partial class WaveformControl : UserControl
  {
    private Path? _waveformPath;
    private Line? _playbackLine;
    private CanvasControl? _waveCanvas;
    private TextBlock? _emptyStateText;

    private bool _win2dFailed;
    private bool _win2dDrawFallbackRunning;

    private IReadOnlyList<float>? _renderSamples;
    private double _renderSampleSpacing;
    private double _renderCenterY;
    private double _renderActualHeight;
    private bool _renderIsPeakMode;
    private Windows.UI.Color _renderStrokeColor;
    private Windows.UI.Color _renderFillColor;

    public static readonly DependencyProperty SamplesProperty =
      DependencyProperty.Register(
        nameof(Samples),
        typeof(object),
        typeof(WaveformControl),
        new PropertyMetadata(null, OnSamplesChanged));

    public static readonly DependencyProperty ModeProperty =
      DependencyProperty.Register(
        nameof(Mode),
        typeof(string),
        typeof(WaveformControl),
        new PropertyMetadata("peak", OnVisualPropertyChanged));

    public static readonly DependencyProperty WaveformColorProperty =
      DependencyProperty.Register(
        nameof(WaveformColor),
        typeof(string),
        typeof(WaveformControl),
        new PropertyMetadata("Cyan", OnVisualPropertyChanged));

    public static readonly DependencyProperty ZoomLevelProperty =
      DependencyProperty.Register(
        nameof(ZoomLevel),
        typeof(double),
        typeof(WaveformControl),
        new PropertyMetadata(1.0, OnVisualPropertyChanged));

    public static readonly DependencyProperty PlaybackPositionProperty =
      DependencyProperty.Register(
        nameof(PlaybackPosition),
        typeof(double),
        typeof(WaveformControl),
        new PropertyMetadata(-1.0, OnPlaybackPositionChanged));

    public WaveformControl()
    {
      InitializeComponent();
      SizeChanged += WaveformControl_SizeChanged;
      Loaded += WaveformControl_Loaded;
    }

    private void WaveformControl_Loaded(object sender, RoutedEventArgs e)
    {
      if (Content is not Grid grid)
      {
        return;
      }

      _waveformPath ??= grid.FindName("WaveformPath") as Path;
      _playbackLine ??= grid.FindName("PlaybackLine") as Line;
      _waveCanvas ??= grid.FindName("WaveCanvas") as CanvasControl;
      _emptyStateText ??= grid.FindName("EmptyStateText") as TextBlock;

      if (_waveCanvas != null)
      {
        _waveCanvas.Draw += WaveCanvas_Draw;
        _waveCanvas.SizeChanged += WaveCanvas_SizeChanged;
      }

      UpdateWaveform();
      UpdatePlaybackPosition();
    }

    private void WaveCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
      _waveCanvas?.Invalidate();
    }

    private void WaveCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
      if (_win2dFailed || _renderSamples == null || _renderSamples.Count == 0)
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
        session.Clear(Windows.UI.Color.FromArgb(0, 0, 0, 0));

        var centerY = (float)_renderCenterY;
        var spacing = (float)_renderSampleSpacing;
        var amp = (float)(_renderActualHeight * 0.4);
        var samples = _renderSamples;
        var stroke = _renderStrokeColor;

        if (_renderIsPeakMode)
        {
          using var builder = new CanvasPathBuilder(session);
          builder.SetFilledRegionDetermination(CanvasFilledRegionDetermination.Winding);
          builder.BeginFigure(0, centerY);
          for (var i = 0; i < samples.Count; i++)
          {
            var x = i * spacing;
            var normalized = Math.Clamp(samples[i], -1f, 1f);
            var y = centerY - normalized * amp;
            builder.AddLine(x, y);
          }

          for (var i = samples.Count - 1; i >= 0; i--)
          {
            var x = i * spacing;
            var normalized = Math.Clamp(samples[i], -1f, 1f);
            var sh = normalized * amp;
            var y = centerY + Math.Abs(sh);
            builder.AddLine(x, y);
          }

          builder.EndFigure(CanvasFigureLoop.Closed);
          using (var geom = CanvasGeometry.CreatePath(builder))
          {
            session.FillGeometry(geom, _renderFillColor);
            session.DrawGeometry(geom, stroke, 1.5f);
          }
        }
        else
        {
          var px = 0f;
          var py = centerY;
          for (var i = 0; i < samples.Count; i++)
          {
            var x = i * spacing;
            var normalized = Math.Clamp(samples[i], -1f, 1f);
            var y = (float)(centerY - normalized * amp);
            session.DrawLine(px, py, x, y, stroke, 1.5f);
            px = x;
            py = y;
          }
        }
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"[WaveformControl] Win2D draw failed; using CPU path. {ex.GetType().Name}: {ex.Message}");
        _win2dFailed = true;
        if (_win2dDrawFallbackRunning)
        {
          return;
        }

        _win2dDrawFallbackRunning = true;
        try
        {
          if (_waveCanvas != null)
          {
            _waveCanvas.Visibility = Visibility.Collapsed;
          }

          if (_waveformPath != null)
          {
            _waveformPath.Visibility = Visibility.Visible;
          }

          ApplyCpuWaveformPathGeometry();
        }
        finally
        {
          _win2dDrawFallbackRunning = false;
        }
      }
    }

    private static void OnSamplesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
      if (d is WaveformControl control)
      {
        control.UpdateWaveform();
      }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
      if (d is WaveformControl control)
      {
        control.UpdateWaveform();
      }
    }

    private static void OnPlaybackPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
      if (d is WaveformControl control)
      {
        control.UpdatePlaybackPosition();
      }
    }

    private void WaveformControl_SizeChanged(object _, SizeChangedEventArgs e)
    {
      UpdateWaveform();
      UpdatePlaybackPosition();
    }

    public object? Samples
    {
      get => GetValue(SamplesProperty);
      set => SetValue(SamplesProperty, value);
    }

    public string Mode
    {
      get => (string)GetValue(ModeProperty);
      set => SetValue(ModeProperty, value);
    }

    public string WaveformColor
    {
      get => (string)GetValue(WaveformColorProperty);
      set => SetValue(WaveformColorProperty, value);
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

    private List<float>? GetSamplesList()
    {
      var samples = Samples;
      if (samples == null)
      {
        return null;
      }

      if (samples is System.Collections.ObjectModel.ObservableCollection<float> observableCollection)
      {
        return observableCollection.ToList();
      }

      if (samples is IList<float> floatList)
      {
        return floatList.ToList();
      }

      if (samples is IEnumerable<float> floatEnumerable)
      {
        return floatEnumerable.ToList();
      }

      if (samples is float[] floatArray)
      {
        return floatArray.ToList();
      }

      if (samples is IEnumerable enumerable)
      {
        var result = new List<float>();
        foreach (var item in enumerable)
        {
          if (item is float f)
          {
            result.Add(f);
          }
          else if (item != null && float.TryParse(item.ToString(), out var parsed))
          {
            result.Add(parsed);
          }
        }

        return result.Count > 0 ? result : null;
      }

      return null;
    }

    private void UpdateWaveform()
    {
      if (_waveformPath == null)
      {
        return;
      }

      var samples = GetSamplesList();
      if (samples == null || samples.Count == 0)
      {
        _renderSamples = null;
        _waveformPath.Data = null;
        if (_emptyStateText != null)
        {
          _emptyStateText.Visibility = Visibility.Visible;
        }

        if (_waveCanvas != null)
        {
          _waveCanvas.Visibility = Visibility.Collapsed;
          _waveCanvas.Invalidate();
        }

        _waveformPath.Visibility = Visibility.Visible;
        return;
      }

      if (_emptyStateText != null)
      {
        _emptyStateText.Visibility = Visibility.Collapsed;
      }

      var actualWidth = ActualWidth > 0 ? ActualWidth : 800;
      var actualHeight = ActualHeight > 0 ? ActualHeight : 200;
      var centerY = actualHeight / 2.0;
      var zoomedWidth = actualWidth * ZoomLevel;
      var samplesToDisplay = samples.Count;
      if (ZoomLevel > 1.0)
      {
        samplesToDisplay = Math.Max(1, (int)(samples.Count / ZoomLevel));
      }

      var displaySamples = WaveformDownsampler.Downsample(samples, samplesToDisplay, Mode);
      if (displaySamples.Count == 0)
      {
        return;
      }

      var sampleSpacing = zoomedWidth / displaySamples.Count;
      var isPeakMode = Mode.Equals("peak", StringComparison.OrdinalIgnoreCase);
      var strokeColor = TryParseWaveformColor(WaveformColor, out var sc)
        ? sc
        : Windows.UI.Color.FromArgb(255, 0, 255, 255);
      var fillAlpha = (byte)(isPeakMode ? 77 : 0);
      var fillColor = Windows.UI.Color.FromArgb(fillAlpha, strokeColor.R, strokeColor.G, strokeColor.B);

      _renderSamples = displaySamples;
      _renderSampleSpacing = sampleSpacing;
      _renderCenterY = centerY;
      _renderActualHeight = actualHeight;
      _renderIsPeakMode = isPeakMode;
      _renderStrokeColor = strokeColor;
      _renderFillColor = fillColor;

      var useWin2d = !_win2dFailed && _waveCanvas != null;
      if (useWin2d)
      {
        _waveformPath.Visibility = Visibility.Collapsed;
        _waveformPath.Data = null;
        _waveCanvas!.Visibility = Visibility.Visible;
        _waveCanvas.Invalidate();
      }
      else
      {
        if (_waveCanvas != null)
        {
          _waveCanvas.Visibility = Visibility.Collapsed;
        }

        _waveformPath.Visibility = Visibility.Visible;
        ApplyCpuWaveformPathGeometry();
      }
    }

    private void ApplyCpuWaveformPathGeometry()
    {
      if (_waveformPath == null || _renderSamples == null || _renderSamples.Count == 0)
      {
        return;
      }

      var displaySamples = _renderSamples;
      var sampleSpacing = _renderSampleSpacing;
      var centerY = _renderCenterY;
      var actualHeight = _renderActualHeight;
      var isPeakMode = _renderIsPeakMode;

      var pathGeometry = new PathGeometry();
      var pathFigure = new PathFigure { StartPoint = new Windows.Foundation.Point(0, centerY) };

      for (var i = 0; i < displaySamples.Count; i++)
      {
        var sample = displaySamples[i];
        var x = i * sampleSpacing;
        var normalizedSample = Math.Clamp(sample, -1.0f, 1.0f);
        var sampleHeight = normalizedSample * (actualHeight * 0.4);
        var y = centerY - sampleHeight;
        pathFigure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(x, y) });
      }

      if (isPeakMode)
      {
        for (var i = displaySamples.Count - 1; i >= 0; i--)
        {
          var sample = displaySamples[i];
          var x = i * sampleSpacing;
          var normalizedSample = Math.Clamp(sample, -1.0f, 1.0f);
          var sampleHeight = normalizedSample * (actualHeight * 0.4);
          var y = centerY + Math.Abs(sampleHeight);
          pathFigure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(x, y) });
        }

        pathFigure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(0, centerY) });
      }

      pathGeometry.Figures.Add(pathFigure);
      _waveformPath.Data = pathGeometry;
      _waveformPath.Stroke = new SolidColorBrush(_renderStrokeColor);
      _waveformPath.Fill = isPeakMode
        ? new SolidColorBrush(_renderFillColor)
        : null;
    }

    private void UpdatePlaybackPosition()
    {
      if (_playbackLine == null || PlaybackPosition < 0)
      {
        if (_playbackLine != null)
        {
          _playbackLine.Visibility = Visibility.Collapsed;
        }

        return;
      }

      var actualWidth = ActualWidth > 0 ? ActualWidth : 800;
      var actualHeight = ActualHeight > 0 ? ActualHeight : 200;
      var position = Math.Clamp(PlaybackPosition, 0.0, 1.0);
      var x = position * actualWidth;

      _playbackLine.X1 = x;
      _playbackLine.Y1 = 0;
      _playbackLine.X2 = x;
      _playbackLine.Y2 = actualHeight;
      _playbackLine.Visibility = Visibility.Visible;
    }

    private static bool TryParseWaveformColor(string colorName, out Windows.UI.Color color)
    {
      color = Windows.UI.Color.FromArgb(255, 0, 255, 255);
      if (string.IsNullOrWhiteSpace(colorName))
      {
        return true;
      }

      if (colorName.StartsWith('#'))
      {
        try
        {
          var hex = colorName.Substring(1);
          var r = Convert.ToByte(hex.Length >= 2 ? hex.Substring(0, 2) : "00", 16);
          var g = Convert.ToByte(hex.Length >= 4 ? hex.Substring(2, 2) : "00", 16);
          var b = Convert.ToByte(hex.Length >= 6 ? hex.Substring(4, 2) : "00", 16);
          var a = hex.Length == 8 ? Convert.ToByte(hex.Substring(6, 2), 16) : (byte)255;
          color = Windows.UI.Color.FromArgb(a, r, g, b);
          return true;
        }
        catch (FormatException)
        {
          return false;
        }
        catch (ArgumentOutOfRangeException)
        {
          return false;
        }
      }

      var colorProperty = typeof(Microsoft.UI.Colors).GetProperty(
        colorName,
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.IgnoreCase);
      if (colorProperty != null && colorProperty.GetValue(null) is Windows.UI.Color named)
      {
        color = named;
        return true;
      }

      return false;
    }
  }
}
