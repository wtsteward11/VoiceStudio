using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services.UndoableActions;
using System;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// MultiVoiceGeneratorView panel - Generate multiple voice synthesis jobs simultaneously.
  /// </summary>
  public sealed partial class MultiVoiceGeneratorView : Microsoft.UI.Xaml.Controls.UserControl
  {
    public MultiVoiceGeneratorViewModel ViewModel { get; }
    private ToastNotificationService? _toastService;
    private ContextMenuService? _contextMenuService;
    private UndoRedoService? _undoRedoService;

    public MultiVoiceGeneratorView()
    {
      this.InitializeComponent();
      ViewModel = new MultiVoiceGeneratorViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IMultiVoiceGeneratorClient>()
      );
      DataContext = ViewModel;

      // Initialize services
      _toastService = ServiceProvider.GetToastNotificationService();
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(MultiVoiceGeneratorViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Multi-Voice Generator Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(MultiVoiceGeneratorViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Multi-Voice Generator", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += MultiVoiceGeneratorView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void MultiVoiceGeneratorView_KeyboardNavigation_Loaded(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Multi-Voice Generator Help";
      HelpOverlay.HelpText = "The Multi-Voice Generator panel allows you to generate multiple voice synthesis jobs simultaneously with different voices, texts, and settings. This is essential for batch processing and A/B testing. Add items to the queue manually or import from CSV. Each item can have different voice profiles, text, engines, quality modes, and emotions. Start a generation job to process all items in the queue. View results in grid, list, or comparison mode. Export results to CSV for further analysis. Compare multiple voices to find the best quality.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+A", Description = "Add item to queue" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+I", Description = "Import CSV" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+E", Description = "Export CSV" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+S", Description = "Start generation" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Maximum 20 items allowed per generation job");
      HelpOverlay.Tips.Add("CSV import supports columns: Profile ID, Text, Engine, Quality Mode, Language, Emotion");
      HelpOverlay.Tips.Add("Use different engines and quality modes to compare results");
      HelpOverlay.Tips.Add("Grid view shows results in a compact format");
      HelpOverlay.Tips.Add("List view provides detailed information for each result");
      HelpOverlay.Tips.Add("Comparison mode helps identify the best quality voice");
      HelpOverlay.Tips.Add("Export results to CSV for batch analysis");
      HelpOverlay.Tips.Add("Job progress updates automatically during generation");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void QueueItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var item = element.DataContext ?? listView.SelectedItem;
        if (item != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var editItem = new MenuFlyoutItem { Text = "Edit" };
            editItem.Click += async (_, __) => await HandleQueueItemMenuClick("Edit", item);
            menu.Items.Add(editItem);

            var duplicateItem = new MenuFlyoutItem { Text = "Duplicate" };
            duplicateItem.Click += async (_, __) => await HandleQueueItemMenuClick("Duplicate", item);
            menu.Items.Add(duplicateItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var removeItem = new MenuFlyoutItem { Text = "Remove" };
            removeItem.Click += async (_, __) => await HandleQueueItemMenuClick("Remove", item);
            menu.Items.Add(removeItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private void Result_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var result = element.DataContext ?? listView.SelectedItem;
        if (result != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var playItem = new MenuFlyoutItem { Text = "Play" };
            playItem.Click += async (_, __) => await HandleResultMenuClick("Play", result);
            menu.Items.Add(playItem);

            var exportItem = new MenuFlyoutItem { Text = "Export" };
            exportItem.Click += async (_, __) => await HandleResultMenuClick("Export", result);
            menu.Items.Add(exportItem);

            var duplicateItem = new MenuFlyoutItem { Text = "Duplicate" };
            duplicateItem.Click += async (_, __) => await HandleResultMenuClick("Duplicate", result);
            menu.Items.Add(duplicateItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, __) => await HandleResultMenuClick("Delete", result);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private async System.Threading.Tasks.Task HandleQueueItemMenuClick(string action, object item)
    {
      try
      {
        switch (action.ToLower())
        {
          case "edit":
            ViewModel.SelectedQueueItem = (VoiceGenerationItem)item;
            _toastService?.ShowToast(ToastType.Info, "Edit", "Queue item selected for editing");
            break;
          case "duplicate":
            DuplicateQueueItem(item);
            break;
          case "remove":
            var dialog = new ContentDialog
            {
              Title = "Remove Queue Item",
              Content = "Are you sure you want to remove this item from the generation queue?",
              PrimaryButtonText = "Remove",
              CloseButtonText = "Cancel",
              DefaultButton = ContentDialogButton.Close,
              XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
              var itemToRemove = (VoiceGenerationItem)item;
              var itemIndex = ViewModel.GenerationQueue.IndexOf(itemToRemove);

              ViewModel.GenerationQueue.Remove(itemToRemove);

              // Register undo action
              if (_undoRedoService != null && itemIndex >= 0)
              {
                var actionObj = new SimpleAction(
                    "Remove Queue Item",
                    () => ViewModel.GenerationQueue.Insert(itemIndex, itemToRemove),
                    () => ViewModel.GenerationQueue.Remove(itemToRemove));
                _undoRedoService.RegisterAction(actionObj);
              }

              _toastService?.ShowToast(ToastType.Success, "Removed", "Item removed from queue");
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private async System.Threading.Tasks.Task HandleResultMenuClick(string action, object result)
    {
      try
      {
        switch (action.ToLower())
        {
          case "play":
            _toastService?.ShowToast(ToastType.Info, "Play", "Playing result...");
            break;
          case "export":
            await ExportResultAsync(result);
            break;
          case "duplicate":
            DuplicateResult(result);
            break;
          case "delete":
            var dialog = new ContentDialog
            {
              Title = "Delete Result",
              Content = "Are you sure you want to delete this generation result? This action cannot be undone.",
              PrimaryButtonText = "Delete",
              CloseButtonText = "Cancel",
              DefaultButton = ContentDialogButton.Close,
              XamlRoot = this.XamlRoot
            };

            var dialogResult = await dialog.ShowAsync();
            if (dialogResult == ContentDialogResult.Primary)
            {
              var resultToDelete = (VoiceGenerationResultItem)result;
              var resultIndex = ViewModel.Results.IndexOf(resultToDelete);

              ViewModel.Results.Remove(resultToDelete);

              // Register undo action
              if (_undoRedoService != null && resultIndex >= 0)
              {
                var actionObj = new SimpleAction(
                    "Delete Generation Result",
                    () => ViewModel.Results.Insert(resultIndex, resultToDelete),
                    () => ViewModel.Results.Remove(resultToDelete));
                _undoRedoService.RegisterAction(actionObj);
              }

              _toastService?.ShowToast(ToastType.Success, "Deleted", "Result deleted");
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private void DuplicateQueueItem(object item)
    {
      try
      {
        var itemType = item.GetType();
        var duplicatedItem = Activator.CreateInstance(itemType);
        if (duplicatedItem != null)
        {
          // Copy properties from original item
          foreach (var prop in itemType.GetProperties())
          {
            if (prop.CanRead && prop.CanWrite && prop.GetIndexParameters().Length == 0)
            {
              var value = prop.GetValue(item);
              prop.SetValue(duplicatedItem, value);
            }
          }

          var index = ViewModel.GenerationQueue.IndexOf((VoiceGenerationItem)item);
          ViewModel.GenerationQueue.Insert(index + 1, (VoiceGenerationItem)duplicatedItem);
          _toastService?.ShowToast(ToastType.Success, "Duplicated", "Queue item duplicated");
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Duplicate Failed", ex.Message);
      }
    }

    private void DuplicateResult(object result)
    {
      try
      {
        var resultType = result.GetType();
        var duplicatedResult = Activator.CreateInstance(resultType);
        if (duplicatedResult != null)
        {
          // Copy properties from original result
          foreach (var prop in resultType.GetProperties())
          {
            if (prop.CanRead && prop.CanWrite && prop.GetIndexParameters().Length == 0)
            {
              var value = prop.GetValue(result);
              prop.SetValue(duplicatedResult, value);
            }
          }

          var index = ViewModel.Results.IndexOf((VoiceGenerationResultItem)result);
          ViewModel.Results.Insert(index + 1, (VoiceGenerationResultItem)duplicatedResult);
          _toastService?.ShowToast(ToastType.Success, "Duplicated", "Result duplicated");
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Duplicate Failed", ex.Message);
      }
    }

    private async System.Threading.Tasks.Task ExportResultAsync(object result)
    {
      try
      {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("JSON", new[] { ".json" });
        picker.FileTypeChoices.Add("CSV", new[] { ".csv" });
        picker.SuggestedFileName = "generation_result_export";

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
          var extension = file.FileType.ToLower();
          var resultType = result.GetType();
          string content;

          if (extension == ".json")
          {
            var jsonData = new
            {
              Id = resultType.GetProperty("Id")?.GetValue(result)?.ToString() ?? "unknown",
              Status = resultType.GetProperty("Status")?.GetValue(result)?.ToString() ?? "unknown",
              Created = resultType.GetProperty("Created")?.GetValue(result)?.ToString() ?? DateTime.UtcNow.ToString()
            };
            content = System.Text.Json.JsonSerializer.Serialize(jsonData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
          }
          else
          {
            content = "Id,Status,Created\n";
            content += $"\"{resultType.GetProperty("Id")?.GetValue(result)?.ToString() ?? "unknown"}\",";
            content += $"\"{resultType.GetProperty("Status")?.GetValue(result)?.ToString() ?? "unknown"}\",";
            content += $"\"{resultType.GetProperty("Created")?.GetValue(result)?.ToString() ?? DateTime.UtcNow.ToString()}\"";
          }

          await Windows.Storage.FileIO.WriteTextAsync(file, content);
          _toastService?.ShowToast(ToastType.Success, "Export", "Generation result exported successfully");
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Export Failed", ex.Message);
      }
    }
  }
}