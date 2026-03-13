using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using System.Threading;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// Quality Control View - Quality management dashboard panel.
  /// </summary>
  public sealed partial class QualityControlView : UserControl
  {
    public QualityControlViewModel ViewModel { get; }
    private ToastNotificationService? _toastService;

    public QualityControlView()
    {
      InitializeComponent();
      ViewModel = new QualityControlViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IQualityControlClient>());
      DataContext = ViewModel;

      // Initialize services
      _toastService = ServiceProvider.GetToastNotificationService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(QualityControlViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Quality Control Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(QualityControlViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Quality Control", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation and initial data load (ADR-047)
      this.Loaded += QualityControlView_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private async void QualityControlView_Loaded(object _, RoutedEventArgs __)
    {
      this.Loaded -= QualityControlView_Loaded;
      KeyboardNavigationHelper.SetupTabNavigation(this);
      await ViewModel.InitializeAsync(CancellationToken.None);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Quality Control Help";
      HelpOverlay.HelpText = "The Quality Control panel provides a comprehensive dashboard for managing and monitoring audio quality across all voice synthesis projects. View quality metrics, track quality trends, set quality thresholds, and receive alerts for quality issues. The dashboard helps maintain consistent quality standards and identify areas for improvement in voice synthesis workflows.";

      HelpOverlay.Shortcuts.Clear();

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Quality control dashboard monitors all voice synthesis operations");
      HelpOverlay.Tips.Add("Set quality thresholds to automatically flag low-quality outputs");
      HelpOverlay.Tips.Add("Track quality trends over time to identify improvements");
      HelpOverlay.Tips.Add("Quality alerts notify you of potential issues");
      HelpOverlay.Tips.Add("Use quality reports to analyze and improve synthesis settings");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }
  }
}