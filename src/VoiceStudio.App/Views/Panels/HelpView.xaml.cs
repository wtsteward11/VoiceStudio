using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// HelpView panel for help system and documentation.
  /// </summary>
  public sealed partial class HelpView : UserControl
  {
    public HelpViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;

    public HelpView()
    {
      this.InitializeComponent();
      ViewModel = new HelpViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetHelpClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(HelpViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Help Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(HelpViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Help", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += HelpView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void HelpView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
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

    private void TopicsListView_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && _contextMenuService != null)
      {
        var topic = (e.OriginalSource as FrameworkElement)?.DataContext ?? listView.SelectedItem;
        if (topic is ViewModels.HelpTopic helpTopic)
        {
          e.Handled = true;
          var menu = new MenuFlyout();

          var copyTitleItem = new MenuFlyoutItem { Text = "Copy Topic Title" };
          copyTitleItem.Click += (_, __) =>
          {
            if (!string.IsNullOrEmpty(helpTopic.Title))
            {
              var dataPackage = new DataPackage();
              dataPackage.SetText(helpTopic.Title);
              Clipboard.SetContent(dataPackage);
              _toastService?.ShowToast(ToastType.Success, "Copied", "Topic title copied");
            }
          };
          menu.Items.Add(copyTitleItem);

          var refreshItem = new MenuFlyoutItem { Text = "Refresh Topics" };
          refreshItem.Click += (_, __) =>
          {
            if (ViewModel.RefreshCommand.CanExecute(null))
            {
              ViewModel.RefreshCommand.Execute(null);
              _toastService?.ShowToast(ToastType.Success, "Refreshed", "Help topics refreshed");
            }
          };
          menu.Items.Add(refreshItem);

          var position = e.GetPosition(listView);
          _contextMenuService.ShowContextMenu(menu, listView, position);
        }
      }
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Help System";
      HelpOverlay.HelpText = "The Help panel provides access to documentation, tutorials, and support resources. Browse user guides, tutorials, keyboard shortcuts, and troubleshooting information. Use the search to find specific topics quickly.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+F", Description = "Search help content" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Escape", Description = "Close help" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Use the search box to quickly find help topics");
      HelpOverlay.Tips.Add("Browse tutorials for step-by-step guides");
      HelpOverlay.Tips.Add("Check keyboard shortcuts for productivity tips");
      HelpOverlay.Tips.Add("Troubleshooting section helps resolve common issues");
      HelpOverlay.Tips.Add("Each panel has its own help button (?) for contextual help");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }
  }
}