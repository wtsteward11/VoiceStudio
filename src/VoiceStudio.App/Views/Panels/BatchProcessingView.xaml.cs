using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;
using Windows.ApplicationModel.DataTransfer;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.Core.Models;
using System;
using System.Linq;

// Type alias to resolve ambiguity with VoiceStudio.App.Services.BatchJob
using BatchJob = VoiceStudio.Core.Models.BatchJob;

namespace VoiceStudio.App.Views.Panels
{
  public sealed partial class BatchProcessingView : UserControl
  {
    public BatchProcessingViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;
    private DragDropVisualFeedbackService? _dragDropService;
    private BatchJob? _draggedJob;
    private Controls.PanelHost? _parentPanelHost;

    public BatchProcessingView()
    {
      this.InitializeComponent();
      ViewModel = new BatchProcessingViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IBatchProcessingClient>(),
          AppServices.GetRequiredService<IDialogService>()
      );
      this.DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();
      _dragDropService = ServiceProvider.GetDragDropVisualFeedbackService();

      // Subscribe to ViewModel events for quality metrics updates
      ViewModel.PropertyChanged += (s, e) =>
      {
        // Update quality badge if quality metrics change
        if (e.PropertyName == nameof(BatchProcessingViewModel.QualityMetrics) ||
                  e.PropertyName == nameof(BatchProcessingViewModel.HasQualityMetrics))
        {
          UpdatePanelHostQualityMetrics();
        }
      };

      // Add keyboard handler for multi-select
      this.KeyDown += BatchProcessingView_KeyDown;

      // Setup keyboard navigation
      this.Loaded += BatchProcessingView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        // Close any open dialogs or overlays
      });

      // Wire up PanelHost integration for quality badge (IDEA 8)
      this.Loaded += BatchProcessingView_Loaded;

      // Load jobs and queue status on initialization
      _ = ViewModel.LoadJobsCommand.ExecuteAsync(null);
      _ = ViewModel.LoadQueueStatusCommand.ExecuteAsync(null);

      // Subscribe to jobs collection changes to update timeline (IDEA 23)
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(BatchProcessingViewModel.Jobs))
        {
          UpdateTimeline();
        }
        else if (e.PropertyName == nameof(BatchProcessingViewModel.SelectedJob))
        {
          UpdateTimeline();
        }
      };
    }

    private void UpdateTimeline()
    {
      if (BatchQueueTimeline == null || ViewModel?.Jobs == null)
        return;

      // Update timeline with current jobs
      var jobsList = ViewModel.Jobs.ToList();
      BatchQueueTimeline.SetJobs(jobsList);
    }

    private void BatchProcessingView_Loaded(object sender, RoutedEventArgs e)
    {
      // Find parent PanelHost
      _parentPanelHost = FindParentPanelHost(this);
      if (_parentPanelHost != null)
      {
        // Enable quality badge
        _parentPanelHost.ShowQualityBadge = true;
        _parentPanelHost.PanelTitle = "Batch Processing";
        _parentPanelHost.PanelIcon = "⚡";

        // Set initial quality metrics if available
        UpdatePanelHostQualityMetrics();
      }
    }

    private Controls.PanelHost? FindParentPanelHost(DependencyObject element)
    {
      var parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element);
      while (parent != null)
      {
        if (parent is Controls.PanelHost panelHost)
        {
          return panelHost;
        }
        parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent);
      }
      return null;
    }

    private void UpdatePanelHostQualityMetrics()
    {
      if (_parentPanelHost != null && ViewModel != null)
      {
        // Get quality metrics from ViewModel if available
        var qualityMetricsProperty = ViewModel.GetType().GetProperty("QualityMetrics");
        if (qualityMetricsProperty != null)
        {
          _parentPanelHost.QualityMetrics = qualityMetricsProperty.GetValue(ViewModel) as VoiceStudio.Core.Models.QualityMetrics;
        }
      }
    }

    private void BatchProcessingView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
      var isCtrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

      if (isCtrlPressed && e.Key == VirtualKey.A)
      {
        // Ctrl+A - Select all jobs
        ViewModel.SelectAllJobsCommand.Execute(null);
        UpdateJobSelectionVisuals();
        e.Handled = true;
      }
      else if (e.Key == VirtualKey.Escape)
      {
        // Escape - Clear job selection
        ViewModel.ClearJobSelectionCommand.Execute(null);
        UpdateJobSelectionVisuals();
        e.Handled = true;
      }
    }

    private void Job_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
      if (sender is ListViewItem listViewItem && listViewItem.DataContext is BatchJob job)
      {
        var isCtrlPressed = InputHelper.IsControlPressed();
        var isShiftPressed = InputHelper.IsShiftPressed();

        ViewModel.ToggleJobSelection(job.Id, isCtrlPressed, isShiftPressed);

        UpdateJobSelectionVisuals();
        e.Handled = true;
      }
    }

    private void UpdateJobSelectionVisuals()
    {
      // Update visual indicators for all job list items
      UpdateJobSelectionVisualsRecursive(this);
    }

    private void UpdateJobSelectionVisualsRecursive(DependencyObject element)
    {
      if (element == null || ViewModel == null)
        return;

      // Check if this is a ListViewItem with a BatchJob
      if (element is ListViewItem listViewItem && listViewItem.DataContext is BatchJob job)
      {
        var isSelected = ViewModel.IsJobSelected(job.Id);

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
        UpdateJobSelectionVisualsRecursive(child);
      }
    }

    private void StatusFilter_SelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs _)
    {
      if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
      {
        if (string.IsNullOrEmpty(tag))
        {
          ViewModel.FilterStatus = null;
        }
        else if (System.Enum.TryParse<JobStatus>(tag, out var status))
        {
          ViewModel.FilterStatus = status;
        }
      }
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Batch Processing Help";
      HelpOverlay.HelpText = "The Batch Processing panel allows you to create and manage batch synthesis jobs. Create jobs with multiple text inputs, configure engines and settings, and process them all in a queue. Monitor job progress, filter by status, and manage the processing queue. Auto-refresh keeps the status updated automatically.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh job list" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+N", Description = "Create new batch job" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Delete", Description = "Delete selected job" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Batch jobs process multiple texts efficiently in a queue");
      HelpOverlay.Tips.Add("Enable auto-refresh to automatically update job status");
      HelpOverlay.Tips.Add("Use filters to find jobs by status (Pending, Running, Completed, Failed)");
      HelpOverlay.Tips.Add("Job progress is shown as a percentage with estimated time remaining");
      HelpOverlay.Tips.Add("Cancelled jobs can be restarted or deleted from the list");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void Job_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var job = element.DataContext as BatchJob ?? listView.SelectedItem as BatchJob;
        if (job != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var startItem = new MenuFlyoutItem { Text = "Start Job" };
            startItem.Click += async (_, _) => await HandleJobMenuClick("Start", job);
            menu.Items.Add(startItem);

            var cancelItem = new MenuFlyoutItem { Text = "Cancel Job" };
            cancelItem.Click += async (_, _) => await HandleJobMenuClick("Cancel", job);
            menu.Items.Add(cancelItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var duplicateItem = new MenuFlyoutItem { Text = "Duplicate" };
            duplicateItem.Click += async (_, _) => await HandleJobMenuClick("Duplicate", job);
            menu.Items.Add(duplicateItem);

            var exportItem = new MenuFlyoutItem { Text = "Export Results" };
            exportItem.Click += async (_, _) => await HandleJobMenuClick("Export", job);
            menu.Items.Add(exportItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, _) => await HandleJobMenuClick("Delete", job);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private async System.Threading.Tasks.Task HandleJobMenuClick(string action, BatchJob job)
    {
      try
      {
        switch (action.ToLower())
        {
          case "start":
            if (ViewModel.StartJobCommand.CanExecute(job))
            {
              await ViewModel.StartJobCommand.ExecuteAsync(job);
              _toastService?.ShowToast(ToastType.Success, "Job Started", $"Started job '{job.Name}'");
            }
            break;
          case "cancel":
            if (ViewModel.CancelJobCommand.CanExecute(job))
            {
              await ViewModel.CancelJobCommand.ExecuteAsync(job);
              _toastService?.ShowToast(ToastType.Info, "Job Cancelled", $"Cancelled job '{job.Name}'");
            }
            break;
          case "duplicate":
            await DuplicateJobAsync(job);
            break;
          case "export":
            _toastService?.ShowToast(ToastType.Info, "Export", $"Export functionality for '{job.Name}' is planned for a future release. Use the download button to save results.");
            break;
          case "delete":
            if (ViewModel.DeleteJobCommand.CanExecute(job))
            {
              var dialog = new ContentDialog
              {
                Title = "Delete Job",
                Content = $"Are you sure you want to delete job '{job.Name}'? This action cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
              };

              var result = await dialog.ShowAsync();
              if (result == ContentDialogResult.Primary)
              {
                var jobToDelete = job;
                var jobIndex = ViewModel.Jobs.IndexOf(job);

                await ViewModel.DeleteJobCommand.ExecuteAsync(job);

                // Register undo action
                if (_undoRedoService != null && jobIndex >= 0)
                {
                  var actionObj = new SimpleAction(
                      $"Delete Job: {job.Name}",
                      () => ViewModel.Jobs.Insert(jobIndex, jobToDelete),
                      () => ViewModel.Jobs.Remove(jobToDelete));
                  _undoRedoService.RegisterAction(actionObj);
                }

                _toastService?.ShowToast(ToastType.Success, "Deleted", $"Deleted job '{job.Name}'");
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

    private System.Threading.Tasks.Task DuplicateJobAsync(BatchJob job)
    {
      try
      {
        var duplicatedJob = new BatchJob
        {
          Id = Guid.NewGuid().ToString(),
          Name = $"{job.Name} Copy",
          ProjectId = job.ProjectId,
          VoiceProfileId = job.VoiceProfileId,
          EngineId = job.EngineId,
          Text = job.Text,
          Language = job.Language,
          Status = JobStatus.Pending,
          Progress = 0.0,
          Created = DateTime.UtcNow
        };

        ViewModel.Jobs.Add(duplicatedJob);

        // Register undo action
        if (_undoRedoService != null)
        {
          var actionObj = new SimpleAction(
              $"Duplicate Job: {job.Name}",
              () => ViewModel.Jobs.Remove(duplicatedJob),
              () => ViewModel.Jobs.Add(duplicatedJob));
          _undoRedoService.RegisterAction(actionObj);
        }

        _toastService?.ShowToast(ToastType.Success, "Duplicated", $"Duplicated job '{job.Name}'");
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to duplicate job: {ex.Message}");
      }

      return System.Threading.Tasks.Task.CompletedTask;
    }

    // Drag-and-drop handlers for queue reordering
    private void Job_DragStarting(UIElement sender, DragStartingEventArgs e)
    {
      if (sender is ListViewItem listViewItem && listViewItem.DataContext is BatchJob job)
      {
        _draggedJob = job;

        // Set drag data
        e.Data.SetText(job.Id);
        e.Data.Properties.Add("JobId", job.Id);
        e.Data.Properties.Add("JobName", job.Name ?? "Unnamed Job");

        // Reduce opacity of source element
        listViewItem.Opacity = 0.5;
      }
    }

    private void Job_DragItemsCompleted(UIElement sender, DragItemsCompletedEventArgs _)
    {
      // Clean up drag state
      if (sender is ListViewItem listViewItem)
      {
        listViewItem.Opacity = 1.0;
      }

      _dragDropService?.Cleanup();

      _draggedJob = null;
    }

    private void Job_DragOver(object sender, DragEventArgs e)
    {
      if (sender is ListViewItem listViewItem && _dragDropService != null)
      {
        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsGlyphVisible = false;
        e.DragUIOverride.IsContentVisible = false;

        // Show drop target indicator
        var position = e.GetPosition(listViewItem);
        var dropPosition = DetermineJobDropPosition(listViewItem, position);
        _dragDropService.ShowDropTargetIndicator(listViewItem, dropPosition);
      }
    }

    private void Job_Drop(object sender, DragEventArgs e)
    {
      if (sender is ListViewItem listViewItem && _draggedJob != null && _dragDropService != null)
      {
        e.AcceptedOperation = DataPackageOperation.Move;

        // Hide drop indicator
        _dragDropService.HideDropTargetIndicator();
        _dragDropService.Cleanup();

        // Get target job
        if (listViewItem.DataContext is BatchJob targetJob)
        {
          var draggedJob = _draggedJob;
          var draggedIndex = ViewModel.Jobs.IndexOf(draggedJob);
          var targetIndex = ViewModel.Jobs.IndexOf(targetJob);

          if (draggedIndex >= 0 && targetIndex >= 0 && draggedIndex != targetIndex)
          {
            // Determine drop position
            var position = e.GetPosition(listViewItem);
            var dropPosition = DetermineJobDropPosition(listViewItem, position);

            // Reorder jobs in the collection
            ViewModel.Jobs.RemoveAt(draggedIndex);

            if (dropPosition == DropPosition.Before)
            {
              ViewModel.Jobs.Insert(targetIndex, draggedJob);
            }
            else if (dropPosition == DropPosition.After)
            {
              var newIndex = targetIndex < draggedIndex ? targetIndex + 1 : targetIndex;
              ViewModel.Jobs.Insert(newIndex, draggedJob);
            }
            else
            {
              // On - replace target
              ViewModel.Jobs.Insert(targetIndex, draggedJob);
            }

            _toastService?.ShowToast(ToastType.Success, "Reordered", $"Moved '{draggedJob.Name}' in queue");
          }
        }

        // Clean up drag state
        _draggedJob = null;

        // Restore source element opacity
        if (e.OriginalSource is ListViewItem sourceItem)
        {
          sourceItem.Opacity = 1.0;
        }
      }
    }

    private void Job_DragLeave(object _, DragEventArgs __)
    {
      _dragDropService?.HideDropTargetIndicator();
    }

    private void BatchProcessingView_KeyboardNavigation_Loaded(object sender, RoutedEventArgs e)
    {
      // Setup Tab navigation order for this panel
      KeyboardNavigationHelper.SetupTabNavigation(this, 0);
    }

    private DropPosition DetermineJobDropPosition(ListViewItem target, Point position)
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
  }
}