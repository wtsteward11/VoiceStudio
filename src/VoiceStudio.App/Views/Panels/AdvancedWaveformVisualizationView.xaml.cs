using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// Advanced waveform visualization panel.
  /// Currently using code-only UI to avoid XamlCompiler.exe crashes with complex XAML.
  /// </summary>
  public sealed partial class AdvancedWaveformVisualizationView : UserControl
  {
    public AdvancedWaveformVisualizationView()
    {
      // Build UI programmatically to avoid XAML compiler issues
      var rootGrid = new Grid
      {
        Background = (Brush)Application.Current.Resources["VSQ.Panel.Background"]
      };
      rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

      // Header
      var headerBorder = new Border
      {
        Background = (Brush)Application.Current.Resources["VSQ.Panel.Background.HeaderBrush"],
        Padding = new Thickness(16, 12, 16, 12)
      };
      var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
      headerStack.Children.Add(new FontIcon { Glyph = "\uE9D9", FontSize = 20 });
      headerStack.Children.Add(new TextBlock
      {
        Text = "Advanced Waveform",
        FontSize = 18,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Foreground = (Brush)Application.Current.Resources["VSQ.Text.PrimaryBrush"]
      });
      headerBorder.Child = headerStack;
      Grid.SetRow(headerBorder, 0);
      rootGrid.Children.Add(headerBorder);

      // Content: Under Development
      var contentGrid = new Grid { Padding = new Thickness(32) };
      var contentStack = new StackPanel
      {
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        Spacing = 16,
        MaxWidth = 400
      };

      contentStack.Children.Add(new FontIcon
      {
        Glyph = "\uE9D9",
        FontSize = 64,
        Opacity = 0.4,
        HorizontalAlignment = HorizontalAlignment.Center
      });

      contentStack.Children.Add(new TextBlock
      {
        Text = "Advanced Waveform Visualization",
        FontSize = 18,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Foreground = (Brush)Application.Current.Resources["VSQ.Text.PrimaryBrush"],
        HorizontalAlignment = HorizontalAlignment.Center
      });

      contentStack.Children.Add(new TextBlock
      {
        Text = "Enhanced waveform visualization with zoom, multi-track overlay, and annotation support. This feature is under development.",
        Foreground = (Brush)Application.Current.Resources["VSQ.Text.SecondaryBrush"],
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center
      });

      // Planned features box
      var featuresBox = new Border
      {
        Background = (Brush)Application.Current.Resources["VSQ.Panel.Background.DarkBrush"],
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(16),
        Margin = new Thickness(0, 16, 0, 0)
      };
      var featuresStack = new StackPanel { Spacing = 8 };
      featuresStack.Children.Add(new TextBlock
      {
        Text = "Planned Features:",
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Foreground = (Brush)Application.Current.Resources["VSQ.Text.PrimaryBrush"]
      });
      featuresStack.Children.Add(new TextBlock { Text = "- High-resolution waveform rendering", Foreground = (Brush)Application.Current.Resources["VSQ.Text.SecondaryBrush"] });
      featuresStack.Children.Add(new TextBlock { Text = "- Multi-track overlay comparison", Foreground = (Brush)Application.Current.Resources["VSQ.Text.SecondaryBrush"] });
      featuresStack.Children.Add(new TextBlock { Text = "- Region selection and export", Foreground = (Brush)Application.Current.Resources["VSQ.Text.SecondaryBrush"] });
      featuresStack.Children.Add(new TextBlock { Text = "- Peak and RMS visualization", Foreground = (Brush)Application.Current.Resources["VSQ.Text.SecondaryBrush"] });
      featuresBox.Child = featuresStack;
      contentStack.Children.Add(featuresBox);

      contentGrid.Children.Add(contentStack);
      Grid.SetRow(contentGrid, 1);
      rootGrid.Children.Add(contentGrid);

      Content = rootGrid;
    }
  }
}
