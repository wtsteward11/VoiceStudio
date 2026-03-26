using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// KeyboardShortcutsView panel for managing keyboard shortcuts.
  /// </summary>
  public sealed partial class KeyboardShortcutsView : UserControl
  {
    public KeyboardShortcutsViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;

    public KeyboardShortcutsView()
    {
      this.InitializeComponent();
      ViewModel = new KeyboardShortcutsViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetKeyboardShortcutsClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(KeyboardShortcutsViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Keyboard Shortcuts Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(KeyboardShortcutsViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Keyboard Shortcuts", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += KeyboardShortcutsView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void KeyboardShortcutsView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void SearchTextBox_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is TextBox textBox && _contextMenuService != null)
      {
        e.Handled = true;
        var menu = new MenuFlyout();

        var pasteItem = new MenuFlyoutItem { Text = "Paste" };
        pasteItem.Click += (_, __) =>
        {
          var clipboard = Clipboard.GetContent();
          if (clipboard.Contains(StandardDataFormats.Text))
          {
            _ = clipboard.GetTextAsync().AsTask().ContinueWith(task =>
                    {
                      if (task.IsCompletedSuccessfully)
                      {
                        DispatcherQueue.TryEnqueue(() =>
                                {
                                  textBox.Text = task.Result;
                                  _toastService?.ShowToast(ToastType.Success, "Pasted", "Search text pasted");
                                });
                      }
                    });
          }
        };
        menu.Items.Add(pasteItem);

        var copyItem = new MenuFlyoutItem { Text = "Copy" };
        copyItem.Click += (_, __) =>
        {
          if (!string.IsNullOrEmpty(textBox.Text))
          {
            var dataPackage = new DataPackage();
            dataPackage.SetText(textBox.Text);
            Clipboard.SetContent(dataPackage);
            _toastService?.ShowToast(ToastType.Success, "Copied", "Search text copied");
          }
        };
        copyItem.IsEnabled = !string.IsNullOrEmpty(textBox.Text);
        menu.Items.Add(copyItem);

        var clearItem = new MenuFlyoutItem { Text = "Clear" };
        clearItem.Click += (_, __) =>
        {
          textBox.Text = string.Empty;
          _toastService?.ShowToast(ToastType.Info, "Cleared", "Search cleared");
        };
        clearItem.IsEnabled = !string.IsNullOrEmpty(textBox.Text);
        menu.Items.Add(clearItem);

        var position = e.GetPosition(textBox);
        _contextMenuService.ShowContextMenu(menu, textBox, position);
      }
    }

    private void ShortcutsListView_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && _contextMenuService != null)
      {
        var shortcut = (e.OriginalSource as FrameworkElement)?.DataContext ?? listView.SelectedItem;
        if (shortcut is ViewModels.ShortcutItem shortcutItem)
        {
          e.Handled = true;
          var menu = new MenuFlyout();

          var copyKeyItem = new MenuFlyoutItem { Text = "Copy Shortcut Key" };
          copyKeyItem.Click += (_, __) =>
          {
            if (!string.IsNullOrEmpty(shortcutItem.Key))
            {
              var dataPackage = new DataPackage();
              dataPackage.SetText(shortcutItem.Key);
              Clipboard.SetContent(dataPackage);
              _toastService?.ShowToast(ToastType.Success, "Copied", "Shortcut key copied");
            }
          };
          menu.Items.Add(copyKeyItem);

          var editItem = new MenuFlyoutItem { Text = "Edit" };
          editItem.Click += (_, __) =>
          {
            if (ViewModel.StartEditCommand.CanExecute(shortcutItem))
            {
              ViewModel.StartEditCommand.Execute(shortcutItem);
              _toastService?.ShowToast(ToastType.Info, "Editing", "Edit shortcut dialog opened");
            }
          };
          menu.Items.Add(editItem);

          var resetItem = new MenuFlyoutItem { Text = "Reset to Default" };
          resetItem.Click += (_, __) =>
          {
            if (ViewModel.ResetShortcutCommand.CanExecute(shortcutItem))
            {
              ViewModel.ResetShortcutCommand.Execute(shortcutItem);
              _toastService?.ShowToast(ToastType.Success, "Reset", "Shortcut reset to default");
            }
          };
          resetItem.IsEnabled = shortcutItem.IsCustom;
          menu.Items.Add(resetItem);

          var position = e.GetPosition(listView);
          _contextMenuService.ShowContextMenu(menu, listView, position);
        }
      }
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Keyboard Shortcuts Help";
      HelpOverlay.HelpText = "The Keyboard Shortcuts panel allows you to view, edit, and customize all keyboard shortcuts in VoiceStudio. Search for shortcuts, filter by category or panel, and customize shortcuts to match your workflow. Changes are saved automatically.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+F", Description = "Focus search" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Escape", Description = "Close help" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Click 'Edit' on any shortcut to customize it");
      HelpOverlay.Tips.Add("Shortcuts can be filtered by category (General, Editing, Navigation, etc.)");
      HelpOverlay.Tips.Add("Filter by panel to see shortcuts specific to that panel");
      HelpOverlay.Tips.Add("Custom shortcuts are marked and can be reset to defaults");
      HelpOverlay.Tips.Add("Some shortcuts may conflict - the system will warn you");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }
  }
}