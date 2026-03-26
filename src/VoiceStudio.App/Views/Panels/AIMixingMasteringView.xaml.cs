using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// AIMixingMasteringView panel - AI mixing and mastering assistant.
  /// </summary>
  public sealed partial class AIMixingMasteringView : Microsoft.UI.Xaml.Controls.UserControl
  {
    public AIMixingMasteringViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;

    public AIMixingMasteringView()
    {
      this.InitializeComponent();
      ViewModel = new AIMixingMasteringViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          ServiceProvider.GetAIMixingClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(AIMixingMasteringViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "AI Mixing & Mastering Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(AIMixingMasteringViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "AI Mixing & Mastering", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += AIMixingMasteringView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void AIMixingMasteringView_KeyboardNavigation_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "AI Mixing & Mastering Help";
      HelpOverlay.HelpText = "The AI Mixing & Mastering Assistant uses AI to automatically analyze and optimize your audio mix, particularly for multi-voice projects. It provides a 'virtual sound engineer' that balances voice track levels, applies EQ or compression, and outputs a polished mix or master with minimal effort. Choose between 'Balance Mix' mode for track balancing or 'Master for Podcast/Broadcast' mode for loudness targets. Click 'Analyze' to scan your mix and receive AI-generated suggestions in plain language. Preview individual suggestions or apply all at once. Use the 'Before/After' toggle to compare original vs. AI-mixed audio. The panel shows a detailed report card with loudness, peak levels, and dynamic range.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+A", Description = "Analyze mix" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+Enter", Description = "Apply all suggestions" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Space", Description = "Preview selected suggestion" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+M", Description = "Analyze mastering" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+Shift+M", Description = "Apply mastering" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Use 'Balance Mix' mode for multi-track balancing and EQ suggestions");
      HelpOverlay.Tips.Add("Use 'Master for Podcast' mode for loudness targets (-16 LUFS)");
      HelpOverlay.Tips.Add("AI suggestions are prioritized by importance (high/medium/low)");
      HelpOverlay.Tips.Add("Preview suggestions before applying to hear the changes");
      HelpOverlay.Tips.Add("Apply all suggestions for one-click enhancement");
      HelpOverlay.Tips.Add("Before/After toggle lets you compare original vs. processed audio");
      HelpOverlay.Tips.Add("Report card shows detailed analysis of your mix");
      HelpOverlay.Tips.Add("Target loudness varies by format (podcast: -16 LUFS, broadcast: -23 LUFS)");
      HelpOverlay.Tips.Add("High priority suggestions address critical mix issues");
      HelpOverlay.Tips.Add("Confidence score indicates AI's certainty in the suggestion");

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
            applyItem.Click += async (_, _) => await HandleSuggestionMenuClick("Apply", suggestion);
            menu.Items.Add(applyItem);

            var previewItem = new MenuFlyoutItem { Text = "Preview" };
            previewItem.Click += async (_, _) => await HandleSuggestionMenuClick("Preview", suggestion);
            menu.Items.Add(previewItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var dismissItem = new MenuFlyoutItem { Text = "Dismiss" };
            dismissItem.Click += async (_, _) => await HandleSuggestionMenuClick("Dismiss", suggestion);
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
        if (suggestion is not ViewModels.AIMixingMixSuggestionItem suggestionItem)
          return System.Threading.Tasks.Task.CompletedTask;

        switch (action.ToLower())
        {
          case "apply":
            ViewModel.SelectedSuggestion = suggestionItem;
            _toastService?.ShowToast(ToastType.Info, "Apply Suggestion", "Suggestion applied");
            break;
          case "preview":
            ViewModel.SelectedSuggestion = suggestionItem;
            _toastService?.ShowToast(ToastType.Info, "Preview Suggestion", "Previewing suggestion");
            break;
          case "dismiss":
            ViewModel.Suggestions.Remove(suggestionItem);
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