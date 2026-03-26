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
  /// BackupRestoreView panel for backup and restore functionality.
  /// </summary>
  public sealed partial class BackupRestoreView : UserControl
  {
    public BackupRestoreViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;

    public BackupRestoreView()
    {
      this.InitializeComponent();
      ViewModel = new BackupRestoreViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          ServiceProvider.GetBackupRestoreClient(),
          AppServices.TryGetEventAggregator());
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(BackupRestoreViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Backup & Restore Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(BackupRestoreViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Backup & Restore", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += BackupRestoreView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void BackupRestoreView_KeyboardNavigation_Loaded(object sender, RoutedEventArgs e)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Backup & Restore Help";
      HelpOverlay.HelpText = "The Backup & Restore panel allows you to create backups of your VoiceStudio data and restore from previous backups. Backups can include voice profiles, projects, settings, and trained models. Use backups to protect your work or transfer data between installations.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh backup list" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+B", Description = "Create backup" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Escape", Description = "Close help" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Create regular backups to protect your work");
      HelpOverlay.Tips.Add("Backups including models can be very large - consider excluding them unless needed");
      HelpOverlay.Tips.Add("You can restore specific components (profiles, projects, settings) from a backup");
      HelpOverlay.Tips.Add("Upload backups created on other systems to restore them here");
      HelpOverlay.Tips.Add("Backups are stored locally and can be manually copied for safekeeping");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void Backup_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var backupObj = (element.DataContext ?? listView.SelectedItem) as BackupRestoreViewModel.BackupItem;
        if (backupObj != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var restoreItem = new MenuFlyoutItem { Text = "Restore" };
            restoreItem.Click += async (_, _) => await HandleBackupMenuClick("Restore", backupObj);
            menu.Items.Add(restoreItem);

            var downloadItem = new MenuFlyoutItem { Text = "Download" };
            downloadItem.Click += async (_, _) => await HandleBackupMenuClick("Download", backupObj);
            menu.Items.Add(downloadItem);

            var duplicateItem = new MenuFlyoutItem { Text = "Duplicate" };
            duplicateItem.Click += async (_, _) => await HandleBackupMenuClick("Duplicate", backupObj);
            menu.Items.Add(duplicateItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, _) => await HandleBackupMenuClick("Delete", backupObj);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private async System.Threading.Tasks.Task HandleBackupMenuClick(string action, BackupRestoreViewModel.BackupItem backup)
    {
      try
      {
        switch (action.ToLower())
        {
          case "restore":
            _toastService?.ShowToast(ToastType.Info, "Restore", "Restoring backup...");
            break;
          case "download":
            _toastService?.ShowToast(ToastType.Info, "Download", "Downloading backup...");
            break;
          case "duplicate":
            DuplicateBackup(backup);
            break;
          case "delete":
            var dialog = new ContentDialog
            {
              Title = "Delete Backup",
              Content = "Are you sure you want to delete this backup? This action cannot be undone.",
              PrimaryButtonText = "Delete",
              CloseButtonText = "Cancel",
              DefaultButton = ContentDialogButton.Close,
              XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
              var backupToDelete = backup;
              var backupIndex = ViewModel.Backups.IndexOf(backupToDelete);

              ViewModel.Backups.Remove(backupToDelete);

              // Register undo action
              if (_undoRedoService != null && backupIndex >= 0)
              {
                var actionObj = new SimpleAction(
                    "Delete Backup",
                    () => ViewModel.Backups.Insert(backupIndex, backupToDelete),
                    () => ViewModel.Backups.Remove(backupToDelete));
                _undoRedoService.RegisterAction(actionObj);
              }

              _toastService?.ShowToast(ToastType.Success, "Deleted", "Backup deleted");
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private void DuplicateBackup(BackupRestoreViewModel.BackupItem backup)
    {
      try
      {
        if (backup == null)
        {
          _toastService?.ShowToast(ToastType.Error, "Duplicate Failed", "Invalid backup item");
          return;
        }

        var backupType = backup.GetType();
        var duplicatedBackup = Activator.CreateInstance(backupType);
        if (duplicatedBackup != null)
        {
          foreach (var prop in backupType.GetProperties())
          {
            if (prop.CanRead && prop.CanWrite && prop.GetIndexParameters().Length == 0)
            {
              var value = prop.GetValue(backup);
              if (prop.Name == "Name")
              {
                prop.SetValue(duplicatedBackup, $"{value} (Copy)");
              }
              else
              {
                prop.SetValue(duplicatedBackup, value);
              }
            }
          }

          var index = ViewModel.Backups.IndexOf(backup);
          ViewModel.Backups.Insert(index + 1, (BackupRestoreViewModel.BackupItem)duplicatedBackup);
          _toastService?.ShowToast(ToastType.Success, "Duplicated", "Backup duplicated");
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Duplicate Failed", ex.Message);
      }
    }
  }
}