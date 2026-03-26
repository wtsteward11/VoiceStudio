using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using System;
using Windows.Foundation;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// SceneBuilderView panel for creating and managing scene compositions.
  /// </summary>
  public sealed partial class SceneBuilderView : UserControl
  {
    public SceneBuilderViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;
    private DragDropVisualFeedbackService? _dragDropService;
    private object? _draggedScene;

    public SceneBuilderView()
    {
      this.InitializeComponent();
      ViewModel = new SceneBuilderViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          ServiceProvider.GetSceneBuilderClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();
      _dragDropService = ServiceProvider.GetDragDropVisualFeedbackService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(SceneBuilderViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Scene Builder Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(SceneBuilderViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Scene Builder", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += SceneBuilderView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void SceneBuilderView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Scene Builder Help";
      HelpOverlay.HelpText = "The Scene Builder allows you to create and manage scene compositions. Build complex audio scenes with multiple tracks, effects, and automation. Organize scenes into projects, preview compositions, and export finished work.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+N", Description = "Create new scene" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+S", Description = "Save scene" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Escape", Description = "Close help" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Scenes can contain multiple tracks with different audio sources");
      HelpOverlay.Tips.Add("Apply effects and automation to individual tracks or the master");
      HelpOverlay.Tips.Add("Organize scenes into projects for better workflow management");
      HelpOverlay.Tips.Add("Preview scenes before exporting to ensure quality");
      HelpOverlay.Tips.Add("Export scenes in various formats for different use cases");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void Scene_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var scene = element.DataContext ?? listView.SelectedItem;
        if (scene != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var editItem = new MenuFlyoutItem { Text = "Edit" };
            editItem.Click += async (_, _) => await HandleSceneMenuClick("Edit", scene);
            menu.Items.Add(editItem);

            var previewItem = new MenuFlyoutItem { Text = "Preview" };
            previewItem.Click += async (_, _) => await HandleSceneMenuClick("Preview", scene);
            menu.Items.Add(previewItem);

            var duplicateItem = new MenuFlyoutItem { Text = "Duplicate" };
            duplicateItem.Click += async (_, _) => await HandleSceneMenuClick("Duplicate", scene);
            menu.Items.Add(duplicateItem);

            var exportItem = new MenuFlyoutItem { Text = "Export" };
            exportItem.Click += async (_, _) => await HandleSceneMenuClick("Export", scene);
            menu.Items.Add(exportItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, _) => await HandleSceneMenuClick("Delete", scene);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private async System.Threading.Tasks.Task HandleSceneMenuClick(string action, object scene)
    {
      try
      {
        switch (action.ToLower())
        {
          case "edit":
            ViewModel.SelectedScene = (SceneItem)scene;
            _toastService?.ShowToast(ToastType.Info, "Edit Scene", "Scene selected for editing");
            break;
          case "preview":
            _toastService?.ShowToast(ToastType.Info, "Preview", "Previewing scene");
            break;
          case "duplicate":
            DuplicateScene(scene);
            break;
          case "export":
            await ExportSceneAsync(scene);
            break;
          case "delete":
            var dialog = new ContentDialog
            {
              Title = "Delete Scene",
              Content = "Are you sure you want to delete this scene? This action cannot be undone.",
              PrimaryButtonText = "Delete",
              CloseButtonText = "Cancel",
              DefaultButton = ContentDialogButton.Close,
              XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
              var sceneToDelete = (SceneItem)scene;
              var sceneIndex = ViewModel.Scenes.IndexOf(sceneToDelete);

              ViewModel.Scenes.Remove(sceneToDelete);

              // Register undo action
              if (_undoRedoService != null && sceneIndex >= 0)
              {
                var actionObj = new SimpleAction(
                    "Delete Scene",
                    () => ViewModel.Scenes.Insert(sceneIndex, sceneToDelete),
                    () => ViewModel.Scenes.Remove(sceneToDelete));
                _undoRedoService.RegisterAction(actionObj);
              }

              _toastService?.ShowToast(ToastType.Success, "Deleted", "Scene deleted");
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    // Drag-and-drop handlers for scene reordering
    private void Scene_DragStarting(UIElement sender, DragStartingEventArgs e)
    {
      if (sender is ListViewItem listViewItem && listViewItem.DataContext is SceneItem scene)
      {
        _draggedScene = scene;

        // Set drag data
        e.Data.SetText(scene.Id);
        e.Data.Properties.Add("SceneId", scene.Id);
        e.Data.Properties.Add("SceneName", scene.Name ?? "Unnamed Scene");

        // Reduce opacity of source element
        listViewItem.Opacity = 0.5;
      }
    }

    private void Scene_DragItemsCompleted(UIElement sender, DragItemsCompletedEventArgs e)
    {
      // Clean up drag state
      if (sender is ListViewItem listViewItem)
      {
        listViewItem.Opacity = 1.0;
      }

      _dragDropService?.Cleanup();

      _draggedScene = null;
    }

    private void Scene_DragOver(object sender, DragEventArgs e)
    {
      if (sender is ListViewItem listViewItem && _dragDropService != null)
      {
        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsGlyphVisible = false;
        e.DragUIOverride.IsContentVisible = false;

        // Show drop target indicator
        var position = e.GetPosition(listViewItem);
        var dropPosition = DetermineSceneDropPosition(listViewItem, position);
        _dragDropService.ShowDropTargetIndicator(listViewItem, dropPosition);
      }
    }

    private void Scene_Drop(object sender, DragEventArgs e)
    {
      if (sender is ListViewItem listViewItem && _draggedScene != null && _dragDropService != null)
      {
        e.AcceptedOperation = DataPackageOperation.Move;

        // Hide drop indicator
        _dragDropService.HideDropTargetIndicator();
        _dragDropService.Cleanup();

        // Get target scene
        if (listViewItem.DataContext is SceneItem targetScene && _draggedScene is SceneItem draggedScene)
        {
          var draggedIndex = ViewModel.Scenes.IndexOf(draggedScene);
          var targetIndex = ViewModel.Scenes.IndexOf(targetScene);

          if (draggedIndex >= 0 && targetIndex >= 0 && draggedIndex != targetIndex)
          {
            // Determine drop position
            var position = e.GetPosition(listViewItem);
            var dropPosition = DetermineSceneDropPosition(listViewItem, position);

            // Reorder scenes in the collection
            ViewModel.Scenes.RemoveAt(draggedIndex);

            if (dropPosition == DropPosition.Before)
            {
              ViewModel.Scenes.Insert(targetIndex, draggedScene);
            }
            else if (dropPosition == DropPosition.After)
            {
              var newIndex = targetIndex < draggedIndex ? targetIndex + 1 : targetIndex;
              ViewModel.Scenes.Insert(newIndex, draggedScene);
            }
            else
            {
              // On - replace target
              ViewModel.Scenes.Insert(targetIndex, draggedScene);
            }

            _toastService?.ShowToast(ToastType.Success, "Reordered", $"Moved '{draggedScene.Name}' in scene list");
          }
        }

        // Clean up drag state
        _draggedScene = null;

        // Restore source element opacity
        if (e.OriginalSource is ListViewItem sourceItem)
        {
          sourceItem.Opacity = 1.0;
        }
      }
    }

    private void Scene_DragLeave(object _, DragEventArgs __)
    {
      _dragDropService?.HideDropTargetIndicator();
    }

    private DropPosition DetermineSceneDropPosition(ListViewItem target, Point position)
    {
      // Determine if drop is before, after, or on the target
      var targetHeight = target.ActualHeight;
      var relativeY = position.Y;

      if (relativeY < targetHeight * 0.33)
        return DropPosition.Before;
      else if (relativeY > targetHeight * 0.67)
        return DropPosition.After;
      else
        return DropPosition.On;
    }

    private void DuplicateScene(object scene)
    {
      try
      {
        var sceneType = scene.GetType();
        var sceneName = sceneType.GetProperty("Name")?.GetValue(scene)?.ToString() ?? "scene";
        var sceneId = sceneType.GetProperty("Id")?.GetValue(scene)?.ToString() ?? Guid.NewGuid().ToString();

        var duplicatedScene = Activator.CreateInstance(sceneType);
        if (duplicatedScene != null)
        {
          var nameProp = sceneType.GetProperty("Name");
          if (nameProp?.CanWrite == true)
          {
            nameProp.SetValue(duplicatedScene, $"{sceneName} (Copy)");
          }

          var idProp = sceneType.GetProperty("Id");
          if (idProp?.CanWrite == true)
          {
            idProp.SetValue(duplicatedScene, Guid.NewGuid().ToString());
          }

          var index = ViewModel.Scenes.IndexOf((SceneItem)scene);
          ViewModel.Scenes.Insert(index + 1, (SceneItem)duplicatedScene);
          _toastService?.ShowToast(ToastType.Success, "Duplicated", "Scene duplicated");
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Duplicate Failed", ex.Message);
      }
    }

    private async System.Threading.Tasks.Task ExportSceneAsync(object scene)
    {
      try
      {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("JSON", new[] { ".json" });
        picker.SuggestedFileName = "scene_export";

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
          var sceneType = scene.GetType();
          var jsonData = new
          {
            Id = sceneType.GetProperty("Id")?.GetValue(scene)?.ToString() ?? "unknown",
            Name = sceneType.GetProperty("Name")?.GetValue(scene)?.ToString() ?? "unknown",
            Created = sceneType.GetProperty("Created")?.GetValue(scene)?.ToString() ?? DateTime.UtcNow.ToString()
          };
          var content = System.Text.Json.JsonSerializer.Serialize(jsonData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
          await Windows.Storage.FileIO.WriteTextAsync(file, content);
          _toastService?.ShowToast(ToastType.Success, "Export", "Scene exported successfully");
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Export Failed", ex.Message);
      }
    }
  }
}