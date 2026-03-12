using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// MixAssistantView panel for AI mixing & mastering assistance.
  /// </summary>
  public sealed partial class MixAssistantView : UserControl
  {
    public MixAssistantViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;

    public MixAssistantView()
    {
      this.InitializeComponent();
      ViewModel = new MixAssistantViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          VoiceStudio.App.Services.ServiceProvider.GetBackendClient(),
          AppServices.GetProjectsClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(MixAssistantViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Mix Assistant Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(MixAssistantViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Mix Assistant", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += MixAssistantView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void MixAssistantView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "AI Mix Assistant Help";
      HelpOverlay.HelpText = "The AI Mix Assistant analyzes your audio mix and provides intelligent suggestions for improving levels, frequency balance, stereo field, and dynamics. Select a project, configure analysis options, and let AI analyze your mix to generate actionable recommendations. You can filter suggestions by category and priority, apply individual suggestions or apply all at once. Generate AI-powered mix presets based on your genre preferences for quick setup.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+A", Description = "Analyze mix" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Enter", Description = "Apply selected suggestion" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+Enter", Description = "Apply all suggestions" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Delete", Description = "Dismiss selected suggestion" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Analyze levels to check for clipping and optimal volume balance");
      HelpOverlay.Tips.Add("Frequency analysis helps identify tonal imbalances");
      HelpOverlay.Tips.Add("Stereo analysis checks width and imaging of your mix");
      HelpOverlay.Tips.Add("Dynamics analysis evaluates compression and dynamic range");
      HelpOverlay.Tips.Add("Filter suggestions by category (EQ, Compression, etc.) for focused workflow");
      HelpOverlay.Tips.Add("Use priority filters to see only critical or high-priority suggestions");
      HelpOverlay.Tips.Add("Generate presets to save time on similar projects");
      HelpOverlay.Tips.Add("Apply suggestions one at a time to hear individual changes");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void Suggestion_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var suggestion = element.DataContext ?? listView.SelectedItem;
        if (suggestion != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var applyItem = new MenuFlyoutItem { Text = "Apply" };
            applyItem.Click += async (_, __) => await HandleSuggestionMenuClick("Apply", suggestion);
            menu.Items.Add(applyItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var dismissItem = new MenuFlyoutItem { Text = "Dismiss" };
            dismissItem.Click += async (_, __) => await HandleSuggestionMenuClick("Dismiss", suggestion);
            menu.Items.Add(dismissItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private System.Threading.Tasks.Task HandleSuggestionMenuClick(string action, object suggestion)
    {
      try
      {
        switch (action.ToLower())
        {
          case "apply":
            ViewModel.SelectedSuggestion = (MixAssistantMixSuggestionItem)suggestion;
            _toastService?.ShowToast(ToastType.Info, "Apply Suggestion", "Suggestion applied");
            break;
          case "dismiss":
            ViewModel.Suggestions.Remove((MixAssistantMixSuggestionItem)suggestion);
            _toastService?.ShowToast(ToastType.Success, "Dismissed", "Suggestion dismissed");
            break;
        }
      }
      catch (System.Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }

      return System.Threading.Tasks.Task.CompletedTask;
    }
  }
}