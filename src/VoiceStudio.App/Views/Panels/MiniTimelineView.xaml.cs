using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml.Media;
using System;
using System.Linq;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// Mini timeline view for BottomPanelHost.
  /// Implements IDEA 6: Mini Timeline in BottomPanelHost.
  /// </summary>
  public sealed partial class MiniTimelineView : UserControl
  {
    public MiniTimelineViewModel ViewModel { get; }
    private bool _isDragging;
    private ToastNotificationService? _toastService;

    public MiniTimelineView()
    {
      this.InitializeComponent();
      ViewModel = new MiniTimelineViewModel(
          ServiceProvider.GetBackendClient(),
          ServiceProvider.GetAudioPlayerService()
      );
      this.DataContext = ViewModel;

      // Initialize services
      _toastService = ServiceProvider.GetToastNotificationService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (_, _) =>
      {
        if (false)
        {
          // Error message removed - property not available
        }
        else if (false)
        {
          // Status message removed - property not available
        }
      };

      // Setup keyboard navigation
      this.Loaded += MiniTimelineView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        // Help overlay not implemented for MiniTimelineView
      });

      // Subscribe to property changes to update UI
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(ViewModel.TimelineZoom) ||
                  e.PropertyName == nameof(ViewModel.Duration))
        {
          UpdateTimeRuler();
        }
      };

      // Initialize time ruler after loaded
      this.Loaded += (_, __) => UpdateTimeRuler();
    }

    private void UpdateTimeRuler()
    {
      if (TimeRulerCanvas == null || ViewModel == null)
        return;

      TimeRulerCanvas.Children.Clear();

      var duration = ViewModel.Duration;
      if (duration <= 0)
        return;

      var pixelsPerSecond = ViewModel.PixelsPerSecond;
      var width = TimeRulerCanvas.ActualWidth;
      if (width <= 0)
        return;

      // Draw time markers every 5 seconds (or adjust based on zoom)
      var markerInterval = 5.0; // seconds
      if (pixelsPerSecond < 20)
        markerInterval = 10.0; // Show every 10 seconds when zoomed out
      else if (pixelsPerSecond > 50)
        markerInterval = 1.0; // Show every second when zoomed in

      for (double time = 0; time <= duration; time += markerInterval)
      {
        var x = time * pixelsPerSecond;

        // Major marker (every 10 seconds or at minute marks)
        var isMajor = time % 10 == 0 || time % 60 == 0;
        var line = new Line
        {
          X1 = x,
          Y1 = isMajor ? 0 : 8,
          X2 = x,
          Y2 = 24,
          Stroke = new SolidColorBrush(Microsoft.UI.Colors.Gray),
          StrokeThickness = isMajor ? 1.5 : 1,
          Opacity = isMajor ? 0.8 : 0.5
        };
        TimeRulerCanvas.Children.Add(line);

        // Time label (every 10 seconds or at minute marks)
        if (isMajor)
        {
          var label = new TextBlock
          {
            Text = FormatTime(time),
            FontSize = 9,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.LightGray),
            Margin = new Microsoft.UI.Xaml.Thickness(x + 2, 2, 0, 0)
          };
          TimeRulerCanvas.Children.Add(label);
        }
      }
    }

    private string FormatTime(double seconds)
    {
      var minutes = (int)(seconds / 60);
      var secs = (int)(seconds % 60);
      return $"{minutes}:{secs:D2}";
    }

    private void TimelineCanvas_PointerPressed(object _, PointerRoutedEventArgs e)
    {
      _isDragging = true;
      SeekToPointerPosition(e);
      TimelineCanvas.CapturePointer(e.Pointer);
    }

    private void TimelineCanvas_PointerMoved(object _, PointerRoutedEventArgs e)
    {
      if (_isDragging)
      {
        SeekToPointerPosition(e);
      }
    }

    private void TimelineCanvas_PointerReleased(object _, PointerRoutedEventArgs e)
    {
      if (_isDragging)
      {
        SeekToPointerPosition(e);
        _isDragging = false;
        TimelineCanvas.ReleasePointerCapture(e.Pointer);
      }
    }

    private void SeekToPointerPosition(PointerRoutedEventArgs e)
    {
      if (TimelineCanvas == null || ViewModel == null)
        return;

      var point = e.GetCurrentPoint(TimelineCanvas);
      var x = point.Position.X;
      var pixelsPerSecond = ViewModel.PixelsPerSecond;
      var time = x / pixelsPerSecond;

      // Clamp to valid range
      time = Math.Max(0, Math.Min(time, ViewModel.Duration));

      ViewModel.SeekToTime(time);
    }

    private void MiniTimelineView_KeyboardNavigation_Loaded(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }
  }
}