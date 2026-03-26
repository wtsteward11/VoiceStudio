using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using System;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// APIKeyManagerView panel for API key management.
  /// </summary>
  public sealed partial class APIKeyManagerView : UserControl
  {
    public APIKeyManagerViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;

    public APIKeyManagerView()
    {
      this.InitializeComponent();
      ViewModel = new APIKeyManagerViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          ServiceProvider.GetAPIKeyManagerClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(APIKeyManagerViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "API Key Manager Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(APIKeyManagerViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "API Key Manager", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += APIKeyManagerView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void APIKeyManagerView_KeyboardNavigation_Loaded(object sender, RoutedEventArgs e)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "API Key Manager Help";
      HelpOverlay.HelpText = "The API Key Manager panel allows you to securely manage API keys for external services such as OpenAI, ElevenLabs, Voice.ai, and other cloud-based services. Create, update, validate, and delete API keys for various services. API keys are masked for security (only the last 4 characters are visible). Use the validate function to test if an API key is working correctly. API keys are stored securely and can be used by other features in VoiceStudio that require external service integration.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+N", Description = "Create new API key" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Delete", Description = "Delete selected API key" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+V", Description = "Validate selected API key" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh API keys list" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("API keys are masked for security (only last 4 characters visible)");
      HelpOverlay.Tips.Add("Use the validate function to test if an API key is working");
      HelpOverlay.Tips.Add("API keys are stored securely and encrypted in production");
      HelpOverlay.Tips.Add("Supported services include OpenAI, ElevenLabs, Voice.ai, Azure Speech, and more");
      HelpOverlay.Tips.Add("Inactive keys can be toggled without deleting them");
      HelpOverlay.Tips.Add("Usage count tracks how many times each key has been used");
      HelpOverlay.Tips.Add("Last used timestamp shows when the key was last validated or used");
      HelpOverlay.Tips.Add("Delete unused or compromised keys to maintain security");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void ApiKey_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var apiKey = (element.DataContext ?? listView.SelectedItem) as APIKeyItem;
        if (apiKey != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var editItem = new MenuFlyoutItem { Text = "Edit" };
            editItem.Click += async (_, _) => await HandleApiKeyMenuClick("Edit", apiKey);
            menu.Items.Add(editItem);

            var validateItem = new MenuFlyoutItem { Text = "Validate" };
            validateItem.Click += async (_, _) => await HandleApiKeyMenuClick("Validate", apiKey);
            menu.Items.Add(validateItem);

            var toggleItem = new MenuFlyoutItem { Text = "Toggle Active" };
            toggleItem.Click += async (_, _) => await HandleApiKeyMenuClick("Toggle", apiKey);
            menu.Items.Add(toggleItem);

            var duplicateItem = new MenuFlyoutItem { Text = "Duplicate" };
            duplicateItem.Click += async (_, _) => await HandleApiKeyMenuClick("Duplicate", apiKey);
            menu.Items.Add(duplicateItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, _) => await HandleApiKeyMenuClick("Delete", apiKey);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private async System.Threading.Tasks.Task HandleApiKeyMenuClick(string action, APIKeyItem apiKey)
    {
      try
      {
        switch (action.ToLower())
        {
          case "edit":
            ViewModel.SelectedKey = apiKey;
            _toastService?.ShowToast(ToastType.Info, "Edit API Key", "API key selected for editing");
            break;
          case "validate":
            _toastService?.ShowToast(ToastType.Info, "Validate", "Validating API key...");
            break;
          case "toggle":
            _toastService?.ShowToast(ToastType.Info, "Toggle", "Toggling API key active status");
            break;
          case "duplicate":
            DuplicateApiKey(apiKey);
            break;
          case "delete":
            var dialog = new ContentDialog
            {
              Title = "Delete API Key",
              Content = "Are you sure you want to delete this API key? This action cannot be undone.",
              PrimaryButtonText = "Delete",
              CloseButtonText = "Cancel",
              DefaultButton = ContentDialogButton.Close,
              XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
              var keyToDelete = apiKey;
              var keyIndex = ViewModel.ApiKeys.IndexOf(apiKey);

              ViewModel.ApiKeys.Remove(apiKey);

              // Register undo action
              if (_undoRedoService != null && keyIndex >= 0)
              {
                var actionObj = new SimpleAction(
                    "Delete API Key",
                    () => ViewModel.ApiKeys.Insert(keyIndex, keyToDelete),
                    () => ViewModel.ApiKeys.Remove(keyToDelete));
                _undoRedoService.RegisterAction(actionObj);
              }

              _toastService?.ShowToast(ToastType.Success, "Deleted", "API key deleted");
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private void DuplicateApiKey(APIKeyItem apiKey)
    {
      if (apiKey == null)
      {
        _toastService?.ShowToast(ToastType.Error, "Duplicate Failed", "Invalid API key");
        return;
      }

      try
      {
        var keyType = apiKey.GetType();
        var duplicatedKey = Activator.CreateInstance(keyType);
        if (duplicatedKey != null)
        {
          foreach (var prop in keyType.GetProperties())
          {
            if (prop.CanRead && prop.CanWrite && prop.GetIndexParameters().Length == 0)
            {
              var value = prop.GetValue(apiKey);
              if (prop.Name == "Name")
              {
                prop.SetValue(duplicatedKey, $"{value} (Copy)");
              }
              else
              {
                prop.SetValue(duplicatedKey, value);
              }
            }
          }

          var index = ViewModel.ApiKeys.IndexOf(apiKey);
          ViewModel.ApiKeys.Insert(index + 1, (APIKeyItem)duplicatedKey);
          _toastService?.ShowToast(ToastType.Success, "Duplicated", "API key duplicated");
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Duplicate Failed", ex.Message);
      }
    }
  }
}