using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.Controls;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Views.Panels
{
    /// <summary>
    /// Plugin Health Dashboard view for monitoring plugin metrics and health status.
    /// Part of Phase 4: Enterprise Marketplace Expansion.
    /// </summary>
    public sealed partial class PluginHealthDashboardView : UserControl
    {
        public PluginHealthDashboardViewModel ViewModel { get; }
        private ToastNotificationService? _toastService;

        public PluginHealthDashboardView()
        {
            this.InitializeComponent();

            // Get dependencies from service locator
            var backendClient = AppServices.GetBackendClient();
            var context = AppServices.GetViewModelContext();

            // Create ViewModel
            ViewModel = new PluginHealthDashboardViewModel(context, backendClient);
            this.DataContext = ViewModel;

            // Initialize toast service
            _toastService = AppServices.TryGetToastNotificationService();

            // Subscribe to ViewModel events for toast notifications
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;

            // Load data on initialization
            ViewModel.RefreshCommand.ExecuteAsync(null);

            // Setup keyboard navigation
            this.Loaded += PluginHealthDashboardView_Loaded;

            // Setup Escape key to close help overlay
            KeyboardNavigationHelper.SetupEscapeKeyHandling(this, CloseHelpOverlay);
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PluginHealthDashboardViewModel.ErrorMessage) &&
                !string.IsNullOrEmpty(ViewModel.ErrorMessage))
            {
                _toastService?.ShowToast(ToastType.Error, "Plugin Health", ViewModel.ErrorMessage);
            }
            else if (e.PropertyName == nameof(PluginHealthDashboardViewModel.StatusMessage) &&
                     !string.IsNullOrEmpty(ViewModel.StatusMessage) &&
                     ViewModel.StatusMessage.Contains("success", System.StringComparison.OrdinalIgnoreCase))
            {
                _toastService?.ShowToast(ToastType.Success, "Plugin Health", ViewModel.StatusMessage);
            }
        }

        private void PluginHealthDashboardView_Loaded(object sender, RoutedEventArgs e)
        {
            // Setup Tab navigation order for this panel
            KeyboardNavigationHelper.SetupTabNavigation(this, 0);
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            if (HelpOverlayControl != null)
            {
                HelpOverlayControl.IsVisible = true;
                HelpOverlayControl.Visibility = Visibility.Visible;
            }
        }

        private void CloseHelpOverlay()
        {
            if (HelpOverlayControl?.IsVisible == true)
            {
                HelpOverlayControl.IsVisible = false;
                HelpOverlayControl.Visibility = Visibility.Collapsed;
            }
        }
    }
}
