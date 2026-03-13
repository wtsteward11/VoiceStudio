using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// Engine Recommendation panel.
  /// Implements IDEA 47: Quality-Based Engine Recommendation System.
  /// </summary>
  public sealed partial class EngineRecommendationView : UserControl
  {
    public EngineRecommendationViewModel ViewModel { get; }
    private ToastNotificationService? _toastService;
    private ContextMenuService? _contextMenuService;

    public EngineRecommendationView()
    {
      this.InitializeComponent();
      ViewModel = new EngineRecommendationViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IQualityControlClient>()
      );
      this.DataContext = ViewModel;

      // Initialize services
      _toastService = ServiceProvider.GetToastNotificationService();
      _contextMenuService = ServiceProvider.GetContextMenuService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(EngineRecommendationViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Engine Recommendation Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(EngineRecommendationViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Engine Recommendation", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += EngineRecommendationView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void EngineRecommendationView_KeyboardNavigation_Loaded(object sender, RoutedEventArgs e)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void Recommendation_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var recommendation = element.DataContext ?? listView.SelectedItem;
        if (recommendation != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var useItem = new MenuFlyoutItem { Text = "Use This Engine" };
            useItem.Click += (_, _) => _toastService?.ShowToast(ToastType.Info, "Use Engine", "Engine recommendation selected");
            menu.Items.Add(useItem);

            var detailsItem = new MenuFlyoutItem { Text = "View Details" };
            detailsItem.Click += (_, _) => _toastService?.ShowToast(ToastType.Info, "View Details", "Showing engine details");
            menu.Items.Add(detailsItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }
  }
}