using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Controls
{
    /// <summary>
    /// Single automation curve editor: draws curve and optional point handles.
    /// </summary>
    public sealed partial class AutomationCurveEditorControl : UserControl
    {
        public static readonly DependencyProperty CurveProperty =
            DependencyProperty.Register(nameof(Curve), typeof(AutomationCurve), typeof(AutomationCurveEditorControl),
                new PropertyMetadata(null, OnCurveChanged));
        public static readonly DependencyProperty TrackIdProperty =
            DependencyProperty.Register(nameof(TrackId), typeof(string), typeof(AutomationCurveEditorControl),
                new PropertyMetadata(null));

        public AutomationCurve? Curve { get => (AutomationCurve?)GetValue(CurveProperty); set => SetValue(CurveProperty, value); }
        public string? TrackId { get => (string?)GetValue(TrackIdProperty); set => SetValue(TrackIdProperty, value); }
        public bool HasSelectedPoint { get; private set; }

        private static void OnCurveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((AutomationCurveEditorControl)d).UpdateVisual();

        public AutomationCurveEditorControl()
        {
            InitializeComponent();
            Loaded += (_, _) => UpdateVisual();
            SizeChanged += (_, _) => UpdateVisual();
        }

        public Task LoadCurvesAsync() => Task.CompletedTask;

        private void UpdateVisual()
        {
            var curve = Curve;
            PointsCanvas.Children.Clear();
            if (curve?.Points == null || curve.Points.Count == 0)
            {
                CurvePath.Data = null;
                EmptyText.Visibility = Visibility.Visible;
                return;
            }
            EmptyText.Visibility = Visibility.Collapsed;
            double w = EditorRoot.ActualWidth;
            double h = EditorRoot.ActualHeight;
            if (w <= 0 || h <= 0) return;

            double maxT = 0.001;
            double minV = 0, maxV = 1;
            foreach (var p in curve.Points) { if (p.Time > maxT) maxT = p.Time; if (p.Value < minV) minV = p.Value; if (p.Value > maxV) maxV = p.Value; }
            if (maxT <= 0) maxT = 1;
            if (maxV <= minV) maxV = minV + 0.01;

            var pf = new PathFigure();
            for (int i = 0; i < curve.Points.Count; i++)
            {
                var p = curve.Points[i];
                double x = (p.Time / maxT) * w;
                double y = h - (p.Value - minV) / (maxV - minV) * h;
                if (i == 0) pf.StartPoint = new Windows.Foundation.Point(x, y);
                else pf.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(x, y) });
            }
            CurvePath.Data = new PathGeometry { Figures = { pf } };

            foreach (var p in curve.Points)
            {
                double x = (p.Time / maxT) * w;
                double y = h - (p.Value - minV) / (maxV - minV) * h;
                var el = new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(Microsoft.UI.Colors.White), Stroke = new SolidColorBrush(Microsoft.UI.Colors.Cyan), StrokeThickness = 1 };
                Canvas.SetLeft(el, x - 4);
                Canvas.SetTop(el, y - 4);
                PointsCanvas.Children.Add(el);
            }
        }
    }
}
