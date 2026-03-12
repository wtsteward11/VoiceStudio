using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.Services;
using System.Runtime.InteropServices.WindowsRuntime;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// Quality Benchmarking panel.
  /// Implements IDEA 52: Quality Benchmarking and Comparison Tool.
  /// </summary>
  public sealed partial class QualityBenchmarkView : UserControl
  {
    public QualityBenchmarkViewModel ViewModel { get; }
    private ToastNotificationService? _toastService;
    private ContextMenuService? _contextMenuService;

    public QualityBenchmarkView()
    {
      this.InitializeComponent();
      ViewModel = new QualityBenchmarkViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          ServiceProvider.GetBackendClient(),
          ServiceProvider.GetProfilesClient()
      );
      this.DataContext = ViewModel;

      // Initialize services
      _toastService = ServiceProvider.GetToastNotificationService();
      _contextMenuService = ServiceProvider.GetContextMenuService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(QualityBenchmarkViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Quality Benchmark Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(QualityBenchmarkViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Quality Benchmark", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += QualityBenchmarkView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void QualityBenchmarkView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Quality Benchmarking Help";
      HelpOverlay.HelpText = "The Quality Benchmarking panel allows you to compare audio quality across different TTS engines. Select a voice profile, enter test text, choose engines to test, and run benchmarks to compare quality metrics (MOS score, similarity, naturalness) and performance across engines. Use this tool to find the best engine for your use case.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+R", Description = "Run benchmark" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F1", Description = "Show help" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Quality benchmarking helps compare engines for your specific use case");
      HelpOverlay.Tips.Add("MOS (Mean Opinion Score) ranges from 1.0 to 5.0 - higher is better");
      HelpOverlay.Tips.Add("Similarity measures how closely the voice matches the reference (0.0-1.0)");
      HelpOverlay.Tips.Add("Naturalness measures how natural the voice sounds (0.0-1.0)");
      HelpOverlay.Tips.Add("Benchmarking can take time - results appear as engines complete");
      HelpOverlay.Tips.Add("Enable quality enhancement for higher quality but slower processing");
      HelpOverlay.Tips.Add("Compare results to find the best engine for your needs");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void BenchmarkResult_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var result = element.DataContext ?? listView.SelectedItem;
        if (result != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var viewItem = new MenuFlyoutItem { Text = "View Details" };
            viewItem.Click += (_, _) => _toastService?.ShowToast(ToastType.Info, "View Details", "Showing benchmark result details");
            menu.Items.Add(viewItem);

            var exportItem = new MenuFlyoutItem { Text = "Export Result" };
            exportItem.Click += async (_, _) => await ExportBenchmarkResultAsync(result);
            menu.Items.Add(exportItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private async System.Threading.Tasks.Task ExportBenchmarkResultAsync(object result)
    {
      try
      {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("JSON", new[] { ".json" });
        picker.FileTypeChoices.Add("CSV", new[] { ".csv" });
        picker.SuggestedFileName = "benchmark_result_export";

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
          var extension = file.FileType.ToLower();
          string content;

          if (extension == ".json")
          {
            var resultType = result.GetType();
            var jsonData = new
            {
              Engine = resultType.GetProperty("Engine")?.GetValue(result)?.ToString() ?? "unknown",
              MosScore = resultType.GetProperty("MosScore")?.GetValue(result)?.ToString() ?? "0",
              Similarity = resultType.GetProperty("Similarity")?.GetValue(result)?.ToString() ?? "0",
              Naturalness = resultType.GetProperty("Naturalness")?.GetValue(result)?.ToString() ?? "0",
              ProcessingTime = resultType.GetProperty("ProcessingTime")?.GetValue(result)?.ToString() ?? "0"
            };
            content = System.Text.Json.JsonSerializer.Serialize(jsonData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
          }
          else
          {
            var resultType = result.GetType();
            content = "Engine,MOS Score,Similarity,Naturalness,Processing Time\n";
            content += $"\"{resultType.GetProperty("Engine")?.GetValue(result)?.ToString() ?? "unknown"}\",";
            content += $"{resultType.GetProperty("MosScore")?.GetValue(result)?.ToString() ?? "0"},";
            content += $"{resultType.GetProperty("Similarity")?.GetValue(result)?.ToString() ?? "0"},";
            content += $"{resultType.GetProperty("Naturalness")?.GetValue(result)?.ToString() ?? "0"},";
            content += $"{resultType.GetProperty("ProcessingTime")?.GetValue(result)?.ToString() ?? "0"}";
          }

          await Windows.Storage.FileIO.WriteTextAsync(file, content);
          _toastService?.ShowToast(ToastType.Success, "Export", "Benchmark result exported successfully");
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Export Failed", ex.Message);
      }
    }
  }
}