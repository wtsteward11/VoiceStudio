// Phase 5: Waveform Visualization
// Task 5.12: Professional waveform display

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.UI;

namespace VoiceStudio.App.Features.Waveform;

/// <summary>
/// Waveform display style.
/// </summary>
public enum WaveformStyle
{
    Bars,
    Lines,
    Mirror,
    Filled,
    Gradient,
}

/// <summary>
/// Waveform data for rendering.
/// </summary>
public class WaveformData
{
    public float[] Peaks { get; set; } = Array.Empty<float>();
    public float[] RmsPeaks { get; set; } = Array.Empty<float>();
    public int SampleRate { get; set; }
    public double Duration { get; set; }
    public int Channels { get; set; }
}

/// <summary>
/// Waveform render options.
/// </summary>
public class WaveformRenderOptions
{
    public WaveformStyle Style { get; set; } = WaveformStyle.Filled;
    public Color WaveformColor { get; set; } = Color.FromArgb(255, 0, 120, 212);
    public Color RmsColor { get; set; } = Color.FromArgb(255, 0, 90, 158);
    public Color BackgroundColor { get; set; } = Microsoft.UI.Colors.Transparent;
    public Color PlayheadColor { get; set; } = Microsoft.UI.Colors.Red;
    public Color SelectionColor { get; set; } = Color.FromArgb(80, 0, 120, 212);
    public double BarWidth { get; set; } = 2;
    public double BarGap { get; set; } = 1;
    public bool ShowRms { get; set; } = true;
    public bool ShowPlayhead { get; set; } = true;
    public bool Antialiased { get; set; } = true;
}

/// <summary>
/// Service for generating waveform data.
/// </summary>
public class WaveformGenerator
{
    /// <summary>
    /// Generate waveform data from audio samples.
    /// </summary>
    public Task<WaveformData> GenerateAsync(
        float[] samples,
        int sampleRate,
        int channels,
        int targetPeakCount = 1000)
    {
        return Task.Run(() =>
        {
            var samplesPerPeak = Math.Max(1, samples.Length / targetPeakCount);
            var peakCount = (samples.Length + samplesPerPeak - 1) / samplesPerPeak;
            
            var peaks = new float[peakCount];
            var rmsPeaks = new float[peakCount];
            
            for (int i = 0; i < peakCount; i++)
            {
                var start = i * samplesPerPeak;
                var end = Math.Min(start + samplesPerPeak, samples.Length);
                
                float maxPeak = 0;
                float sumSquares = 0;
                
                for (int j = start; j < end; j++)
                {
                    var abs = Math.Abs(samples[j]);
                    if (abs > maxPeak)
                    {
                        maxPeak = abs;
                    }
                    sumSquares += samples[j] * samples[j];
                }
                
                peaks[i] = maxPeak;
                rmsPeaks[i] = (float)Math.Sqrt(sumSquares / (end - start));
            }
            
            return new WaveformData
            {
                Peaks = peaks,
                RmsPeaks = rmsPeaks,
                SampleRate = sampleRate,
                Duration = (double)samples.Length / sampleRate / channels,
                Channels = channels,
            };
        });
    }

    /// <summary>
    /// Generate waveform data from an audio file by reading its PCM samples.
    /// Supports WAV files natively; other formats fall back to raw byte interpretation.
    /// </summary>
    public async Task<WaveformData> GenerateFromFileAsync(
        string filePath,
        int targetPeakCount = 1000)
    {
        return await Task.Run(() =>
        {
            if (!System.IO.File.Exists(filePath))
            {
                throw new System.IO.FileNotFoundException("Audio file not found", filePath);
            }

            using var stream = System.IO.File.OpenRead(filePath);
            using var reader = new System.IO.BinaryReader(stream);

            int sampleRate = 44100;
            int channels = 1;
            int bitsPerSample = 16;
            float[] samples;

            var header = new byte[4];
            if (stream.Length >= 44 && stream.Read(header, 0, 4) == 4
                && header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F')
            {
                stream.Position = 0;
                reader.ReadBytes(4); // "RIFF"
                reader.ReadInt32();  // file size
                reader.ReadBytes(4); // "WAVE"

                while (stream.Position < stream.Length - 8)
                {
                    var chunkId = new string(reader.ReadChars(4));
                    int chunkSize = reader.ReadInt32();

                    if (chunkId == "fmt ")
                    {
                        reader.ReadInt16(); // audio format
                        channels = reader.ReadInt16();
                        sampleRate = reader.ReadInt32();
                        reader.ReadInt32(); // byte rate
                        reader.ReadInt16(); // block align
                        bitsPerSample = reader.ReadInt16();
                        if (chunkSize > 16)
                            stream.Position += chunkSize - 16;
                    }
                    else if (chunkId == "data")
                    {
                        int bytesPerSample = bitsPerSample / 8;
                        int totalSamples = chunkSize / bytesPerSample;
                        samples = new float[totalSamples];

                        for (int i = 0; i < totalSamples && stream.Position < stream.Length; i++)
                        {
                            samples[i] = bitsPerSample switch
                            {
                                8 => (reader.ReadByte() - 128) / 128f,
                                16 => reader.ReadInt16() / 32768f,
                                24 => (reader.ReadByte() | (reader.ReadByte() << 8) | ((sbyte)reader.ReadByte() << 16)) / 8388608f,
                                32 => reader.ReadInt32() / (float)int.MaxValue,
                                _ => reader.ReadInt16() / 32768f,
                            };
                        }

                        return GenerateAsync(samples, sampleRate, channels, targetPeakCount).GetAwaiter().GetResult();
                    }
                    else
                    {
                        stream.Position += chunkSize;
                    }
                }
            }

            stream.Position = 0;
            int rawSampleCount = (int)(stream.Length / 2);
            samples = new float[rawSampleCount];
            for (int i = 0; i < rawSampleCount && stream.Position < stream.Length - 1; i++)
            {
                samples[i] = reader.ReadInt16() / 32768f;
            }

            return GenerateAsync(samples, sampleRate, channels, targetPeakCount).GetAwaiter().GetResult();
        });
    }
}

/// <summary>
/// Renders waveform data to canvas.
/// </summary>
public class WaveformRenderer
{
    private readonly WaveformRenderOptions _options;

    public WaveformRenderer(WaveformRenderOptions? options = null)
    {
        _options = options ?? new WaveformRenderOptions();
    }

    public WaveformRenderOptions Options => _options;

    /// <summary>
    /// Render waveform to a canvas.
    /// </summary>
    public void Render(
        CanvasDrawingSession session,
        WaveformData data,
        Rect bounds,
        double startTime = 0,
        double endTime = -1,
        double currentTime = -1,
        double selectionStart = -1,
        double selectionEnd = -1)
    {
        if (data.Peaks.Length == 0)
        {
            return;
        }
        
        if (endTime < 0)
        {
            endTime = data.Duration;
        }
        
        var width = bounds.Width;
        var height = bounds.Height;
        var centerY = height / 2;
        
        // Draw background
        if (_options.BackgroundColor != Microsoft.UI.Colors.Transparent)
        {
            session.FillRectangle(bounds, _options.BackgroundColor);
        }
        
        // Calculate visible range
        var startIndex = (int)(startTime / data.Duration * data.Peaks.Length);
        var endIndex = (int)(endTime / data.Duration * data.Peaks.Length);
        var visiblePeaks = Math.Max(1, endIndex - startIndex);
        
        // Draw selection
        if (selectionStart >= 0 && selectionEnd > selectionStart)
        {
            var selStartX = TimeToX(selectionStart, startTime, endTime, width);
            var selEndX = TimeToX(selectionEnd, startTime, endTime, width);
            
            session.FillRectangle(
                (float)(bounds.X + selStartX),
                (float)bounds.Y,
                (float)(selEndX - selStartX),
                (float)height,
                _options.SelectionColor);
        }
        
        // Draw waveform
        switch (_options.Style)
        {
            case WaveformStyle.Bars:
                DrawBars(session, data, bounds, startIndex, visiblePeaks, centerY);
                break;
                
            case WaveformStyle.Lines:
                DrawLines(session, data, bounds, startIndex, visiblePeaks, centerY);
                break;
                
            case WaveformStyle.Mirror:
                DrawMirror(session, data, bounds, startIndex, visiblePeaks, centerY);
                break;
                
            case WaveformStyle.Filled:
            default:
                DrawFilled(session, data, bounds, startIndex, visiblePeaks, centerY);
                break;
        }
        
        // Draw playhead
        if (_options.ShowPlayhead && currentTime >= startTime && currentTime <= endTime)
        {
            var playheadX = TimeToX(currentTime, startTime, endTime, width);
            session.DrawLine(
                (float)(bounds.X + playheadX),
                (float)bounds.Y,
                (float)(bounds.X + playheadX),
                (float)(bounds.Y + height),
                _options.PlayheadColor,
                2);
        }
    }

    private void DrawBars(
        CanvasDrawingSession session,
        WaveformData data,
        Rect bounds,
        int startIndex,
        int visiblePeaks,
        double centerY)
    {
        var barWidth = _options.BarWidth;
        var gap = _options.BarGap;
        var totalWidth = barWidth + gap;
        var barCount = (int)(bounds.Width / totalWidth);
        var peaksPerBar = Math.Max(1, visiblePeaks / barCount);
        
        for (int i = 0; i < barCount; i++)
        {
            var peakIndex = startIndex + i * peaksPerBar;
            if (peakIndex >= data.Peaks.Length)
            {
                break;
            }
            
            var peak = data.Peaks[peakIndex];
            var barHeight = peak * bounds.Height;
            
            var x = (float)(bounds.X + i * totalWidth);
            var y = (float)(bounds.Y + centerY - barHeight / 2);
            
            session.FillRectangle(x, y, (float)barWidth, (float)barHeight, _options.WaveformColor);
            
            if (_options.ShowRms && peakIndex < data.RmsPeaks.Length)
            {
                var rms = data.RmsPeaks[peakIndex];
                var rmsHeight = rms * bounds.Height;
                var rmsY = (float)(bounds.Y + centerY - rmsHeight / 2);
                
                session.FillRectangle(x, rmsY, (float)barWidth, (float)rmsHeight, _options.RmsColor);
            }
        }
    }

    private void DrawLines(
        CanvasDrawingSession session,
        WaveformData data,
        Rect bounds,
        int startIndex,
        int visiblePeaks,
        double centerY)
    {
        var pointCount = Math.Min((int)bounds.Width, visiblePeaks);
        var peaksPerPoint = Math.Max(1, visiblePeaks / pointCount);
        
        using var pathBuilder = new CanvasPathBuilder(session);
        
        pathBuilder.BeginFigure(
            (float)bounds.X,
            (float)(bounds.Y + centerY));
        
        for (int i = 0; i < pointCount; i++)
        {
            var peakIndex = startIndex + i * peaksPerPoint;
            if (peakIndex >= data.Peaks.Length)
            {
                break;
            }
            
            var peak = data.Peaks[peakIndex];
            var x = (float)(bounds.X + i * bounds.Width / pointCount);
            var y = (float)(bounds.Y + centerY - peak * bounds.Height / 2);
            
            pathBuilder.AddLine(x, y);
        }
        
        pathBuilder.EndFigure(CanvasFigureLoop.Open);
        
        using var geometry = CanvasGeometry.CreatePath(pathBuilder);
        session.DrawGeometry(geometry, _options.WaveformColor, 1);
    }

    private void DrawMirror(
        CanvasDrawingSession session,
        WaveformData data,
        Rect bounds,
        int startIndex,
        int visiblePeaks,
        double centerY)
    {
        var pointCount = Math.Min((int)bounds.Width, visiblePeaks);
        var peaksPerPoint = Math.Max(1, visiblePeaks / pointCount);
        
        using var pathBuilder = new CanvasPathBuilder(session);
        
        // Top half
        pathBuilder.BeginFigure(
            (float)bounds.X,
            (float)(bounds.Y + centerY));
        
        for (int i = 0; i < pointCount; i++)
        {
            var peakIndex = startIndex + i * peaksPerPoint;
            if (peakIndex >= data.Peaks.Length)
            {
                break;
            }
            
            var peak = data.Peaks[peakIndex];
            var x = (float)(bounds.X + i * bounds.Width / pointCount);
            var y = (float)(bounds.Y + centerY - peak * bounds.Height / 2);
            
            pathBuilder.AddLine(x, y);
        }
        
        // Bottom half (mirror)
        for (int i = pointCount - 1; i >= 0; i--)
        {
            var peakIndex = startIndex + i * peaksPerPoint;
            if (peakIndex >= data.Peaks.Length)
            {
                continue;
            }
            
            var peak = data.Peaks[peakIndex];
            var x = (float)(bounds.X + i * bounds.Width / pointCount);
            var y = (float)(bounds.Y + centerY + peak * bounds.Height / 2);
            
            pathBuilder.AddLine(x, y);
        }
        
        pathBuilder.EndFigure(CanvasFigureLoop.Closed);
        
        using var geometry = CanvasGeometry.CreatePath(pathBuilder);
        session.FillGeometry(geometry, _options.WaveformColor);
    }

    private void DrawFilled(
        CanvasDrawingSession session,
        WaveformData data,
        Rect bounds,
        int startIndex,
        int visiblePeaks,
        double centerY)
    {
        var pointCount = Math.Min((int)bounds.Width, visiblePeaks);
        var peaksPerPoint = Math.Max(1, visiblePeaks / pointCount);
        
        // Draw main waveform
        using (var pathBuilder = new CanvasPathBuilder(session))
        {
            pathBuilder.BeginFigure(
                (float)bounds.X,
                (float)(bounds.Y + centerY));
            
            for (int i = 0; i < pointCount; i++)
            {
                var peakIndex = startIndex + i * peaksPerPoint;
                if (peakIndex >= data.Peaks.Length)
                {
                    break;
                }
                
                var peak = data.Peaks[peakIndex];
                var x = (float)(bounds.X + i * bounds.Width / pointCount);
                var y = (float)(bounds.Y + centerY - peak * bounds.Height / 2);
                
                pathBuilder.AddLine(x, y);
            }
            
            for (int i = pointCount - 1; i >= 0; i--)
            {
                var peakIndex = startIndex + i * peaksPerPoint;
                if (peakIndex >= data.Peaks.Length)
                {
                    continue;
                }
                
                var peak = data.Peaks[peakIndex];
                var x = (float)(bounds.X + i * bounds.Width / pointCount);
                var y = (float)(bounds.Y + centerY + peak * bounds.Height / 2);
                
                pathBuilder.AddLine(x, y);
            }
            
            pathBuilder.EndFigure(CanvasFigureLoop.Closed);
            
            using var geometry = CanvasGeometry.CreatePath(pathBuilder);
            session.FillGeometry(geometry, _options.WaveformColor);
        }
        
        // Draw RMS overlay
        if (_options.ShowRms)
        {
            using var pathBuilder = new CanvasPathBuilder(session);
            
            pathBuilder.BeginFigure(
                (float)bounds.X,
                (float)(bounds.Y + centerY));
            
            for (int i = 0; i < pointCount; i++)
            {
                var peakIndex = startIndex + i * peaksPerPoint;
                if (peakIndex >= data.RmsPeaks.Length)
                {
                    break;
                }
                
                var rms = data.RmsPeaks[peakIndex];
                var x = (float)(bounds.X + i * bounds.Width / pointCount);
                var y = (float)(bounds.Y + centerY - rms * bounds.Height / 2);
                
                pathBuilder.AddLine(x, y);
            }
            
            for (int i = pointCount - 1; i >= 0; i--)
            {
                var peakIndex = startIndex + i * peaksPerPoint;
                if (peakIndex >= data.RmsPeaks.Length)
                {
                    continue;
                }
                
                var rms = data.RmsPeaks[peakIndex];
                var x = (float)(bounds.X + i * bounds.Width / pointCount);
                var y = (float)(bounds.Y + centerY + rms * bounds.Height / 2);
                
                pathBuilder.AddLine(x, y);
            }
            
            pathBuilder.EndFigure(CanvasFigureLoop.Closed);
            
            using var geometry = CanvasGeometry.CreatePath(pathBuilder);
            session.FillGeometry(geometry, _options.RmsColor);
        }
    }

    private double TimeToX(double time, double startTime, double endTime, double width)
    {
        return (time - startTime) / (endTime - startTime) * width;
    }
}
