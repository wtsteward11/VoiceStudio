using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.App.Controls;
using VoiceStudio.Core.Models;
using System;

namespace VoiceStudio.App.Views.Panels
{
  public sealed partial class ModelManagerView : UserControl
  {
    public ModelManagerViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;

    public ModelManagerView()
    {
      this.InitializeComponent();
      ViewModel = new ModelManagerViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IModelManagerClient>(),
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IJobProgressApiClient>()
      );
      this.DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();

      // Load models on initialization
      _ = ViewModel.LoadModelsCommand.ExecuteAsync(null);
      _ = ViewModel.LoadStorageStatsCommand.ExecuteAsync(null);

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(ModelManagerViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowError(ViewModel.ErrorMessage, "Model Manager Error");
        }
        else if (e.PropertyName == nameof(ModelManagerViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowSuccess(ViewModel.StatusMessage, "Model Manager");
        }
      };

      // Register keyboard shortcuts
      this.KeyDown += ModelManagerView_KeyDown;
    }

    private void ModelManagerView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      // Setup Tab navigation order for this panel
      KeyboardNavigationHelper.SetupTabNavigation(this, 0);
    }

    private void ModelManagerView_KeyDown(object _, KeyRoutedEventArgs e)
    {
      // Handle F5 for refresh
      if (e.Key == Windows.System.VirtualKey.F5 && ViewModel.RefreshCommand.CanExecute(null))
      {
        _ = ViewModel.RefreshCommand.ExecuteAsync(null);
        e.Handled = true;
      }

      // Handle Delete key for selected model
      if (e.Key == Windows.System.VirtualKey.Delete && ViewModel.SelectedModel != null && ViewModel.DeleteModelCommand.CanExecute(ViewModel.SelectedModel))
      {
        _ = DeleteModelAsync(ViewModel.SelectedModel);
        e.Handled = true;
      }
    }

    private void VerifyButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      if (sender is Button button && button.CommandParameter is ModelInfo model)
      {
        _ = ViewModel.VerifyModelCommand.ExecuteAsync(model);
      }
    }

    private void UpdateButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      if (sender is Button button && button.CommandParameter is ModelInfo model)
      {
        _ = ViewModel.UpdateChecksumCommand.ExecuteAsync(model);
      }
    }

    private void DeleteButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      if (sender is Button button && button.CommandParameter is ModelInfo model)
      {
        _ = DeleteModelAsync(model);
      }
    }

    private void Model_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var model = element.DataContext as ModelInfo ?? listView.SelectedItem as ModelInfo;
        if (model != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var verifyItem = new MenuFlyoutItem { Text = "Verify" };
            verifyItem.Click += async (_, __) => await HandleModelMenuClick("Verify", model);
            menu.Items.Add(verifyItem);

            var updateItem = new MenuFlyoutItem { Text = "Update Checksum" };
            updateItem.Click += async (_, __) => await HandleModelMenuClick("Update", model);
            menu.Items.Add(updateItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, __) => await HandleModelMenuClick("Delete", model);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private async System.Threading.Tasks.Task HandleModelMenuClick(string action, ModelInfo model)
    {
      try
      {
        switch (action.ToLower())
        {
          case "verify":
            if (ViewModel.VerifyModelCommand.CanExecute(model))
            {
              await ViewModel.VerifyModelCommand.ExecuteAsync(model);
              _toastService?.ShowInfo($"Verifying model '{model.ModelName}'", "Verifying");
            }
            break;
          case "update":
            if (ViewModel.UpdateChecksumCommand.CanExecute(model))
            {
              await ViewModel.UpdateChecksumCommand.ExecuteAsync(model);
              _toastService?.ShowSuccess($"Updated checksum for '{model.ModelName}'", "Updated");
            }
            break;
          case "delete":
            await DeleteModelAsync(model);
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowError($"Failed to {action}: {ex.Message}", "Error");
      }
    }

    private async System.Threading.Tasks.Task DeleteModelAsync(ModelInfo model)
    {
      try
      {
        var dialog = new ContentDialog
        {
          Title = "Delete Model",
          Content = $"Are you sure you want to delete model '{model.ModelName}'? This action cannot be undone.",
          PrimaryButtonText = "Delete",
          CloseButtonText = "Cancel",
          DefaultButton = ContentDialogButton.Close,
          XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && ViewModel.DeleteModelCommand.CanExecute(model))
        {
          var modelToDelete = model;
          var modelIndex = ViewModel.Models.IndexOf(model);

          await ViewModel.DeleteModelCommand.ExecuteAsync(model);

          // Register undo action
          if (_undoRedoService != null && modelIndex >= 0)
          {
            var actionObj = new SimpleAction(
                $"Delete Model: {model.ModelName}",
                () => ViewModel.Models.Insert(modelIndex, modelToDelete),
                () => ViewModel.Models.Remove(modelToDelete));
            _undoRedoService.RegisterAction(actionObj);
          }

          _toastService?.ShowSuccess($"Deleted model '{model.ModelName}'", "Deleted");
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowError($"Failed to delete model: {ex.Message}", "Error");
      }
    }

    private void HelpButton_Click(object _, RoutedEventArgs __)
    {
      HelpOverlay.Title = "Model Manager Help";
      HelpOverlay.HelpText = "The Model Manager panel allows you to manage AI models used by different engines (XTTS v2, Chatterbox, Tortoise, Piper, OpenVoice, SDXL, RealESRGAN, SVD). View all installed models, their status, size, and storage statistics. Import new models, verify model integrity, delete unused models, and filter models by engine type. Monitor storage usage to manage disk space effectively.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh models list" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+I", Description = "Import model" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Delete", Description = "Delete selected model" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Escape", Description = "Close help" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Filter models by engine type to see only relevant models");
      HelpOverlay.Tips.Add("Verify models to ensure they are not corrupted");
      HelpOverlay.Tips.Add("Monitor storage statistics to manage disk space");
      HelpOverlay.Tips.Add("Delete unused models to free up storage space");
      HelpOverlay.Tips.Add("Import models from files or download from repositories");
      HelpOverlay.Tips.Add("Models are organized by engine type for easy management");
      HelpOverlay.Tips.Add("Check model status to see which models are ready to use");
      HelpOverlay.Tips.Add("Storage stats show total used space and available space");

      HelpOverlay.Visibility = Visibility.Visible;
      HelpOverlay.Show();
    }
  }
}