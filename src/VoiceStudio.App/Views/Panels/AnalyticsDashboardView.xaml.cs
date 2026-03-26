using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// AnalyticsDashboardView panel for analytics dashboard.
  /// </summary>
  public sealed partial class AnalyticsDashboardView : UserControl
  {
    public AnalyticsDashboardViewModel ViewModel { get; }
    private ToastNotificationService? _toastService;

    public AnalyticsDashboardView()
    {
      this.InitializeComponent();
      ViewModel = new AnalyticsDashboardViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          ServiceProvider.GetAnalyticsDashboardClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _toastService = ServiceProvider.GetToastNotificationService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(AnalyticsDashboardViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Analytics Dashboard Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(AnalyticsDashboardViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Analytics Dashboard", ViewModel.StatusMessage);
        }
      };
    }
  }
}