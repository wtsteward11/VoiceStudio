using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.Controls;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Services;
using System.Threading;

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

    public QualityBenchmarkView()
    {
      this.InitializeComponent();
      ViewModel = new QualityBenchmarkViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IQualityControlClient>(),
          ServiceProvider.GetProfilesClient(),
          AppServices.GetEnginesClient(),
          AppServices.GetAudioPlayerService(),
          AppServices.GetVoiceSynthesisService(),
          AppServices.GetRequiredService<BackendClientConfig>());
      this.DataContext = ViewModel;

      _toastService = ServiceProvider.GetToastNotificationService();

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

      this.Loaded += QualityBenchmarkView_Loaded;

      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private async void QualityBenchmarkView_Loaded(object _, RoutedEventArgs __)
    {
      this.Loaded -= QualityBenchmarkView_Loaded;
      KeyboardNavigationHelper.SetupTabNavigation(this);
      await ViewModel.InitializeAsync(CancellationToken.None);
    }

    private void HelpButton_Click(object _, RoutedEventArgs __)
    {
      HelpOverlay.Title = "Quality Benchmarking Help";
      HelpOverlay.HelpText = "The Quality Benchmarking panel allows you to compare audio quality across different TTS engines. Select a voice profile, enter test text, choose engines to test, and run benchmarks to compare quality metrics (MOS score, similarity, naturalness) and performance across engines. Use this tool to find the best engine for your use case.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new KeyboardShortcut { Key = "Ctrl+R", Description = "Run benchmark" });
      HelpOverlay.Shortcuts.Add(new KeyboardShortcut { Key = "F1", Description = "Show help" });

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
  }
}
