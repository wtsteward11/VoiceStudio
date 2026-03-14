using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.Core.Models;
using System;
using Windows.Foundation;
using Windows.ApplicationModel.DataTransfer;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// TemplateLibraryView panel for managing project templates.
  /// </summary>
  public sealed partial class TemplateLibraryView : UserControl
  {
    public TemplateLibraryViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;
    private DragDropVisualFeedbackService? _dragDropService;
    private object? _draggedTemplate;

    public TemplateLibraryView()
    {
      this.InitializeComponent();
      ViewModel = new TemplateLibraryViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetTemplateLibraryClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();
      _dragDropService = ServiceProvider.GetDragDropVisualFeedbackService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(TemplateLibraryViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Template Library Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(TemplateLibraryViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Template Library", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += TemplateLibraryView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void TemplateLibraryView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Template Library Help";
      HelpOverlay.HelpText = "The Template Library provides project templates to quickly start new projects with pre-configured settings, tracks, and effects. Browse templates by category, preview template details, and apply templates to create new projects. Create your own templates from existing projects.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+N", Description = "Create template from project" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh template list" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Escape", Description = "Close help" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Templates save time by providing pre-configured project setups");
      HelpOverlay.Tips.Add("Create templates from your favorite project configurations");
      HelpOverlay.Tips.Add("Templates can include tracks, effects, and automation curves");
      HelpOverlay.Tips.Add("Organize templates by category for easy browsing");
      HelpOverlay.Tips.Add("Apply templates to start new projects with a solid foundation");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void Template_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var template = element.DataContext ?? listView.SelectedItem;
        if (template != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var applyItem = new MenuFlyoutItem { Text = "Apply Template" };
            applyItem.Click += async (_, _) => await HandleTemplateMenuClick("Apply", template);
            menu.Items.Add(applyItem);

            var previewItem = new MenuFlyoutItem { Text = "Preview" };
            previewItem.Click += async (_, _) => await HandleTemplateMenuClick("Preview", template);
            menu.Items.Add(previewItem);

            var editItem = new MenuFlyoutItem { Text = "Edit" };
            editItem.Click += async (_, _) => await HandleTemplateMenuClick("Edit", template);
            menu.Items.Add(editItem);

            var duplicateItem = new MenuFlyoutItem { Text = "Duplicate" };
            duplicateItem.Click += async (_, _) => await HandleTemplateMenuClick("Duplicate", template);
            menu.Items.Add(duplicateItem);

            var exportItem = new MenuFlyoutItem { Text = "Export" };
            exportItem.Click += async (_, _) => await HandleTemplateMenuClick("Export", template);
            menu.Items.Add(exportItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, _) => await HandleTemplateMenuClick("Delete", template);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private async System.Threading.Tasks.Task HandleTemplateMenuClick(string action, object templateObj)
    {
      try
      {
        var template = (TemplateItem)templateObj;
        switch (action.ToLower())
        {
          case "apply":
            ViewModel.SelectedTemplate = template;
            _toastService?.ShowToast(ToastType.Success, "Template Applied", "Template applied to new project");
            break;
          case "preview":
            _toastService?.ShowToast(ToastType.Info, "Preview", "Previewing template");
            break;
          case "edit":
            ViewModel.SelectedTemplate = template;
            _toastService?.ShowToast(ToastType.Info, "Edit Template", "Template selected for editing");
            break;
          case "duplicate":
            DuplicateTemplate(template);
            break;
          case "export":
            await ExportTemplateAsync(template);
            break;
          case "delete":
            var dialog = new ContentDialog
            {
              Title = "Delete Template",
              Content = "Are you sure you want to delete this template? This action cannot be undone.",
              PrimaryButtonText = "Delete",
              CloseButtonText = "Cancel",
              DefaultButton = ContentDialogButton.Close,
              XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
              var templateToDelete = template;
              var templateIndex = ViewModel.Templates.IndexOf(template);

              ViewModel.Templates.Remove(template);

              // Register undo action
              if (_undoRedoService != null && templateIndex >= 0)
              {
                var actionObj = new SimpleAction(
                    "Delete Template",
                    () => ViewModel.Templates.Insert(templateIndex, templateToDelete),
                    () => ViewModel.Templates.Remove(templateToDelete));
                _undoRedoService.RegisterAction(actionObj);
              }

              _toastService?.ShowToast(ToastType.Success, "Deleted", "Template deleted");
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    // Drag-and-drop handlers for template reordering
    private void Template_DragStarting(UIElement sender, DragStartingEventArgs e)
    {
      if (sender is ListViewItem listViewItem && listViewItem.DataContext is TemplateItem template)
      {
        _draggedTemplate = template;

        // Set drag data
        e.Data.SetText(template.Id);
        e.Data.Properties.Add("TemplateId", template.Id);
        e.Data.Properties.Add("TemplateName", template.Name ?? "Unnamed Template");

        // Reduce opacity of source element
        listViewItem.Opacity = 0.5;
      }
    }

    private void Template_DragItemsCompleted(UIElement sender, DragItemsCompletedEventArgs e)
    {
      // Clean up drag state
      if (sender is ListViewItem listViewItem)
      {
        listViewItem.Opacity = 1.0;
      }

      _dragDropService?.Cleanup();

      _draggedTemplate = null;
    }

    private void Template_DragOver(object sender, DragEventArgs e)
    {
      if (sender is ListViewItem listViewItem && _dragDropService != null)
      {
        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsGlyphVisible = false;
        e.DragUIOverride.IsContentVisible = false;

        // Show drop target indicator
        var position = e.GetPosition(listViewItem);
        var dropPosition = DetermineTemplateDropPosition(listViewItem, position);
        _dragDropService.ShowDropTargetIndicator(listViewItem, dropPosition);
      }
    }

    private void Template_Drop(object sender, DragEventArgs e)
    {
      if (sender is ListViewItem listViewItem && _draggedTemplate != null && _dragDropService != null)
      {
        e.AcceptedOperation = DataPackageOperation.Move;

        // Hide drop indicator
        _dragDropService.HideDropTargetIndicator();
        _dragDropService.Cleanup();

        // Get target template
        if (listViewItem.DataContext is TemplateItem targetTemplate && _draggedTemplate is TemplateItem draggedTemplate)
        {
          var draggedIndex = ViewModel.Templates.IndexOf(draggedTemplate);
          var targetIndex = ViewModel.Templates.IndexOf(targetTemplate);

          if (draggedIndex >= 0 && targetIndex >= 0 && draggedIndex != targetIndex)
          {
            // Determine drop position
            var position = e.GetPosition(listViewItem);
            var dropPosition = DetermineTemplateDropPosition(listViewItem, position);

            // Reorder templates in the collection
            ViewModel.Templates.RemoveAt(draggedIndex);

            if (dropPosition == DropPosition.Before)
            {
              ViewModel.Templates.Insert(targetIndex, draggedTemplate);
            }
            else if (dropPosition == DropPosition.After)
            {
              var newIndex = targetIndex < draggedIndex ? targetIndex + 1 : targetIndex;
              ViewModel.Templates.Insert(newIndex, draggedTemplate);
            }
            else
            {
              // On - replace target
              ViewModel.Templates.Insert(targetIndex, draggedTemplate);
            }

            _toastService?.ShowToast(ToastType.Success, "Reordered", $"Moved '{draggedTemplate.Name}' in template list");
          }
        }

        // Clean up drag state
        _draggedTemplate = null;

        // Restore source element opacity
        if (e.OriginalSource is ListViewItem sourceItem)
        {
          sourceItem.Opacity = 1.0;
        }
      }
    }

    private void Template_DragLeave(object sender, DragEventArgs e)
    {
      _dragDropService?.HideDropTargetIndicator();
    }

    private DropPosition DetermineTemplateDropPosition(ListViewItem target, Point position)
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

    private void DuplicateTemplate(object template)
    {
      if (template is TemplateItem originalTemplate)
      {
        var duplicate = new TemplateItem(
            new TemplateLibraryTemplate
            {
              Id = Guid.NewGuid().ToString(),
              Name = $"{originalTemplate.Name} (Copy)",
              Category = originalTemplate.Category,
              Description = originalTemplate.Description,
              ThumbnailUrl = originalTemplate.ThumbnailUrl,
              ProjectData = new System.Collections.Generic.Dictionary<string, object>(),
              Tags = originalTemplate.Tags != null ? new System.Collections.Generic.List<string>(originalTemplate.Tags) : new System.Collections.Generic.List<string>(),
              Author = originalTemplate.Author,
              Version = "1.0",
              IsPublic = false,
              UsageCount = 0,
              Created = DateTime.UtcNow.ToString("O"),
              Modified = DateTime.UtcNow.ToString("O")
            }
        );

        var templateIndex = ViewModel.Templates.IndexOf(originalTemplate);
        ViewModel.Templates.Insert(templateIndex + 1, duplicate);
        ViewModel.SelectedTemplate = duplicate;
        _toastService?.ShowToast(ToastType.Success, "Duplicated", "Template duplicated");
      }
    }

    private async System.Threading.Tasks.Task ExportTemplateAsync(object template)
    {
      if (template is TemplateItem templateItem)
      {
        try
        {
          var picker = new Windows.Storage.Pickers.FileSavePicker();
          picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
          picker.FileTypeChoices.Add("JSON", new[] { ".json" });
          picker.SuggestedFileName = $"{templateItem.Name}_export";

          var file = await picker.PickSaveFileAsync();
          if (file != null)
          {
            var jsonData = new
            {
              Id = templateItem.Id,
              Name = templateItem.Name,
              Category = templateItem.Category,
              Description = templateItem.Description,
              ThumbnailUrl = templateItem.ThumbnailUrl,
              Tags = templateItem.Tags,
              Author = templateItem.Author,
              IsPublic = templateItem.IsPublic,
              UsageCount = templateItem.UsageCount
            };
            var content = System.Text.Json.JsonSerializer.Serialize(jsonData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await Windows.Storage.FileIO.WriteTextAsync(file, content);
            _toastService?.ShowToast(ToastType.Success, "Export", "Template exported successfully");
          }
        }
        catch (Exception ex)
        {
          _toastService?.ShowToast(ToastType.Error, "Export Failed", ex.Message);
        }
      }
    }
  }
}