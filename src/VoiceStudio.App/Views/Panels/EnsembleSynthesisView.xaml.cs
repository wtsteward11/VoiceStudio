using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services.UndoableActions;
using System;
using System.Linq;
using Windows.System;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// EnsembleSynthesisView panel for multi-voice synthesis.
  /// </summary>
  public sealed partial class EnsembleSynthesisView : UserControl
  {
    public EnsembleSynthesisViewModel ViewModel { get; }
    private ToastNotificationService? _toastService;
    private ContextMenuService? _contextMenuService;
    private UndoRedoService? _undoRedoService;
    private DragDropVisualFeedbackService? _dragDropService;
    private ViewModels.EnsembleVoiceItem? _draggedVoice;
    private int _draggedVoiceIndex = -1;
    private Controls.PanelHost? _parentPanelHost;

    public EnsembleSynthesisView()
    {
      this.InitializeComponent();
      ViewModel = new EnsembleSynthesisViewModel(
          AppServices.GetRequiredService<IViewModelContext>(),
          AppServices.GetRequiredService<IEnsembleSynthesisClient>(),
          AppServices.GetRequiredService<IDialogService>()
      );
      DataContext = ViewModel;

      // Initialize services
      _toastService = ServiceProvider.GetToastNotificationService();
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();
      _dragDropService = ServiceProvider.GetDragDropVisualFeedbackService();

      // Setup keyboard navigation
      this.Loaded += EnsembleSynthesisView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(EnsembleSynthesisViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Ensemble Synthesis Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(EnsembleSynthesisViewModel.QualityMetrics))
        {
          UpdatePanelHostQualityMetrics();
        }
      };

      // Add keyboard handler for multi-select
      this.KeyDown += EnsembleSynthesisView_KeyDown;

      // Wire up PanelHost integration for quality badge (IDEA 8)
      this.Loaded += EnsembleSynthesisView_Loaded;

      // Subscribe to job selection changes to update timeline (IDEA 22)
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(EnsembleSynthesisViewModel.SelectedJob))
        {
          UpdateTimeline();
        }
      };
    }

    private void UpdateTimeline()
    {
      if (EnsembleTimeline == null || ViewModel.SelectedJob == null)
        return;

      // Create voice blocks from the selected job
      var voiceBlocks = new List<Controls.VoiceTimelineBlock>();

      // Create blocks based on job progress from backend API
      const double estimatedDurationPerVoice = 5.0; // 5 seconds per voice (estimate)
      var startTime = 0.0;

      for (int i = 0; i < ViewModel.SelectedJob.TotalVoices; i++)
      {
        string status;
        if (i < ViewModel.SelectedJob.CompletedVoices)
        {
          status = "completed";
        }
        else if (i == ViewModel.SelectedJob.CompletedVoices && ViewModel.SelectedJob.Status == "processing")
        {
          status = "processing";
        }
        else
        {
          status = "pending";
        }

        double progress;
        if (i < ViewModel.SelectedJob.CompletedVoices)
        {
          progress = 1.0;
        }
        else if (i == ViewModel.SelectedJob.CompletedVoices && ViewModel.SelectedJob.Status == "processing")
        {
          progress = ViewModel.SelectedJob.Progress;
        }
        else
        {
          progress = 0.0;
        }

        voiceBlocks.Add(new Controls.VoiceTimelineBlock
        {
          VoiceId = $"voice_{i}",
          ProfileId = $"Voice {i + 1}",
          Engine = "xtts", // Would come from job data
          StartTime = ViewModel.MixMode == "sequential" ? startTime : 0.0,
          Duration = estimatedDurationPerVoice,
          Progress = progress,
          Status = status,
          RowIndex = i
        });

        if (ViewModel.MixMode == "sequential")
        {
          startTime += estimatedDurationPerVoice;
        }
      }

      EnsembleTimeline.MixMode = ViewModel.MixMode;
      EnsembleTimeline.SetTimelineBlocks(voiceBlocks);
    }

    private void EnsembleSynthesisView_Loaded(object sender, RoutedEventArgs e)
    {
      // Find parent PanelHost
      _parentPanelHost = FindParentPanelHost(this);
      if (_parentPanelHost != null)
      {
        // Enable quality badge
        _parentPanelHost.ShowQualityBadge = true;
        _parentPanelHost.PanelTitle = "Ensemble Synthesis";
        _parentPanelHost.PanelIcon = "🎭";

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
        _parentPanelHost.QualityMetrics = ViewModel.QualityMetrics as VoiceStudio.Core.Models.QualityMetrics;
      }
    }

    private void EnsembleSynthesisView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
      var isCtrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

      if (isCtrlPressed && e.Key == VirtualKey.A)
      {
        // Ctrl+A - Select all jobs
        if (ViewModel.SelectAllJobsCommand.CanExecute(null))
        {
          ViewModel.SelectAllJobsCommand.Execute(null);
          e.Handled = true;
        }
      }
      else if (e.Key == VirtualKey.Escape)
      {
        // Escape - Clear job selection
        if (ViewModel.ClearJobSelectionCommand.CanExecute(null))
        {
          ViewModel.ClearJobSelectionCommand.Execute(null);
          e.Handled = true;
        }
      }
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Ensemble Synthesis Help";
      HelpOverlay.HelpText = "The Ensemble Synthesis panel allows you to create multi-voice synthesis by combining multiple voices in different configurations. Add multiple voices with different profiles, engines, and text, then synthesize them together using sequential, parallel, or layered mixing modes. Monitor synthesis jobs and track progress.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+N", Description = "Add voice" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+Enter", Description = "Start synthesis" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh jobs" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Escape", Description = "Close help" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Sequential mode plays voices one after another");
      HelpOverlay.Tips.Add("Parallel mode plays voices simultaneously");
      HelpOverlay.Tips.Add("Layered mode mixes voices with adjustable levels");
      HelpOverlay.Tips.Add("Each voice can use a different profile and engine");
      HelpOverlay.Tips.Add("Monitor synthesis jobs to track progress and results");
      HelpOverlay.Tips.Add("Use different languages and emotions for each voice");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void Voice_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is Border border && border.DataContext is EnsembleVoiceItem voice)
      {
        e.Handled = true;
        if (_contextMenuService != null)
        {
          var menu = new MenuFlyout();

          var duplicateItem = new MenuFlyoutItem { Text = "Duplicate Voice" };
          duplicateItem.Click += async (_, _) => await HandleVoiceMenuClick("Duplicate", voice);
          menu.Items.Add(duplicateItem);

          menu.Items.Add(new MenuFlyoutSeparator());

          var removeItem = new MenuFlyoutItem { Text = "Remove" };
          removeItem.Click += async (_, _) => await HandleVoiceMenuClick("Remove", voice);
          menu.Items.Add(removeItem);

          var position = e.GetPosition(border);
          _contextMenuService.ShowContextMenu(menu, border, position);
        }
      }
    }

    private void Job_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var job = element.DataContext as EnsembleJobItem ?? listView.SelectedItem as EnsembleJobItem;
        if (job != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var viewDetailsItem = new MenuFlyoutItem { Text = "View Details" };
            viewDetailsItem.Click += async (_, _) => await HandleJobMenuClick("View Details", job);
            menu.Items.Add(viewDetailsItem);

            var exportItem = new MenuFlyoutItem { Text = "Export Result" };
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

    private async System.Threading.Tasks.Task HandleVoiceMenuClick(string action, EnsembleVoiceItem voice)
    {
      try
      {
        switch (action.ToLower())
        {
          case "duplicate":
            await DuplicateVoiceAsync(voice);
            break;
          case "remove":
            if (ViewModel.RemoveVoiceCommand.CanExecute(voice))
            {
              await ViewModel.RemoveVoiceCommand.ExecuteAsync(voice);

              // Undo/redo is handled by RemoveVoiceCommand via RemoveEnsembleVoiceAction
              _toastService?.ShowToast(ToastType.Success, "Removed", "Voice removed from ensemble");
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private async System.Threading.Tasks.Task HandleJobMenuClick(string action, EnsembleJobItem job)
    {
      try
      {
        switch (action.ToLower())
        {
          case "view details":
            _toastService?.ShowToast(ToastType.Info, "View Details", $"Job ID: {job.JobId}, Status: {job.Status}");
            break;
          case "export":
            _toastService?.ShowToast(ToastType.Info, "Export", $"Export functionality for job '{job.JobId}' is planned for a future release. Use the download button to save results.");
            break;
          case "delete":
            if (ViewModel.DeleteJobCommand.CanExecute(job))
            {
              var dialog = new ContentDialog
              {
                Title = "Delete Job",
                Content = "Are you sure you want to delete this ensemble synthesis job? This action cannot be undone.",
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
                      $"Delete Ensemble Job: {job.JobId}",
                      () => ViewModel.Jobs.Insert(jobIndex, jobToDelete),
                      () => ViewModel.Jobs.Remove(jobToDelete));
                  _undoRedoService.RegisterAction(actionObj);
                }

                _toastService?.ShowToast(ToastType.Success, "Deleted", "Deleted ensemble synthesis job");
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

    private System.Threading.Tasks.Task DuplicateVoiceAsync(EnsembleVoiceItem voice)
    {
      try
      {
        var duplicatedVoice = new EnsembleVoiceItem
        {
          ProfileId = voice.ProfileId,
          Engine = voice.Engine,
          Language = voice.Language,
          Emotion = voice.Emotion,
          Text = voice.Text
        };

        ViewModel.Voices.Add(duplicatedVoice);

        // Register undo action
        if (_undoRedoService != null)
        {
          var actionObj = new SimpleAction(
              "Duplicate Voice",
              () => ViewModel.Voices.Remove(duplicatedVoice),
              () => ViewModel.Voices.Add(duplicatedVoice));
          _undoRedoService.RegisterAction(actionObj);
        }

        _toastService?.ShowToast(ToastType.Success, "Duplicated", "Voice duplicated in ensemble");
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to duplicate voice: {ex.Message}");
      }

      return System.Threading.Tasks.Task.CompletedTask;
    }

    // Drag-and-drop handlers for voice item reordering
    private void Voice_DragStarting(UIElement sender, DragStartingEventArgs e)
    {
      if (sender is Border border && border.DataContext is ViewModels.EnsembleVoiceItem voice)
      {
        _draggedVoice = voice;
        _draggedVoiceIndex = ViewModel.Voices.IndexOf(voice);

        // Set drag data using voice index as identifier
        e.Data.SetText($"voice_{_draggedVoiceIndex}");
        e.Data.Properties.Add("VoiceIndex", _draggedVoiceIndex);
        e.Data.Properties.Add("VoiceText", voice.Text ?? "Unnamed Voice");

        // Reduce opacity of source element
        border.Opacity = 0.5;
      }
    }

    private void Voice_DragItemsCompleted(UIElement sender, DragItemsCompletedEventArgs e)
    {
      // Clean up drag state
      if (sender is Border border)
      {
        border.Opacity = 1.0;
      }

      _dragDropService?.Cleanup();

      _draggedVoice = null;
      _draggedVoiceIndex = -1;
    }

    private void Voice_DragOver(object sender, DragEventArgs e)
    {
      if (sender is Border border && _dragDropService != null)
      {
        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsGlyphVisible = false;
        e.DragUIOverride.IsContentVisible = false;

        // Show drop target indicator
        var position = e.GetPosition(border);
        var dropPosition = DetermineVoiceDropPosition(border, position);
        _dragDropService.ShowDropTargetIndicator(border, dropPosition);
      }
    }

    private void Voice_Drop(object sender, DragEventArgs e)
    {
      if (sender is Border border && _draggedVoice != null && _draggedVoiceIndex >= 0 && _dragDropService != null)
      {
        e.AcceptedOperation = DataPackageOperation.Move;

        // Hide drop indicator
        _dragDropService.HideDropTargetIndicator();
        _dragDropService.Cleanup();

        // Get target voice
        if (border.DataContext is ViewModels.EnsembleVoiceItem targetVoice)
        {
          var targetIndex = ViewModel.Voices.IndexOf(targetVoice);

          if (targetIndex >= 0 && _draggedVoiceIndex != targetIndex)
          {
            // Determine drop position
            var position = e.GetPosition(border);
            var dropPosition = DetermineVoiceDropPosition(border, position);

            // Reorder voices in the collection
            ViewModel.Voices.RemoveAt(_draggedVoiceIndex);

            if (dropPosition == DropPosition.Before)
            {
              var newIndex = targetIndex < _draggedVoiceIndex ? targetIndex : targetIndex;
              ViewModel.Voices.Insert(newIndex, _draggedVoice);
            }
            else if (dropPosition == DropPosition.After)
            {
              var newIndex = targetIndex < _draggedVoiceIndex ? targetIndex + 1 : targetIndex + 1;
              ViewModel.Voices.Insert(newIndex, _draggedVoice);
            }
            else
            {
              // On - replace target
              ViewModel.Voices.Insert(targetIndex, _draggedVoice);
            }

            _toastService?.ShowToast(ToastType.Success, "Reordered", "Voice order updated in ensemble");
          }
        }

        // Clean up drag state
        _draggedVoice = null;
        _draggedVoiceIndex = -1;

        // Restore source element opacity
        if (e.OriginalSource is Border sourceBorder)
        {
          sourceBorder.Opacity = 1.0;
        }
      }
    }

    private void Voice_DragLeave(object sender, DragEventArgs e)
    {
      _dragDropService?.HideDropTargetIndicator();
    }

    private DropPosition DetermineVoiceDropPosition(Border target, Windows.Foundation.Point position)
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

    private void EnsembleSynthesisView_KeyboardNavigation_Loaded(object sender, RoutedEventArgs e)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }
  }
}