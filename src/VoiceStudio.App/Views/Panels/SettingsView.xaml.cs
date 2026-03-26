using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VoiceStudio.App.Controls;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Services;
using Microsoft.UI.Xaml;

namespace VoiceStudio.App.Views.Panels
{
  public sealed partial class SettingsView : UserControl
  {
    public SettingsViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;

    public SettingsView()
    {
      this.InitializeComponent();
      var settingsService = AppServices.GetSettingsService();
      var settingsClient = AppServices.GetRequiredService<ISettingsClient>();
      var pluginManager = AppServices.GetService<PluginManager>();
      var context = AppServices.GetViewModelContext();
      ViewModel = new SettingsViewModel(
          context,
          settingsService,
          settingsClient,
          pluginManager,
          AppServices.GetService<ITelemetryService>());
      this.DataContext = ViewModel;

      // Initialize services
      _contextMenuService = AppServices.GetContextMenuService();
      _toastService = AppServices.TryGetToastNotificationService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(SettingsViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Settings Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(SettingsViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Settings", ViewModel.StatusMessage);
        }
      };

      // Load settings on initialization
      ViewModel.LoadSettingsCommand.ExecuteAsync(null);

      // Setup keyboard navigation
      this.Loaded += SettingsView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        // Close any open dialogs or overlays
      });
    }

    private void SettingsView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      // Setup Tab navigation order for this panel
      KeyboardNavigationHelper.SetupTabNavigation(this, 0);
    }

    private void CategoryButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
      if (sender is Button button && button.Tag is string category)
      {
        // Hide all panels
        GeneralPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        EnginePanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        AudioPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        TimelinePanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        BackendPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        PerformancePanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        PluginsPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        McpPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        PrivacyPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        SystemPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;

        // Show selected panel
        switch (category)
        {
          case "General":
            GeneralPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            break;
          case "Engine":
            EnginePanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            break;
          case "Audio":
            AudioPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            break;
          case "Timeline":
            TimelinePanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            break;
          case "Backend":
            BackendPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            break;
          case "Performance":
            PerformancePanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            break;
          case "Plugins":
            PluginsPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            break;
          case "MCP":
            McpPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            break;
          case "Privacy":
            PrivacyPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            break;
          case "System":
            SystemPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            break;
        }

        // Update button states
        GeneralCategory.IsEnabled = category != "General";
        EngineCategory.IsEnabled = category != "Engine";
        AudioCategory.IsEnabled = category != "Audio";
        TimelineCategory.IsEnabled = category != "Timeline";
        BackendCategory.IsEnabled = category != "Backend";
        PerformanceCategory.IsEnabled = category != "Performance";
        PluginsCategory.IsEnabled = category != "Plugins";
        McpCategory.IsEnabled = category != "MCP";
        PrivacyCategory.IsEnabled = category != "Privacy";
        SystemCategory.IsEnabled = category != "System";
      }
    }

    private void CategoryButton_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is Button button && button.Tag is string category && _contextMenuService != null)
      {
        e.Handled = true;
        var menu = new MenuFlyout();

        var refreshItem = new MenuFlyoutItem { Text = "Refresh Settings" };
        refreshItem.Click += async (_, _) =>
        {
          if (ViewModel.LoadSettingsCommand.CanExecute(null))
          {
            await ViewModel.LoadSettingsCommand.ExecuteAsync(null);
            _toastService?.ShowToast(ToastType.Success, "Refreshed", $"{category} settings refreshed");
          }
        };
        menu.Items.Add(refreshItem);

        var resetAllItem = new MenuFlyoutItem { Text = "Reset All to Defaults" };
        resetAllItem.Click += async (_, _) =>
        {
          if (ViewModel.ResetSettingsCommand?.CanExecute(null) == true)
          {
            await ViewModel.ResetSettingsCommand.ExecuteAsync(null);
            _toastService?.ShowToast(ToastType.Success, "Reset", "All settings reset to defaults");
          }
          else
          {
            _toastService?.ShowToast(ToastType.Info, "Reset", "Reset functionality not available");
          }
        };
        menu.Items.Add(resetAllItem);

        var saveItem = new MenuFlyoutItem { Text = "Save Settings" };
        saveItem.Click += async (_, _) =>
        {
          if (ViewModel.SaveSettingsCommand.CanExecute(null))
          {
            await ViewModel.SaveSettingsCommand.ExecuteAsync(null);
            _toastService?.ShowToast(ToastType.Success, "Saved", "Settings saved successfully");
          }
        };
        menu.Items.Add(saveItem);

        var position = e.GetPosition(button);
        _contextMenuService.ShowContextMenu(menu, button, position);
      }
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Settings Help";
      HelpOverlay.HelpText = "The Settings panel allows you to configure all aspects of VoiceStudio. Use the category buttons on the left to navigate between different setting groups. Changes are saved to both the backend API and local storage for redundancy.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+S", Description = "Save settings" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+R", Description = "Reset to defaults" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+L", Description = "Load settings" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Settings are automatically saved to local storage as a backup.");
      HelpOverlay.Tips.Add("Changes are marked with an indicator when you have unsaved changes.");
      HelpOverlay.Tips.Add("Use 'Reset to Defaults' to restore all settings to their original values.");
      HelpOverlay.Tips.Add("Backend settings allow you to configure API connection parameters.");
      HelpOverlay.Tips.Add("Performance settings can help optimize the application for your system.");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }
  }
}