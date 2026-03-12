using System;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.UI;

namespace VoiceStudio.App.Controls
{
    /// <summary>
    /// Win2D-based timeline ruler that draws time labels and tick marks dynamically
    /// based on duration and zoom. Replaces static XAML ruler (TRAP 3).
    /// </summary>
    public sealed partial class TimeRulerControl : UserControl
    {
        public static readonly DependencyProperty DurationProperty =
            DependencyProperty.Register(
                nameof(Duration),
                typeof(double),
                typeof(TimeRulerControl),
                new PropertyMetadata(60.0, OnRulerPropertyChanged));

        public static readonly DependencyProperty PixelsPerSecondProperty =
            DependencyProperty.Register(
                nameof(PixelsPerSecond),
                typeof(double),
                typeof(TimeRulerControl),
                new PropertyMetadata(100.0, OnRulerPropertyChanged));

        public static readonly DependencyProperty TrackHeaderWidthProperty =
            DependencyProperty.Register(
                nameof(TrackHeaderWidth),
                typeof(double),
                typeof(TimeRulerControl),
                new PropertyMetadata(160.0, OnRulerPropertyChanged));

        public double Duration
        {
            get => (double)GetValue(DurationProperty);
            set => SetValue(DurationProperty, value);
        }

        public double PixelsPerSecond
        {
            get => (double)GetValue(PixelsPerSecondProperty);
            set => SetValue(PixelsPerSecondProperty, value);
        }

        public double TrackHeaderWidth
        {
            get => (double)GetValue(TrackHeaderWidthProperty);
            set => SetValue(TrackHeaderWidthProperty, value);
        }

        private static void OnRulerPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TimeRulerControl ctrl && ctrl.RulerCanvas != null)
            {
                ctrl.RulerCanvas.Invalidate();
            }
        }

        public TimeRulerControl()
        {
            InitializeComponent();
        }

        private void RulerCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RulerCanvas?.Invalidate();
        }

        private void RulerCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            var session = args.DrawingSession;
            var width = sender.ActualWidth;
            var height = sender.ActualHeight;

            if (width <= 0 || height <= 0 || PixelsPerSecond <= 0)
            {
                return;
            }

            var textColor = Microsoft.UI.Colors.Gray;
            var tickColor = Microsoft.UI.Colors.DarkGray;

            // Choose tick interval based on zoom (pixels per second)
            double tickIntervalSeconds = GetTickInterval(PixelsPerSecond);
            double dur = Math.Max(1, Duration);

            // Draw tick marks and labels from 0 to duration
            for (double t = 0; t <= dur; t += tickIntervalSeconds)
            {
                double x = TrackHeaderWidth + (t * PixelsPerSecond);
                if (x < TrackHeaderWidth - 2)
                {
                    continue;
                }
                if (x > width + 20)
                {
                    break;
                }

                // Tick mark
                bool isMajor = IsMajorTick(t, tickIntervalSeconds);
                float tickHeight = (float)(isMajor ? 8 : 4);
                float yBase = (float)(height - 2);

                session.DrawLine(
                    (float)x, (float)height,
                    (float)x, (float)(height - tickHeight),
                    tickColor,
                    1);

                // Label for major ticks
                if (isMajor)
                {
                    var label = FormatTime(t);
                    using var textFormat = new CanvasTextFormat { FontSize = 9, FontFamily = "Segoe UI" };
                    float labelW = 40;
                    float labelH = 14;
                    float labelX = (float)(x - labelW / 2);
                    float labelY = 2;
                    session.DrawText(label, labelX, labelY, labelW, labelH, textColor, textFormat);
                }
            }
        }

        private static double GetTickInterval(double pixelsPerSecond)
        {
            // Target ~80-120 pixels between major ticks
            double targetPixels = 100;
            double targetSeconds = targetPixels / pixelsPerSecond;

            double[] intervals = { 0.1, 0.25, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300 };
            double chosen = 1;
            foreach (var i in intervals)
            {
                if (i >= targetSeconds * 0.5)
                {
                    chosen = i;
                    break;
                }
                chosen = i;
            }
            return chosen;
        }

        private static bool IsMajorTick(double time, double tickInterval)
        {
            // Major ticks at whole seconds/minutes
            if (tickInterval >= 60)
            {
                return time % 60 == 0;
            }
            if (tickInterval >= 1)
            {
                return time % (tickInterval * 5) < tickInterval * 0.5;
            }
            return time % (tickInterval * 10) < tickInterval * 0.5;
        }

        private static string FormatTime(double seconds)
        {
            int m = (int)(seconds / 60);
            int s = (int)(seconds % 60);
            return $"{m}:{s:D2}";
        }
    }
}
