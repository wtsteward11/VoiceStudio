using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml;
using System;
using System.Threading.Tasks;

namespace VoiceStudio.App.Controls
{
  /// <summary>
  /// Custom control for displaying plotly-generated static chart images from backend endpoints.
  /// </summary>
  public sealed partial class PlotlyControl : UserControl
  {
    private string? _chartUrl;
    private bool _isLoading;

    public PlotlyControl()
    {
      this.InitializeComponent();
    }

    /// <summary>
    /// URL of the plotly chart image to display. Only static image formats are supported.
    /// </summary>
    public string? ChartUrl
    {
      get => _chartUrl;
      set
      {
        if (!string.IsNullOrWhiteSpace(value))
        {
          // Validate that URL is for image format only (reject HTML)
          var isHtml = value.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                      value.Contains("/html") ||
                      value.Contains("format=html");

          if (isHtml)
          {
            ChartImage.Source = null;
            ChartImage.Visibility = Visibility.Collapsed;
            EmptyStateText.Text = "Only static image formats (PNG, JPEG, etc.) are supported. HTML charts are not supported in this Windows-native application.";
            EmptyStateText.Visibility = Visibility.Visible;
            IsLoading = false;
            return;
          }
        }

        _chartUrl = value;
        _ = LoadChartAsync();
      }
    }

    /// <summary>
    /// Whether the control is currently loading a chart.
    /// </summary>
    public bool IsLoading
    {
      get => _isLoading;
      private set
      {
        _isLoading = value;
        LoadingIndicator.IsActive = value;
        LoadingIndicator.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
      }
    }

    /// <summary>
    /// Title or description of the chart.
    /// </summary>
    public string? ChartTitle
    {
      get => (string?)GetValue(ChartTitleProperty);
      set => SetValue(ChartTitleProperty, value);
    }

    public static readonly DependencyProperty ChartTitleProperty =
        DependencyProperty.Register(nameof(ChartTitle), typeof(string), typeof(PlotlyControl), new PropertyMetadata(null));

    /// <summary>
    /// Minimum height for the chart display area.
    /// </summary>
    public double MinChartHeight
    {
      get => (double)GetValue(MinChartHeightProperty);
      set => SetValue(MinChartHeightProperty, value);
    }

    public static readonly DependencyProperty MinChartHeightProperty =
        DependencyProperty.Register(nameof(MinChartHeight), typeof(double), typeof(PlotlyControl), new PropertyMetadata(200.0));

    private async Task LoadChartAsync()
    {
      if (string.IsNullOrWhiteSpace(_chartUrl))
      {
        ChartImage.Source = null;
        ChartImage.Visibility = Visibility.Collapsed;
        EmptyStateText.Visibility = Visibility.Visible;
        IsLoading = false;
        return;
      }

      try
      {
        IsLoading = true;
        ChartImage.Visibility = Visibility.Collapsed;
        EmptyStateText.Visibility = Visibility.Collapsed;

        // Load as static image (only image formats supported)
        await LoadImageAsync(_chartUrl);
      }
      catch (Exception ex)
      {
        ChartImage.Source = null;
        ChartImage.Visibility = Visibility.Collapsed;
        EmptyStateText.Text = $"Error loading chart: {ex.Message}";
        EmptyStateText.Visibility = Visibility.Visible;
        IsLoading = false;
      }
    }

    private Task LoadImageAsync(string imageUrl)
    {
      try
      {
        var bitmapImage = new BitmapImage();

        // Handle both absolute URLs and relative paths
        if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var absoluteUri))
        {
          bitmapImage.UriSource = absoluteUri;
        }
        else
        {
          // For relative URLs, construct full URL (assuming backend base URL)
          const string baseUrl = "http://localhost:8000";
          var fullUrl = imageUrl.StartsWith("/") ? baseUrl + imageUrl : $"{baseUrl}/{imageUrl}";
          if (Uri.TryCreate(fullUrl, UriKind.Absolute, out var fullUri))
          {
            bitmapImage.UriSource = fullUri;
          }
          else
          {
            throw new ArgumentException($"Invalid image URL: {imageUrl}");
          }
        }

        // Wait for image to load
        bitmapImage.ImageOpened += (_, _) =>
        {
          ChartImage.Source = bitmapImage;
          ChartImage.Visibility = Visibility.Visible;
          IsLoading = false;
        };

        bitmapImage.ImageFailed += (_, e) =>
        {
          ChartImage.Source = null;
          ChartImage.Visibility = Visibility.Collapsed;
          EmptyStateText.Text = $"Failed to load chart: {e.ErrorMessage}";
          EmptyStateText.Visibility = Visibility.Visible;
          IsLoading = false;
        };
      }
      catch (Exception ex)
      {
        ChartImage.Source = null;
        ChartImage.Visibility = Visibility.Collapsed;
        EmptyStateText.Text = $"Error loading chart image: {ex.Message}";
        EmptyStateText.Visibility = Visibility.Visible;
        IsLoading = false;
      }

      return Task.CompletedTask;
    }

    /// <summary>
    /// Refresh the chart by reloading from the current URL.
    /// </summary>
    public void Refresh()
    {
      _ = LoadChartAsync();
    }

    /// <summary>
    /// Clear the chart display.
    /// </summary>
    public void Clear()
    {
      _chartUrl = null;
      ChartImage.Source = null;
      ChartImage.Visibility = Visibility.Collapsed;
      EmptyStateText.Text = "No chart data available";
      EmptyStateText.Visibility = Visibility.Visible;
      IsLoading = false;
    }
  }
}