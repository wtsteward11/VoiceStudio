using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Animation;

namespace VoiceStudio.App.Controls
{
  /// <summary>
  /// Panel preview popup control that displays panel information on hover.
  /// Implements IDEA 20: Panel Preview on Hover.
  /// </summary>
  public sealed partial class PanelPreviewPopup : UserControl
  {
    private Popup? _popup;
    private UIElement? _targetElement;

    public PanelPreviewPopup()
    {
      this.InitializeComponent();
      _popup = new Popup
      {
        Child = this,
        IsLightDismissEnabled = false
      };
    }

    /// <summary>
    /// Shows the preview popup near the target element.
    /// </summary>
    public void Show(UIElement targetElement, string title, string description, string iconGlyph, UIElement? previewContent = null)
    {
      if (_popup == null)
        return;

      _targetElement = targetElement;
      PreviewTitle.Text = title;
      PreviewDescription.Text = description;
      PreviewIcon.Glyph = iconGlyph;

      if (previewContent != null)
      {
        PreviewContentPresenter.Content = previewContent;
      }
      else
      {
        PreviewContentPresenter.Content = null;
      }

      // Get the root element for coordinate transformation
      var rootElement = targetElement.XamlRoot?.Content as FrameworkElement;
      if (rootElement == null)
      {
        // Fallback: try to get from App MainWindowInstance
        if (App.MainWindowInstance?.Content is FrameworkElement mainContent)
        {
          rootElement = mainContent;
        }
      }

      if (rootElement != null)
      {
        // Calculate position relative to root
        var transform = targetElement.TransformToVisual(rootElement);
        var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

        // Position to the right of the nav rail
        if (targetElement is FrameworkElement fe)
        {
          _popup.HorizontalOffset = point.X + fe.ActualWidth + 8;
        }
        else
        {
          _popup.HorizontalOffset = point.X + 8;
        }
        _popup.VerticalOffset = point.Y - 8;
      }

      var xamlRoot = targetElement.XamlRoot ?? (App.MainWindowInstance?.Content as FrameworkElement)?.XamlRoot;
      if (xamlRoot == null)
        return;

      _popup.XamlRoot = xamlRoot;
      _popup.IsOpen = true;

      // Animate in using Storyboard
      var storyboard = new Storyboard();
      var fadeIn = new FadeInThemeAnimation();
      fadeIn.Duration = TimeSpan.FromMilliseconds(200);
      Storyboard.SetTarget(fadeIn, this);
      storyboard.Children.Add(fadeIn);
      storyboard.Begin();
    }

    /// <summary>
    /// Hides the preview popup.
    /// </summary>
    public void Hide()
    {
      if (_popup?.IsOpen != true)
        return;

      // Animate out using Storyboard
      var storyboard = new Storyboard();
      var fadeOut = new FadeOutThemeAnimation();
      fadeOut.Duration = TimeSpan.FromMilliseconds(150);
      Storyboard.SetTarget(fadeOut, this);
      storyboard.Children.Add(fadeOut);
      storyboard.Begin();

      // Close after animation
      var timer = new DispatcherTimer
      {
        Interval = TimeSpan.FromMilliseconds(150)
      };
      timer.Tick += (_, _) =>
      {
        timer.Stop();
        _popup.IsOpen = false;
      };
      timer.Start();
    }

    /// <summary>
    /// Updates the preview position if the target element moves.
    /// </summary>
    public void UpdatePosition()
    {
      if (_popup == null || _targetElement == null || !_popup.IsOpen)
        return;

      var rootElement = _targetElement.XamlRoot?.Content as FrameworkElement;
      if (rootElement == null)
      {
        // Fallback: try to get from App MainWindowInstance
        if (App.MainWindowInstance?.Content is FrameworkElement mainContent)
        {
          rootElement = mainContent;
        }
      }

      if (rootElement != null && _targetElement != null)
      {
        var transform = _targetElement.TransformToVisual(rootElement);
        var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

        if (_targetElement is FrameworkElement fe)
        {
          _popup.HorizontalOffset = point.X + fe.ActualWidth + 8;
        }
        else
        {
          _popup.HorizontalOffset = point.X + 8;
        }
        _popup.VerticalOffset = point.Y - 8;
      }
    }
  }
}