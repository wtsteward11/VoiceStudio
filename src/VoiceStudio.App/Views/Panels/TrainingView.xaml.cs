using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.Core.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.ApplicationModel.DataTransfer;

namespace VoiceStudio.App.Views.Panels
{
  public sealed partial class TrainingView : Microsoft.UI.Xaml.Controls.UserControl
  {
    public TrainingViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;
    private DragDropVisualFeedbackService? _dragDropService;
    private TrainingDataset? _draggedDataset;

    public TrainingView()
    {
      this.InitializeComponent();
      ViewModel = new TrainingViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          ServiceProvider.GetBackendClient()
      );
      this.DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();
      _dragDropService = ServiceProvider.GetDragDropVisualFeedbackService();

      // Load datasets on initialization
      _ = ViewModel.LoadDatasetsCommand.ExecuteAsync(null);
      _ = ViewModel.LoadTrainingJobsCommand.ExecuteAsync(null);

      // Add Enter key handling for form submission
      this.KeyDown += TrainingView_KeyDown;

      // Setup keyboard navigation
      this.Loaded += TrainingView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        // Close any open dialogs or overlays
      });

      // Subscribe to quality history changes to update chart
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(ViewModel.QualityHistory) ||
                  e.PropertyName == nameof(ViewModel.SelectedTrainingJob))
        {
          UpdateProgressChart();
        }
      };
    }

    private void ProgressChart_Loaded(object _, RoutedEventArgs __)
    {
      UpdateProgressChart();
    }

    private void UpdateProgressChart()
    {
      // Note: ProgressChart control not implemented in XAML
      // if (ProgressChart != null && ViewModel?.QualityHistory != null)
      // {
      //     ProgressChart.UpdateChart(ViewModel.QualityHistory);
      // }
    }

    private void TrainingView_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
      var isCtrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

      // Enter key in single-line TextBoxes should trigger Create Dataset
      if (e.Key == Windows.System.VirtualKey.Enter && !isCtrlPressed)
      {
        var focusedElement = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(this.XamlRoot);
        if (focusedElement is Microsoft.UI.Xaml.Controls.TextBox textBox && !textBox.AcceptsReturn)
        {
          // Single-line input - Enter submits
          if (ViewModel.CreateDatasetCommand.CanExecute(null))
          {
            _ = ViewModel.CreateDatasetCommand.ExecuteAsync(null);
            e.Handled = true;
          }
        }
        // Multi-line TextBoxes (AcceptsReturn="True") will create new line on Enter
      }
      else if (isCtrlPressed && e.Key == VirtualKey.A)
      {
        // Ctrl+A - Select all (context-aware: datasets or training jobs based on focus)
        var focusedElement = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(this.XamlRoot);
        // Try to determine which list is focused - default to datasets
        if (focusedElement != null)
        {
          var parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent((Microsoft.UI.Xaml.DependencyObject)focusedElement);
          while (parent != null)
          {
            if (parent is ListView listView)
            {
              // Check if it's the training jobs list
              if (listView.Name == "TrainingJobsListView" || (listView.DataContext == ViewModel && ViewModel.TrainingJobs.Count > 0))
              {
                ViewModel.SelectAllTrainingJobsCommand.Execute(null);
                UpdateTrainingJobSelectionVisuals();
              }
              else
              {
                ViewModel.SelectAllDatasetsCommand.Execute(null);
                UpdateDatasetSelectionVisuals();
              }
              e.Handled = true;
              return;
            }
            parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent);
          }
        }
        // Default to datasets
        ViewModel.SelectAllDatasetsCommand.Execute(null);
        UpdateDatasetSelectionVisuals();
        e.Handled = true;
      }
      else if (e.Key == VirtualKey.Escape)
      {
        // Escape - Clear selections
        ViewModel.ClearDatasetSelectionCommand.Execute(null);
        ViewModel.ClearTrainingJobSelectionCommand.Execute(null);
        UpdateDatasetSelectionVisuals();
        UpdateTrainingJobSelectionVisuals();
        e.Handled = true;
      }
    }

    private void Dataset_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
      if (sender is ListViewItem listViewItem && listViewItem.DataContext is TrainingDataset dataset)
      {
        var isCtrlPressed = InputHelper.IsControlPressed();
        var isShiftPressed = InputHelper.IsShiftPressed();

        ViewModel.ToggleDatasetSelection(dataset.Id, isCtrlPressed, isShiftPressed);

        UpdateDatasetSelectionVisuals();
        e.Handled = true;
      }
    }

    private void TrainingJob_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
      if (sender is ListViewItem listViewItem && listViewItem.DataContext is TrainingStatus job)
      {
        var isCtrlPressed = InputHelper.IsControlPressed();
        var isShiftPressed = InputHelper.IsShiftPressed();

        ViewModel.ToggleTrainingJobSelection(job.Id, isCtrlPressed, isShiftPressed);

        UpdateTrainingJobSelectionVisuals();
        e.Handled = true;
      }
    }

    private void UpdateDatasetSelectionVisuals()
    {
      // Update visual indicators for all dataset list items
      UpdateDatasetSelectionVisualsRecursive(this);
    }

    private void UpdateDatasetSelectionVisualsRecursive(DependencyObject element)
    {
      if (element == null || ViewModel == null)
        return;

      // Check if this is a ListViewItem with a TrainingDataset
      if (element is ListViewItem listViewItem && listViewItem.DataContext is TrainingDataset dataset)
      {
        var isSelected = ViewModel.IsDatasetSelected(dataset.Id);

        // Update background to show selection
        if (isSelected)
        {
          listViewItem.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(51, 0, 183, 194)); // VSQ.Accent.Cyan with opacity
        }
        else
        {
          listViewItem.Background = null; // Use default
        }
      }

      // Recursively check children
      var childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(element);
      for (int i = 0; i < childCount; i++)
      {
        var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(element, i);
        UpdateDatasetSelectionVisualsRecursive(child);
      }
    }

    private void UpdateTrainingJobSelectionVisuals()
    {
      // Update visual indicators for all training job list items
      UpdateTrainingJobSelectionVisualsRecursive(this);
    }

    private void TrainingView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      // Setup Tab navigation order for this panel
      KeyboardNavigationHelper.SetupTabNavigation(this, 0);
    }

    private void UpdateTrainingJobSelectionVisualsRecursive(DependencyObject element)
    {
      if (element == null || ViewModel == null)
        return;

      // Check if this is a ListViewItem with a TrainingStatus
      if (element is ListViewItem listViewItem && listViewItem.DataContext is TrainingStatus job)
      {
        var isSelected = ViewModel.IsTrainingJobSelected(job.Id);

        // Update background to show selection
        if (isSelected)
        {
          listViewItem.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(51, 0, 183, 194)); // VSQ.Accent.Cyan with opacity
        }
        else
        {
          listViewItem.Background = null; // Use default
        }
      }

      // Recursively check children
      var childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(element);
      for (int i = 0; i < childCount; i++)
      {
        var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(element, i);
        UpdateTrainingJobSelectionVisualsRecursive(child);
      }
    }

    private void StatusFilter_SelectionChanged(object _, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs __)
    {
      // Note: StatusFilter ComboBox not implemented in XAML
      // if (StatusFilter.SelectedItem is Microsoft.UI.Xaml.Controls.ComboBoxItem item && item.Tag is string status)
      // {
      //     ViewModel.FilterStatus = string.IsNullOrEmpty(status) ? null : status;
      //     _ = ViewModel.LoadTrainingJobsCommand.ExecuteAsync(null);
      // }
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Training Help";
      HelpOverlay.HelpText = "The Training panel allows you to train custom voice models. Create datasets from audio files, configure training parameters, and start training jobs. Monitor training progress, view logs, and manage training jobs. Training requires sufficient audio data and can take significant time.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh datasets or training jobs" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+N", Description = "Create new dataset" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Datasets should contain high-quality, clean audio recordings");
      HelpOverlay.Tips.Add("More audio data generally leads to better model quality");
      HelpOverlay.Tips.Add("Training can take hours or days depending on dataset size");
      HelpOverlay.Tips.Add("Monitor training logs to track progress and identify issues");
      HelpOverlay.Tips.Add("Use quality metrics to evaluate trained models");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void Dataset_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var dataset = element.DataContext as TrainingDataset ?? listView.SelectedItem as TrainingDataset;
        if (dataset != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var startTrainingItem = new MenuFlyoutItem { Text = "Start Training" };
            startTrainingItem.Click += async (_, _) => await HandleDatasetMenuClick("Start Training", dataset);
            menu.Items.Add(startTrainingItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var duplicateItem = new MenuFlyoutItem { Text = "Duplicate" };
            duplicateItem.Click += async (_, _) => await HandleDatasetMenuClick("Duplicate", dataset);
            menu.Items.Add(duplicateItem);

            var exportItem = new MenuFlyoutItem { Text = "Export" };
            exportItem.Click += async (_, _) => await HandleDatasetMenuClick("Export", dataset);
            menu.Items.Add(exportItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, _) => await HandleDatasetMenuClick("Delete", dataset);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private void TrainingJob_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var job = element.DataContext as TrainingStatus ?? listView.SelectedItem as TrainingStatus;
        if (job != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var cancelItem = new MenuFlyoutItem { Text = "Cancel Job" };
            cancelItem.Click += async (_, _) => await HandleTrainingJobMenuClick("Cancel", job);
            menu.Items.Add(cancelItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var viewLogsItem = new MenuFlyoutItem { Text = "View Logs" };
            viewLogsItem.Click += async (_, _) => await HandleTrainingJobMenuClick("View Logs", job);
            menu.Items.Add(viewLogsItem);

            var exportItem = new MenuFlyoutItem { Text = "Export Model" };
            exportItem.Click += async (_, _) => await HandleTrainingJobMenuClick("Export", job);
            menu.Items.Add(exportItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, _) => await HandleTrainingJobMenuClick("Delete", job);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private async System.Threading.Tasks.Task HandleDatasetMenuClick(string action, TrainingDataset dataset)
    {
      try
      {
        switch (action.ToLower())
        {
          case "start training":
            if (ViewModel.StartTrainingCommand.CanExecute(dataset))
            {
              await ViewModel.StartTrainingCommand.ExecuteAsync(dataset);
              _toastService?.ShowToast(ToastType.Success, "Training Started", $"Started training for dataset '{dataset.Name}'");
            }
            break;
          case "duplicate":
            await DuplicateDatasetAsync(dataset);
            break;
          case "export":
            await ExportDatasetAsync(dataset);
            break;
          case "delete":
            if (ViewModel.DeleteDatasetCommand.CanExecute(dataset))
            {
              var dialog = new ContentDialog
              {
                Title = "Delete Dataset",
                Content = $"Are you sure you want to delete dataset '{dataset.Name}'? This action cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
              };

              var result = await dialog.ShowAsync();
              if (result == ContentDialogResult.Primary)
              {
                var datasetToDelete = dataset;
                var datasetIndex = ViewModel.Datasets.IndexOf(dataset);

                await ViewModel.DeleteDatasetCommand.ExecuteAsync(dataset);

                // Register undo action
                if (_undoRedoService != null && datasetIndex >= 0)
                {
                  var actionObj = new SimpleAction(
                      $"Delete Dataset: {dataset.Name}",
                      () => ViewModel.Datasets.Insert(datasetIndex, datasetToDelete),
                      () => ViewModel.Datasets.Remove(datasetToDelete));
                  _undoRedoService.RegisterAction(actionObj);
                }

                _toastService?.ShowToast(ToastType.Success, "Deleted", $"Deleted dataset '{dataset.Name}'");
              }
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private async System.Threading.Tasks.Task HandleTrainingJobMenuClick(string action, TrainingStatus job)
    {
      try
      {
        switch (action.ToLower())
        {
          case "cancel":
            if (ViewModel.CancelTrainingCommand.CanExecute(job))
            {
              await ViewModel.CancelTrainingCommand.ExecuteAsync(job);
              _toastService?.ShowToast(ToastType.Info, "Job Cancelled", "Cancelled training job");
            }
            break;
          case "view logs":
            _toastService?.ShowToast(ToastType.Info, "View Logs", "Logs are displayed in the Training Logs section");
            break;
          case "export":
            await ExportTrainingJobAsync(job);
            break;
          case "delete":
            if (ViewModel.DeleteTrainingJobCommand.CanExecute(job))
            {
              var dialog = new ContentDialog
              {
                Title = "Delete Training Job",
                Content = "Are you sure you want to delete this training job? This action cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
              };

              var result = await dialog.ShowAsync();
              if (result == ContentDialogResult.Primary)
              {
                var jobToDelete = job;
                var jobIndex = ViewModel.TrainingJobs.IndexOf(job);

                await ViewModel.DeleteTrainingJobCommand.ExecuteAsync(job);

                // Register undo action
                if (_undoRedoService != null && jobIndex >= 0)
                {
                  var actionObj = new SimpleAction(
                      "Delete Training Job",
                      () => ViewModel.TrainingJobs.Insert(jobIndex, jobToDelete),
                      () => ViewModel.TrainingJobs.Remove(jobToDelete));
                  _undoRedoService.RegisterAction(actionObj);
                }

                _toastService?.ShowToast(ToastType.Success, "Deleted", "Deleted training job");
              }
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private System.Threading.Tasks.Task DuplicateDatasetAsync(TrainingDataset dataset)
    {
      try
      {
        var duplicatedDataset = new TrainingDataset
        {
          Id = Guid.NewGuid().ToString(),
          Name = $"{dataset.Name} Copy",
          Description = dataset.Description,
          AudioFiles = new System.Collections.Generic.List<string>(dataset.AudioFiles),
          Transcripts = dataset.Transcripts != null ? new System.Collections.Generic.List<string>(dataset.Transcripts) : null,
          Created = DateTime.UtcNow,
          Modified = DateTime.UtcNow
        };

        ViewModel.Datasets.Add(duplicatedDataset);

        // Register undo action
        if (_undoRedoService != null)
        {
          var actionObj = new SimpleAction(
              $"Duplicate Dataset: {dataset.Name}",
              () => ViewModel.Datasets.Remove(duplicatedDataset),
              () => ViewModel.Datasets.Add(duplicatedDataset));
          _undoRedoService.RegisterAction(actionObj);
        }

        _toastService?.ShowToast(ToastType.Success, "Duplicated", $"Duplicated dataset '{dataset.Name}'");
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to duplicate dataset: {ex.Message}");
      }

      return System.Threading.Tasks.Task.CompletedTask;
    }

    // Drag-and-drop handlers for dataset reordering
    private void Dataset_DragStarting(UIElement sender, DragStartingEventArgs e)
    {
      if (sender is ListViewItem listViewItem && listViewItem.DataContext is TrainingDataset dataset)
      {
        _draggedDataset = dataset;

        // Set drag data
        e.Data.SetText(dataset.Id);
        e.Data.Properties.Add("DatasetId", dataset.Id);
        e.Data.Properties.Add("DatasetName", dataset.Name ?? "Unnamed Dataset");

        // Reduce opacity of source element
        listViewItem.Opacity = 0.5;
      }
    }

    private void Dataset_DragItemsCompleted(UIElement sender, DragItemsCompletedEventArgs e)
    {
      // Clean up drag state
      if (sender is ListViewItem listViewItem)
      {
        listViewItem.Opacity = 1.0;
      }

      _dragDropService?.Cleanup();

      _draggedDataset = null;
    }

    private void Dataset_DragOver(object sender, DragEventArgs e)
    {
      if (sender is ListViewItem listViewItem && _dragDropService != null)
      {
        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsGlyphVisible = false;
        e.DragUIOverride.IsContentVisible = false;

        // Show drop target indicator
        var position = e.GetPosition(listViewItem);
        var dropPosition = DetermineDatasetDropPosition(listViewItem, position);
        _dragDropService.ShowDropTargetIndicator(listViewItem, dropPosition);
      }
    }

    private void Dataset_Drop(object sender, DragEventArgs e)
    {
      if (sender is ListViewItem listViewItem && _draggedDataset != null && _dragDropService != null)
      {
        e.AcceptedOperation = DataPackageOperation.Move;

        // Hide drop indicator
        _dragDropService.HideDropTargetIndicator();
        _dragDropService.Cleanup();

        // Get target dataset
        if (listViewItem.DataContext is TrainingDataset targetDataset)
        {
          var draggedDataset = _draggedDataset;
          var draggedIndex = ViewModel.Datasets.IndexOf(draggedDataset);
          var targetIndex = ViewModel.Datasets.IndexOf(targetDataset);

          if (draggedIndex >= 0 && targetIndex >= 0 && draggedIndex != targetIndex)
          {
            // Determine drop position
            var position = e.GetPosition(listViewItem);
            var dropPosition = DetermineDatasetDropPosition(listViewItem, position);

            // Reorder datasets in the collection
            ViewModel.Datasets.RemoveAt(draggedIndex);

            if (dropPosition == DropPosition.Before)
            {
              ViewModel.Datasets.Insert(targetIndex, draggedDataset);
            }
            else if (dropPosition == DropPosition.After)
            {
              var newIndex = targetIndex < draggedIndex ? targetIndex + 1 : targetIndex;
              ViewModel.Datasets.Insert(newIndex, draggedDataset);
            }
            else
            {
              // On - replace target
              ViewModel.Datasets.Insert(targetIndex, draggedDataset);
            }

            _toastService?.ShowToast(ToastType.Success, "Reordered", $"Moved '{draggedDataset.Name}' in dataset list");
          }
        }

        // Clean up drag state
        _draggedDataset = null;

        // Restore source element opacity
        if (e.OriginalSource is ListViewItem sourceItem)
        {
          sourceItem.Opacity = 1.0;
        }
      }
    }

    private void Dataset_DragLeave(object sender, DragEventArgs e)
    {
      _dragDropService?.HideDropTargetIndicator();
    }

    private DropPosition DetermineDatasetDropPosition(ListViewItem target, Point position)
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

    // GAP-004: Serialization logic moved to ViewModel, View handles only file picker/write
    private async System.Threading.Tasks.Task ExportDatasetAsync(TrainingDataset dataset)
    {
      try
      {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("JSON", new[] { ".json" });
        picker.FileTypeChoices.Add("CSV", new[] { ".csv" });
        picker.SuggestedFileName = $"{dataset.Name}_export";

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
          var extension = file.FileType.ToLower();
          var format = extension == ".json" ? "json" : "csv";

          // Delegate serialization to ViewModel (GAP-004 remediation)
          var content = ViewModel.GetExportDatasetContent(dataset, format);

          await Windows.Storage.FileIO.WriteTextAsync(file, content);
          _toastService?.ShowToast(ToastType.Success, "Export", $"Dataset '{dataset.Name}' exported successfully");
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Export Failed", ex.Message);
      }
    }

    // GAP-004: Serialization logic moved to ViewModel, View handles only file picker/write
    private async System.Threading.Tasks.Task ExportTrainingJobAsync(TrainingStatus job)
    {
      try
      {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("JSON", new[] { ".json" });
        picker.SuggestedFileName = "training_job_export";

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
          // Delegate serialization to ViewModel (GAP-004 remediation)
          var content = ViewModel.GetExportTrainingJobContent(job);

          await Windows.Storage.FileIO.WriteTextAsync(file, content);
          _toastService?.ShowToast(ToastType.Success, "Export", "Training job exported successfully");
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Export Failed", ex.Message);
      }
    }
  }
}