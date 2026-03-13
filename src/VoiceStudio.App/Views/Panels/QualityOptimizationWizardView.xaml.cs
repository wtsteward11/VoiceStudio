using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;
using System.Threading;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// Quality Optimization Wizard panel - Voice Profile Quality Optimization Wizard.
  /// Implements IDEA 43: Voice Profile Quality Optimization Wizard.
  /// </summary>
  public sealed partial class QualityOptimizationWizardView : UserControl
  {
    public QualityOptimizationWizardViewModel ViewModel { get; }
    private ToastNotificationService? _toastService;

    public QualityOptimizationWizardView()
    {
      this.InitializeComponent();
      ViewModel = new QualityOptimizationWizardViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetRequiredService<IVoiceSynthesisService>(),
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IQualityControlClient>(),
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IProfilesClient>()
      );
      this.DataContext = ViewModel;

      // Initialize services
      _toastService = ServiceProvider.GetToastNotificationService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(QualityOptimizationWizardViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Quality Optimization Error", ViewModel.ErrorMessage);
        }
      };

      // Setup keyboard navigation and initial data load (ADR-047)
      this.Loaded += QualityOptimizationWizardView_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private async void QualityOptimizationWizardView_Loaded(object _, RoutedEventArgs __)
    {
      this.Loaded -= QualityOptimizationWizardView_Loaded;
      KeyboardNavigationHelper.SetupTabNavigation(this);
      await ViewModel.InitializeAsync(CancellationToken.None);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Quality Optimization Wizard Help";
      HelpOverlay.HelpText = "The Quality Optimization Wizard guides you through optimizing your voice profile's quality. Step 1: Select a profile and target quality tier. Step 2: Analyze current quality metrics. Step 3: Review recommendations and deficiencies. Step 4: Apply optimizations. Step 5: View optimization results and optimized parameters.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh profiles list" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Escape", Description = "Close help" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Select a voice profile that you want to optimize");
      HelpOverlay.Tips.Add("Choose a target quality tier (fast, standard, high, ultra, professional)");
      HelpOverlay.Tips.Add("Enter test text to synthesize and measure quality");
      HelpOverlay.Tips.Add("Review quality deficiencies and recommendations");
      HelpOverlay.Tips.Add("Apply optimizations to improve quality metrics");
      HelpOverlay.Tips.Add("View optimized parameters and results");
      HelpOverlay.Tips.Add("Use the Reset button to start over");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }
  }
}