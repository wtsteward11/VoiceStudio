using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// DatasetQAView panel for dataset quality assurance reports.
  /// </summary>
  public sealed partial class DatasetQAView : UserControl
  {
    public DatasetQAViewModel ViewModel { get; }
    private ToastNotificationService? _toastService;

    public DatasetQAView()
    {
      this.InitializeComponent();
      ViewModel = new DatasetQAViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IDatasetQAClient>()
      );
      this.DataContext = ViewModel;

      _toastService = ServiceProvider.GetToastNotificationService();

      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(DatasetQAViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Dataset QA Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(DatasetQAViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Dataset QA", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += DatasetQAView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void DatasetQAView_KeyboardNavigation_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Dataset QA Reports Help";
      HelpOverlay.HelpText = "The Dataset QA Reports panel provides quality assurance analysis for training datasets. Select a dataset and run QA analysis to evaluate audio clip quality metrics (SNR, LUFS, overall quality score). View detailed results for each clip, see which clips pass or fail quality thresholds, and optionally cull low-quality clips from the dataset. This helps ensure your training datasets contain only high-quality audio samples.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+R", Description = "Run QA analysis" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh dataset list" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F1", Description = "Show help" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("QA analysis evaluates each audio clip for quality, SNR, and LUFS");
      HelpOverlay.Tips.Add("Quality score ranges from 0.0 to 1.0 - higher is better");
      HelpOverlay.Tips.Add("SNR (Signal-to-Noise Ratio) measures audio clarity - higher is better");
      HelpOverlay.Tips.Add("LUFS (Loudness Units) measures audio loudness - optimal around -23 LUFS");
      HelpOverlay.Tips.Add("Adjust quality thresholds to match your requirements");
      HelpOverlay.Tips.Add("Culling removes low-quality clips from the dataset permanently");
      HelpOverlay.Tips.Add("Review clip results before culling to avoid removing good samples");
      HelpOverlay.Tips.Add("High-quality datasets improve training results");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }
  }
}