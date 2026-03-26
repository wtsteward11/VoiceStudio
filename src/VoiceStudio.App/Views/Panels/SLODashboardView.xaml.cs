using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Views.Panels
{
    /// <summary>
    /// SLO Dashboard panel for Service Level Objective monitoring.
    /// Phase 5.2.1: SLO Dashboard with gauge chart visualization.
    /// </summary>
    public sealed partial class SLODashboardView : UserControl
    {
        /// <summary>Gets the ViewModel for this view.</summary>
        public SLODashboardViewModel ViewModel { get; }

        private readonly ToastNotificationService? _toastService;

        /// <summary>
        /// Initializes a new instance of the SLODashboardView.
        /// </summary>
        public SLODashboardView()
        {
            this.InitializeComponent();
            ViewModel = new SLODashboardViewModel(
                AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
                ServiceProvider.GetSLODashboardClient()
            );
            this.DataContext = ViewModel;

            // Initialize services
            _toastService = ServiceProvider.GetToastNotificationService();

            // Subscribe to ViewModel events for toast notifications
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;

            // Setup keyboard navigation
            this.Loaded += SLODashboardView_Loaded;

            // Setup Escape key to close help overlay
            KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
            {
                if (HelpOverlay.IsVisible)
                {
                    HelpOverlay.IsVisible = false;
                }
            });
        }

        private void OnViewModelPropertyChanged(
            object? sender,
            System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SLODashboardViewModel.ErrorMessage)
                && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
            {
                _toastService?.ShowToast(
                    ToastType.Error,
                    "SLO Dashboard Error",
                    ViewModel.ErrorMessage);
            }
            else if (e.PropertyName == nameof(SLODashboardViewModel.StatusMessage)
                && !string.IsNullOrEmpty(ViewModel.StatusMessage))
            {
                _toastService?.ShowToast(
                    ToastType.Success,
                    "SLO Dashboard",
                    ViewModel.StatusMessage);
            }
        }

        private void SLODashboardView_Loaded(
            object sender,
            Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            KeyboardNavigationHelper.SetupTabNavigation(this);
            _ = ViewModel.LoadSloDataAsync();
        }

        private void HelpButton_Click(
            object _,
            Microsoft.UI.Xaml.RoutedEventArgs __)
        {
            HelpOverlay.Title = "SLO Dashboard Help";
            HelpOverlay.HelpText = "The SLO Dashboard monitors Service Level Objectives " +
                "for VoiceStudio operations. Each gauge shows current performance " +
                "against defined targets. Green indicates healthy, yellow is warning, " +
                "and red indicates critical status requiring attention.";

            HelpOverlay.Shortcuts.Clear();
            HelpOverlay.Shortcuts.Add(
                new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh data" });
            HelpOverlay.Shortcuts.Add(
                new Controls.KeyboardShortcut { Key = "Escape", Description = "Close help" });

            HelpOverlay.Tips.Clear();
            HelpOverlay.Tips.Add(
                "SLOs are configured per operation type (synthesis, transcription, etc.)");
            HelpOverlay.Tips.Add(
                "Warning threshold triggers at 90% of target - time to investigate");
            HelpOverlay.Tips.Add(
                "Critical status means SLO is breached - immediate action needed");
            HelpOverlay.Tips.Add(
                "Click any gauge for detailed historical trends");
            HelpOverlay.Tips.Add(
                "SLO data refreshes automatically every 30 seconds");

            HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            HelpOverlay.Show();
        }
    }
}
