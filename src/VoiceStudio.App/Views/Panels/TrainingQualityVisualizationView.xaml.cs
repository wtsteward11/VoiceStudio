using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// TrainingQualityVisualizationView panel for training quality metrics visualization.
  /// </summary>
  public sealed partial class TrainingQualityVisualizationView : UserControl
  {
    public TrainingQualityVisualizationViewModel ViewModel { get; }
    private ToastNotificationService? _toastService;

    public TrainingQualityVisualizationView()
    {
      this.InitializeComponent();
      ViewModel = new TrainingQualityVisualizationViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetRequiredService<VoiceStudio.Core.Services.ITrainingClient>()
      );
      this.DataContext = ViewModel;

      _toastService = ServiceProvider.GetToastNotificationService();

      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(TrainingQualityVisualizationViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Training Quality Visualization Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(TrainingQualityVisualizationViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Training Quality Visualization", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += TrainingQualityVisualizationView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void TrainingQualityVisualizationView_KeyboardNavigation_Loaded(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Training Quality Visualization Help";
      HelpOverlay.HelpText = "The Training Quality Visualization panel provides comprehensive visualization of training quality metrics over epochs. Select a training job to view quality score trends, training/validation loss, MOS scores, similarity, and naturalness metrics. The panel displays summary statistics, best/worst metrics, and detailed quality history. Use this to monitor training progress, identify quality improvements, and detect potential issues during training.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F1", Description = "Show help" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh training jobs list" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Tab", Description = "Navigate between controls" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Quality score ranges from 0.0 to 1.0 - higher is better");
      HelpOverlay.Tips.Add("MOS (Mean Opinion Score) ranges from 1.0 to 5.0 - higher is better");
      HelpOverlay.Tips.Add("Similarity and naturalness range from 0.0 to 1.0 - higher is better");
      HelpOverlay.Tips.Add("Training loss should decrease over epochs for good training");
      HelpOverlay.Tips.Add("Validation loss helps detect overfitting");
      HelpOverlay.Tips.Add("Best metrics show the epoch with highest quality");
      HelpOverlay.Tips.Add("Worst metrics show the epoch with lowest quality");
      HelpOverlay.Tips.Add("Quality trends help identify training improvements");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }
  }
}