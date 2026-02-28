using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using VoiceStudio.App.Core.ErrorHandling;
using VoiceStudio.App.Logging;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using Windows.Foundation;
using Windows.System;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Core;
using Windows.UI;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.App.Views.Panels
{
  public sealed partial class TimelineView : UserControl
  {
    public TimelineViewModel ViewModel { get; }
    private bool _isDragging;
    private AudioClip? _draggedClip;

    private Storyboard? _playheadPulseAnimation;
    private DragDropVisualFeedbackService? _dragDropService;
    private IDragDropService? _panelDragDropService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;
    private AudioClip? _clipboardClip; // For cut/copy/paste
    private KeyboardShortcutService? _keyboardShortcutService;

    public TimelineView()
    {
      this.InitializeComponent();
      // Wire DataContext with BackendClient and AudioPlayerService
      ViewModel = new TimelineViewModel(
          AppServices.GetBackendClient(),
          AppServices.GetAudioPlayerService(),
          AppServices.GetMultiSelectService(),
          AppServices.TryGetToastNotificationService(),
          AppServices.TryGetUndoRedoService(),
          AppServices.TryGetErrorPresentationService(),
          AppServices.TryGetErrorLoggingService(),
          AppServices.GetService<ISettingsService>(),
          AppServices.TryGetRecentProjectsService()
      );
      this.DataContext = ViewModel;

      // Initialize services
      _dragDropService = AppServices.GetDragDropVisualFeedbackService();
      _panelDragDropService = AppServices.TryGetDragDropService();
      _toastService = AppServices.TryGetToastNotificationService();
      _undoRedoService = AppServices.TryGetUndoRedoService();
      _keyboardShortcutService = AppServices.TryGetKeyboardShortcutService();

      // Register keyboard shortcuts
      if (_keyboardShortcutService != null)
      {
        RegisterKeyboardShortcuts();
      }

      // Set up playhead pulsing animation for preview
      SetupPlayheadAnimation();

      // Setup keyboard navigation
      this.Loaded += TimelineView_Loaded;
      this.Unloaded += TimelineView_Unloaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        // Close any open dialogs or overlays
        // Help overlay handling can be added if needed
      });

      // Subscribe to preview state changes
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(TimelineViewModel.PlayheadPulsing))
        {
          if (ViewModel.PlayheadPulsing)
          {
            StartPlayheadPulse();
          }
          else
          {
            StopPlayheadPulse();
          }
        }
        else if (e.PropertyName == nameof(TimelineViewModel.SelectedClipCount) ||
                       e.PropertyName == nameof(TimelineViewModel.Tracks))
        {
          UpdateClipSelectionVisuals();
        }
      };

      // Subscribe to selection changes
      var multiSelectService = ServiceProvider.GetMultiSelectService();
      multiSelectService.SelectionChanged += (s, e) =>
      {
        if (e.PanelId == ViewModel.PanelId)
        {
          UpdateClipSelectionVisuals();
        }
      };

      // Handle keyboard shortcuts
      this.KeyDown += TimelineView_KeyDown;

      // Initialize drag-and-drop visual feedback service
      _dragDropService = ServiceProvider.GetDragDropVisualFeedbackService();
    }

    private void TimelineView_Loaded(object _, RoutedEventArgs __)
    {
      // Setup Tab navigation order for this panel
      KeyboardNavigationHelper.SetupTabNavigation(this, 0);

      // Configure virtualization for tracks list to optimize large projects
      Controls.VirtualizedListHelper.ConfigureListView(TracksListView);

      // Register as drop target for Asset and Profile payloads (Panel Architecture Phase 4)
      _panelDragDropService?.RegisterDropTarget(
          ViewModel.PanelId,
          CanAcceptCrossPanelDrop);
    }

    private void TimelineView_Unloaded(object _, RoutedEventArgs __)
    {
      // Unregister from drop target (Panel Architecture Phase 4)
      _panelDragDropService?.UnregisterDropTarget(ViewModel.PanelId);
    }

    /// <summary>
    /// Determines if this panel can accept a cross-panel drag payload.
    /// Accepts Assets (audio) and Profiles for timeline operations.
    /// </summary>
    private static bool CanAcceptCrossPanelDrop(DragPayload payload)
    {
      return payload.PayloadType == DragPayloadType.Asset ||
             payload.PayloadType == DragPayloadType.Profile ||
             payload.PayloadType == DragPayloadType.TimelineClip ||
             payload.PayloadType == DragPayloadType.ExternalFile;
    }

    /// <summary>
    /// Handles a dropped Asset, Profile, or external file payload from cross-panel drag.
    /// </summary>
    private async Task HandleCrossPanelDropAsync(DragPayload payload, CancellationToken cancellationToken)
    {
      if (ViewModel.SelectedTrack == null || ViewModel.SelectedProject == null)
      {
        _toastService?.ShowToast(ToastType.Warning, "Drop Failed", "Select a track first to add clips");
        return;
      }

      switch (payload.PayloadType)
      {
        case DragPayloadType.Asset:
          foreach (var item in payload.Items)
          {
            // Create clip from asset
            var clip = new AudioClip
            {
              Id = Guid.NewGuid().ToString(),
              Name = item.DisplayName,
              AudioId = item.Id,
              StartTime = ViewModel.SelectedTrack.Clips.Count > 0
                  ? ViewModel.SelectedTrack.Clips.Max(c => c.EndTime)
                  : 0.0,
              Duration = TimeSpan.FromSeconds(5), // Default duration, will be updated by backend
            };

            ViewModel.SelectedTrack.Clips.Add(clip);
            _toastService?.ShowToast(ToastType.Success, "Asset Added", $"Added '{item.DisplayName}' to timeline");
          }
          break;

        case DragPayloadType.Profile:
          // When a profile is dropped, it could set the default voice for new clips
          var profileId = payload.Items.FirstOrDefault()?.Id;
          if (!string.IsNullOrEmpty(profileId))
          {
            _toastService?.ShowToast(ToastType.Info, "Profile Selected", $"Profile '{payload.Items.First().DisplayName}' selected for new clips");
          }
          break;

        case DragPayloadType.ExternalFile:
          foreach (var item in payload.Items)
          {
            // Import external audio file
            _toastService?.ShowToast(ToastType.Info, "Import Started", $"Importing '{item.DisplayName}'...");
          }
          break;
      }

      await Task.CompletedTask;
    }

    private void RegisterKeyboardShortcuts()
    {
      if (_keyboardShortcutService == null) return;

      _keyboardShortcutService.RegisterShortcut(
          "timeline_play_pause",
          VirtualKey.Space,
          VirtualKeyModifiers.None,
          () =>
          {
            if (ViewModel.IsPlaying)
            {
              if (ViewModel.PauseAudioCommand.CanExecute(null)) ViewModel.PauseAudioCommand.Execute(null);
            }
            else
            {
              if (ViewModel.PlayAudioCommand.CanExecute(null)) ViewModel.PlayAudioCommand.Execute(null);
            }
          },
          "Play/Pause timeline"
      );

      _keyboardShortcutService.RegisterShortcut(
          "timeline_stop",
          VirtualKey.S,
          VirtualKeyModifiers.None,
          () => { if (ViewModel.StopAudioCommand.CanExecute(null)) ViewModel.StopAudioCommand.Execute(null); },
          "Stop timeline playback"
      );

      _keyboardShortcutService.RegisterShortcut(
          "timeline_add_track",
          VirtualKey.T,
          VirtualKeyModifiers.Control,
          () => { if (ViewModel.AddTrackCommand.CanExecute(null)) ViewModel.AddTrackCommand.Execute(null); },
          "Add new track"
      );

      _keyboardShortcutService.RegisterShortcut(
          "timeline_delete_clips",
          VirtualKey.Delete,
          VirtualKeyModifiers.None,
          () => { if (ViewModel.DeleteSelectedClipsCommand.CanExecute(null)) ViewModel.DeleteSelectedClipsCommand.Execute(null); },
          "Delete selected clips"
      );

      _keyboardShortcutService.RegisterShortcut(
          "timeline_zoom_in",
          VirtualKey.Add,
          VirtualKeyModifiers.Control,
          () => ViewModel.ZoomInCommand.Execute(null),
          "Zoom in timeline"
      );

      _keyboardShortcutService.RegisterShortcut(
          "timeline_zoom_out",
          VirtualKey.Subtract,
          VirtualKeyModifiers.Control,
          () => ViewModel.ZoomOutCommand.Execute(null),
          "Zoom out timeline"
      );
    }

    private void HelpButton_Click(object _, RoutedEventArgs __)
    {
      HelpOverlay.Title = "Timeline Help";
      HelpOverlay.HelpText = "The Timeline panel is your main workspace for arranging and editing audio clips. Add tracks, place clips on the timeline, and arrange them in time. Use the playhead to navigate and preview your composition. Zoom controls help you work at different time scales. Multi-select clips with Ctrl+Click or Shift+Click to perform batch operations. Drag clips to reposition them on the timeline.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Space", Description = "Play/Pause timeline" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+A", Description = "Select all clips" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+C", Description = "Copy selected clips" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+V", Description = "Paste clips" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+X", Description = "Cut selected clips" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Delete", Description = "Delete selected clips" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+Z", Description = "Undo" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+Y", Description = "Redo" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl++", Description = "Zoom in" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+-", Description = "Zoom out" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Add tracks to organize your audio into separate layers");
      HelpOverlay.Tips.Add("Drag clips from the library onto tracks to place them");
      HelpOverlay.Tips.Add("Use multi-select (Ctrl+Click or Shift+Click) to select multiple clips");
      HelpOverlay.Tips.Add("Right-click clips or tracks for context menus with more options");
      HelpOverlay.Tips.Add("The playhead shows the current playback position");
      HelpOverlay.Tips.Add("Zoom in/out to work at different time scales");
      HelpOverlay.Tips.Add("Clips can be dragged to reposition them on the timeline");
      HelpOverlay.Tips.Add("Use undo/redo (Ctrl+Z/Ctrl+Y) to revert changes");

      HelpOverlay.Visibility = Visibility.Visible;
      HelpOverlay.Show();
    }

    private void SetupPlayheadAnimation()
    {
      // Create pulsing animation (opacity 0.6 to 1.0, repeating)
      var animation = new DoubleAnimation
      {
        From = 0.6,
        To = 1.0,
        Duration = new Microsoft.UI.Xaml.Duration(TimeSpan.FromMilliseconds(500)),
        AutoReverse = true,
        RepeatBehavior = RepeatBehavior.Forever
      };

      Storyboard.SetTarget(animation, PlayheadLine);
      Storyboard.SetTargetProperty(animation, "Opacity");

      _playheadPulseAnimation = new Storyboard();
      _playheadPulseAnimation.Children.Add(animation);
    }

    private void StartPlayheadPulse()
    {
      _playheadPulseAnimation?.Begin();
    }

    private void StopPlayheadPulse()
    {
      _playheadPulseAnimation?.Stop();
      if (PlayheadLine != null)
      {
        PlayheadLine.Opacity = 0.9;
      }
    }

    private async void LoadAudioFileButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
      try
      {
        if (sender is Button button && button.DataContext is ProjectAudioFile audioFile)
        {
          await ViewModel.LoadAudioFileIntoClipCommand.ExecuteAsync(audioFile);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private async void PlayAudioFile_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
      try
      {
        if (sender is Button button && button.Tag is string filename)
        {
          await ViewModel.PlayProjectAudioCommand.ExecuteAsync(filename);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private void TimelineScrubCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
      _isDragging = true;
      HandleTimelineScrub(e);
      TimelineScrubCanvas.CapturePointer(e.Pointer);
    }

    private void TimelineScrubCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
      if (_isDragging)
      {
        HandleTimelineScrub(e);
      }
    }

    private void TimelineScrubCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
      if (_isDragging)
      {
        HandleTimelineScrub(e);
        _isDragging = false;
        TimelineScrubCanvas.ReleasePointerCapture(e.Pointer);

        // Stop preview when scrubbing ends
        if (ViewModel?.IsPreviewing == true)
        {
          var audioPlayerService = ServiceProvider.GetAudioPlayerService() as AudioPlayerService;
          audioPlayerService?.StopPreview();
        }
      }
    }

    private void HandleTimelineScrub(PointerRoutedEventArgs e)
    {
      var point = e.GetCurrentPoint(TimelineScrubCanvas);
      var pixelPosition = point.Position.X;

      // Execute seek command with pixel position
      ViewModel.SeekToPositionCommand.Execute(pixelPosition);
    }

    private void Clip_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is Border border && border.DataContext is AudioClip clip)
      {
        e.Handled = true;
        var menuService = ServiceProvider.GetContextMenuService();
        var menu = menuService.CreateContextMenu("clip", clip);

        // Wire up menu item commands
        WireUpClipMenuCommands(menu, clip);

        var position = e.GetPosition(border);
        menuService.ShowContextMenu(menu, border, position);
      }
    }

    private void Track_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var track = element.DataContext as AudioTrack ??
                   (listView.SelectedItem as AudioTrack);

        if (track != null)
        {
          e.Handled = true;
          var menuService = ServiceProvider.GetContextMenuService();
          var menu = menuService.CreateContextMenu("track", track);

          // Wire up menu item commands
          WireUpTrackMenuCommands(menu, track);

          var position = e.GetPosition(listView);
          menuService.ShowContextMenu(menu, listView, position);
        }
      }
    }

    private void TimelineEmptyArea_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      e.Handled = true;
      var menuService = ServiceProvider.GetContextMenuService();
      var menu = menuService.CreateContextMenu("timeline", null);

      // Wire up menu item commands for empty area
      WireUpTimelineMenuCommands(menu);

      var position = e.GetPosition(TimelineScrubCanvas);
      menuService.ShowContextMenu(menu, TimelineScrubCanvas, position);
    }

    private void WireUpClipMenuCommands(MenuFlyout menu, AudioClip clip)
    {
      foreach (var item in menu.Items)
      {
        if (item is MenuFlyoutItem menuItem)
        {
          menuItem.Click += (_, _) => HandleClipMenuClick(menuItem.Text, clip);
        }
      }
    }

    private void WireUpTrackMenuCommands(MenuFlyout menu, AudioTrack track)
    {
      foreach (var item in menu.Items)
      {
        if (item is MenuFlyoutItem menuItem)
        {
          menuItem.Click += (_, _) => HandleTrackMenuClick(menuItem.Text, track);
        }
        else if (item is ToggleMenuFlyoutItem toggleItem)
        {
          toggleItem.IsChecked = track.IsMuted || track.IsSolo; // Update based on track state
          toggleItem.Click += (_, _) => HandleTrackMenuClick(toggleItem.Text, track);
        }
      }
    }

    private void WireUpTimelineMenuCommands(MenuFlyout menu)
    {
      foreach (var item in menu.Items)
      {
        if (item is MenuFlyoutItem menuItem)
        {
          menuItem.Click += (_, _) => HandleTimelineMenuClick(menuItem.Text);
        }
      }
    }

    private async void HandleClipMenuClick(string action, AudioClip clip)
    {
      try
      {
        switch (action.ToLower())
        {
          case "cut":
            await CutClipAsync(clip);
            break;
          case "copy":
            await CopyClipAsync(clip);
            break;
          case "paste":
            await PasteClipAsync();
            break;
          case "duplicate":
            await DuplicateClipAsync(clip);
            break;
          case "properties":
            ShowClipProperties(clip);
            break;
          case "delete":
            await DeleteClipAsync(clip);
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
        System.Diagnostics.Debug.WriteLine($"Error handling clip menu action '{action}': {ex.Message}");
      }
    }

    private async Task CutClipAsync(AudioClip clip)
    {
      try
      {
        // Copy to clipboard
        await CopyClipAsync(clip);

        // Delete the clip
        await DeleteClipAsync(clip, showToast: false);

        _toastService?.ShowToast(ToastType.Success, "Cut", $"Cut '{clip.Name}' to clipboard");
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Cut Error", $"Failed to cut clip: {ex.Message}");
      }
    }

    private Task CopyClipAsync(AudioClip clip)
    {
      try
      {
        // Store clip in memory clipboard
        _clipboardClip = new AudioClip
        {
          Id = Guid.NewGuid().ToString(), // New ID for pasted clip
          Name = clip.Name,
          ProfileId = clip.ProfileId,
          AudioId = clip.AudioId,
          AudioUrl = clip.AudioUrl,
          Duration = clip.Duration,
          StartTime = clip.StartTime,
          Engine = clip.Engine,
          QualityScore = clip.QualityScore,
          WaveformSamples = clip.WaveformSamples
        };

        // Also copy to system clipboard as JSON
        var dataPackage = new DataPackage();
        dataPackage.SetText(System.Text.Json.JsonSerializer.Serialize(_clipboardClip));
        Clipboard.SetContent(dataPackage);

        _toastService?.ShowToast(ToastType.Success, "Copied", $"Copied '{clip.Name}' to clipboard");
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Copy Error", $"Failed to copy clip: {ex.Message}");
      }

      return Task.CompletedTask;
    }

    private async Task PasteClipAsync()
    {
      try
      {
        if (_clipboardClip == null)
        {
          // Try to get from system clipboard
          var clipboardContent = Clipboard.GetContent();
          if (clipboardContent.Contains(StandardDataFormats.Text))
          {
            var text = await clipboardContent.GetTextAsync();
            try
            {
              _clipboardClip = System.Text.Json.JsonSerializer.Deserialize<AudioClip>(text);
            }
            catch
            {
              _toastService?.ShowToast(ToastType.Warning, "Paste", "No clip in clipboard");
              return;
            }
          }
          else
          {
            _toastService?.ShowToast(ToastType.Warning, "Paste", "No clip in clipboard");
            return;
          }
        }

        if (_clipboardClip == null || ViewModel.SelectedTrack == null || ViewModel.SelectedProject == null)
        {
          _toastService?.ShowToast(ToastType.Warning, "Paste", "No track or project selected");
          return;
        }

        // Create new clip with new ID
        var pastedClip = new AudioClip
        {
          Id = Guid.NewGuid().ToString(),
          Name = _clipboardClip.Name + " (Copy)",
          ProfileId = _clipboardClip.ProfileId,
          AudioId = _clipboardClip.AudioId,
          AudioUrl = _clipboardClip.AudioUrl,
          Duration = _clipboardClip.Duration,
          StartTime = ViewModel.SelectedTrack.Clips.Count > 0
                ? ViewModel.SelectedTrack.Clips.Max(c => c.EndTime)
                : 0.0,
          Engine = _clipboardClip.Engine,
          QualityScore = _clipboardClip.QualityScore,
          WaveformSamples = _clipboardClip.WaveformSamples
        };

        // Add undo/redo action
        _undoRedoService?.AddAction(
            "Paste Clip",
            () =>
            {
              // Undo: Remove the pasted clip
              ViewModel.SelectedTrack.Clips.Remove(pastedClip);
              if (ViewModel.SelectedProject != null)
              {
                var backendClient = ServiceProvider.GetBackendClient();
                _ = backendClient.DeleteClipAsync(ViewModel.SelectedProject.Id, ViewModel.SelectedTrack.Id, pastedClip.Id);
              }
            },
            async () =>
            {
              // Redo: Add the clip back
              ViewModel.SelectedTrack.Clips.Add(pastedClip);
              if (ViewModel.SelectedProject != null)
              {
                try
                {
                  var backendClient = ServiceProvider.GetBackendClient();
                  await backendClient.CreateClipAsync(
                              ViewModel.SelectedProject.Id,
                              ViewModel.SelectedTrack.Id,
                              pastedClip
                          );
                }
                catch (Exception ex) { ErrorLogger.LogWarning($"Undo paste sync failed: {ex.Message}", "TimelineView"); }
              }
            }
        );

        // Save to backend
        try
        {
          var backendClient = ServiceProvider.GetBackendClient();
          pastedClip = await backendClient.CreateClipAsync(
              ViewModel.SelectedProject.Id,
              ViewModel.SelectedTrack.Id,
              pastedClip
          );
        }
        catch (Exception ex)
        {
          _toastService?.ShowToast(ToastType.Warning, "Paste Warning", $"Clip pasted locally. Backend save failed: {ex.Message}");
        }

        ViewModel.SelectedTrack.Clips.Add(pastedClip);
        _toastService?.ShowToast(ToastType.Success, "Pasted", $"Pasted '{pastedClip.Name}'");
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Paste Error", $"Failed to paste clip: {ex.Message}");
      }
    }

    private async Task DuplicateClipAsync(AudioClip clip)
    {
      try
      {
        if (ViewModel.SelectedTrack == null || ViewModel.SelectedProject == null)
        {
          _toastService?.ShowToast(ToastType.Warning, "Duplicate", "No track selected");
          return;
        }

        // Create duplicate with new ID
        var duplicatedClip = new AudioClip
        {
          Id = Guid.NewGuid().ToString(),
          Name = clip.Name + " (Copy)",
          ProfileId = clip.ProfileId,
          AudioId = clip.AudioId,
          AudioUrl = clip.AudioUrl,
          Duration = clip.Duration,
          StartTime = clip.EndTime + 0.1, // Place after original with small gap
          Engine = clip.Engine,
          QualityScore = clip.QualityScore,
          WaveformSamples = clip.WaveformSamples
        };

        // Add undo/redo action
        _undoRedoService?.AddAction(
            "Duplicate Clip",
            () =>
            {
              // Undo: Remove the duplicated clip
              ViewModel.SelectedTrack.Clips.Remove(duplicatedClip);
              if (ViewModel.SelectedProject != null)
              {
                var backendClient = ServiceProvider.GetBackendClient();
                _ = backendClient.DeleteClipAsync(ViewModel.SelectedProject.Id, ViewModel.SelectedTrack.Id, duplicatedClip.Id);
              }
            },
            async () =>
            {
              // Redo: Add the clip back
              ViewModel.SelectedTrack.Clips.Add(duplicatedClip);
              if (ViewModel.SelectedProject != null)
              {
                try
                {
                  var backendClient = ServiceProvider.GetBackendClient();
                  await backendClient.CreateClipAsync(
                              ViewModel.SelectedProject.Id,
                              ViewModel.SelectedTrack.Id,
                              duplicatedClip
                          );
                }
                catch (Exception ex) { ErrorLogger.LogWarning($"Undo duplicate sync failed: {ex.Message}", "TimelineView"); }
              }
            }
        );

        // Save to backend
        try
        {
          var backendClient = ServiceProvider.GetBackendClient();
          duplicatedClip = await backendClient.CreateClipAsync(
              ViewModel.SelectedProject.Id,
              ViewModel.SelectedTrack.Id,
              duplicatedClip
          );
        }
        catch (Exception ex)
        {
          _toastService?.ShowToast(ToastType.Warning, "Duplicate Warning", $"Clip duplicated locally. Backend save failed: {ex.Message}");
        }

        ViewModel.SelectedTrack.Clips.Add(duplicatedClip);
        _toastService?.ShowToast(ToastType.Success, "Duplicated", $"Duplicated '{clip.Name}'");
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Duplicate Error", $"Failed to duplicate clip: {ex.Message}");
      }
    }

    private void ShowClipProperties(AudioClip clip)
    {
      try
      {
        var dialog = new ContentDialog
        {
          Title = $"Properties: {clip.Name}",
          Content = CreateClipPropertiesContent(clip),
          CloseButtonText = "Close",
          XamlRoot = this.XamlRoot
        };

        _ = dialog.ShowAsync();
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to show properties: {ex.Message}");
      }
    }

    private UIElement CreateClipPropertiesContent(AudioClip clip)
    {
      var stackPanel = new StackPanel { Spacing = 8 };

      var properties = new[]
      {
                ("Name", clip.Name),
                ("ID", clip.Id),
                ("Profile ID", clip.ProfileId),
                ("Audio ID", clip.AudioId),
                ("Audio URL", clip.AudioUrl),
                ("Duration", clip.Duration.ToString(@"hh\:mm\:ss\.fff")),
                ("Start Time", $"{clip.StartTime:F2}s"),
                ("End Time", $"{clip.EndTime:F2}s"),
                ("Engine", clip.Engine ?? "N/A"),
                ("Quality Score", clip.QualityScore?.ToString("F2") ?? "N/A")
            };

      foreach (var (label, value) in properties)
      {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

        var labelText = new TextBlock
        {
          Text = $"{label}:",
          FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
          Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(labelText, 0);

        var valueText = new TextBlock
        {
          Text = value ?? "N/A",
          TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
        };
        Grid.SetColumn(valueText, 1);

        grid.Children.Add(labelText);
        grid.Children.Add(valueText);
        stackPanel.Children.Add(grid);
      }

      return new ScrollViewer
      {
        Content = stackPanel,
        MaxHeight = 400
      };
    }

    private async Task DeleteClipAsync(AudioClip clip, bool showToast = true)
    {
      try
      {
        if (ViewModel.SelectedTrack == null || ViewModel.SelectedProject == null)
        {
          if (showToast)
            _toastService?.ShowToast(ToastType.Warning, "Delete", "No track or project selected");
          return;
        }

        // Confirm deletion
        if (showToast)
        {
          var dialog = new ContentDialog
          {
            Title = "Delete Clip",
            Content = $"Are you sure you want to delete '{clip.Name}'? This action cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
          };

          var result = await dialog.ShowAsync();
          if (result != ContentDialogResult.Primary)
            return;
        }

        // Store clip for undo
        var clipToDelete = clip;
        var track = ViewModel.SelectedTrack;
        var project = ViewModel.SelectedProject;

        // Add undo/redo action
        _undoRedoService?.AddAction(
            "Delete Clip",
            async () =>
            {
              // Undo: Add the clip back
              track.Clips.Add(clipToDelete);
              try
              {
                var backendClient = ServiceProvider.GetBackendClient();
                await backendClient.CreateClipAsync(project.Id, track.Id, clipToDelete);
              }
              catch (Exception ex) { ErrorLogger.LogWarning($"Undo delete sync failed: {ex.Message}", "TimelineView"); }
            },
            async () =>
            {
              // Redo: Remove the clip again
              track.Clips.Remove(clipToDelete);
              try
              {
                var backendClient = ServiceProvider.GetBackendClient();
                await backendClient.DeleteClipAsync(project.Id, track.Id, clipToDelete.Id);
              }
              catch (Exception ex) { ErrorLogger.LogWarning($"Redo delete sync failed: {ex.Message}", "TimelineView"); }
            }
        );

        // Delete from backend
        try
        {
          var backendClient = ServiceProvider.GetBackendClient();
          await backendClient.DeleteClipAsync(project.Id, track.Id, clip.Id);
        }
        catch (Exception ex)
        {
          _toastService?.ShowToast(ToastType.Warning, "Delete Warning", $"Clip removed locally. Backend delete failed: {ex.Message}");
        }

        // Remove from track
        track.Clips.Remove(clip);

        if (showToast)
          _toastService?.ShowToast(ToastType.Success, "Deleted", $"Deleted '{clip.Name}'");
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Delete Error", $"Failed to delete clip: {ex.Message}");
      }
    }

    private void HandleTrackMenuClick(string action, AudioTrack track)
    {
      try
      {
        switch (action.ToLower())
        {
          case "add clip":
            // Use existing AddClipToTrackCommand if available
            System.Diagnostics.Debug.WriteLine($"Add clip to track: {track.Name}");
            break;
          case "add effect":
            // Note: Add effect will be implemented when effect picker dialog is available
            System.Diagnostics.Debug.WriteLine($"Add effect to track: {track.Name}");
            break;
          case "mute":
            track.IsMuted = !track.IsMuted;
            System.Diagnostics.Debug.WriteLine($"Toggle mute for track: {track.Name}");
            break;
          case "solo":
            track.IsSolo = !track.IsSolo;
            System.Diagnostics.Debug.WriteLine($"Toggle solo for track: {track.Name}");
            break;
          case "rename":
            // Note: Track rename will be implemented when rename command is available
            System.Diagnostics.Debug.WriteLine($"Rename track: {track.Name}");
            break;
          case "delete":
            // Note: Track delete will be implemented when delete command is available
            System.Diagnostics.Debug.WriteLine($"Delete track: {track.Name}");
            break;
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Error handling track menu action '{action}': {ex.Message}");
      }
    }

    private async void HandleTimelineMenuClick(string action)
    {
      try
      {
        switch (action.ToLower())
        {
          case "add track":
            if (ViewModel.AddTrackCommand.CanExecute(null))
            {
              await ViewModel.AddTrackCommand.ExecuteAsync(null);
            }
            break;
          case "paste":
            await PasteClipAsync();
            break;
          case "zoom in":
            ViewModel.ZoomInCommand.Execute(null);
            break;
          case "zoom out":
            ViewModel.ZoomOutCommand.Execute(null);
            break;
          case "zoom to fit":
            // Note: Zoom to fit will be implemented when zoom command is available
            System.Diagnostics.Debug.WriteLine("Zoom to fit");
            break;
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Error handling timeline menu action '{action}': {ex.Message}");
      }
    }

    private void Clip_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
      if (sender is Border border && border.DataContext is AudioClip clip)
      {
        var pointerPoint = e.GetCurrentPoint(null);
        // Check keyboard modifiers using InputKeyboardSource
        var controlKeyState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        var shiftKeyState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        var isCtrlPressed = (controlKeyState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
        var isShiftPressed = (shiftKeyState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

        ViewModel.ToggleClipSelection(clip.Id, isCtrlPressed, isShiftPressed);

        UpdateClipSelectionVisuals();
        e.Handled = true;
      }
    }

    private void TimelineView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
      var isCtrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

      if (isCtrlPressed && e.Key == VirtualKey.A)
      {
        // Ctrl+A - Select all clips
        ViewModel.SelectAllClipsCommand.Execute(null);
        UpdateClipSelectionVisuals();
        e.Handled = true;
      }
      else if (e.Key == VirtualKey.Escape)
      {
        // Escape - Clear clip selection
        ViewModel.ClearClipSelectionCommand.Execute(null);
        UpdateClipSelectionVisuals();
        e.Handled = true;
      }
    }

    private void UpdateClipSelectionVisuals()
    {
      // Update visual indicators for all clip borders
      UpdateClipSelectionVisualsRecursive(this);
    }

    private void UpdateClipSelectionVisualsRecursive(DependencyObject element)
    {
      if (element == null || ViewModel == null)
        return;

      // Check if this is a clip border with a Tag (clip ID)
      if (element is Border border && border.Tag is string clipId)
      {
        var isSelected = ViewModel.IsClipSelected(clipId);

        // Find the selection indicator child border
        var selectionIndicator = FindChildBorder(border, "ClipSelectionIndicator");
        if (selectionIndicator != null)
        {
          selectionIndicator.Visibility = isSelected
              ? Microsoft.UI.Xaml.Visibility.Visible
              : Microsoft.UI.Xaml.Visibility.Collapsed;
        }

        // Update border brush to show selection
        if (isSelected)
        {
          border.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 183, 194)); // VSQ.Accent.Cyan
          border.BorderThickness = new Microsoft.UI.Xaml.Thickness(2);
        }
        else
        {
          border.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)this.Resources["VSQ.Accent.CyanBrush"];
          border.BorderThickness = new Microsoft.UI.Xaml.Thickness(1);
        }
      }

      // Recursively check children
      var childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(element);
      for (int i = 0; i < childCount; i++)
      {
        var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(element, i);
        UpdateClipSelectionVisualsRecursive(child);
      }
    }

    private static Border? FindChildBorder(DependencyObject? parent, string childName)
    {
      if (parent == null) return null;

      for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
      {
        var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);

        if (child is Border border && (child as FrameworkElement)?.Name == childName)
        {
          return border;
        }

        var foundChild = FindChildBorder(child, childName);
        if (foundChild != null)
        {
          return foundChild;
        }
      }

      return null;
    }

    private void Clip_DragStarting(UIElement sender, DragStartingEventArgs e)
    {
      if (sender is Border border && border.DataContext is AudioClip clip)
      {
        _draggedClip = clip;

        // Set drag data
        e.Data.SetText(clip.Id);
        e.Data.Properties.Add("ClipId", clip.Id);
        e.Data.Properties.Add("ClipName", clip.Name ?? "Unnamed Clip");

        // Reduce opacity of source element
        border.Opacity = 0.5;
      }
    }

    private void Clip_DragItemsCompleted(UIElement sender, DragItemsCompletedEventArgs e)
    {
      // Clean up drag state
      if (sender is Border border)
      {
        border.Opacity = 1.0;
      }

      if (_dragDropService != null)
      {
        _dragDropService.Cleanup();
        // Clear drag preview from canvas
        DragDropCanvas.Children.Clear();
      }

      _draggedClip = null;
    }

    private void Clip_DragOver(object sender, DragEventArgs e)
    {
      if (sender is Border border && _dragDropService != null)
      {
        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsGlyphVisible = false;
        e.DragUIOverride.IsContentVisible = false;

        // Show drop target indicator
        var position = e.GetPosition(border);
        var dropPosition = DetermineDropPosition(border, position);
        _dragDropService.ShowDropTargetIndicator(border, dropPosition);
      }
    }

    private void Clip_Drop(object sender, DragEventArgs e)
    {
      if (sender is Border border && _draggedClip != null && _dragDropService != null)
      {
        e.AcceptedOperation = DataPackageOperation.Move;

        // Hide drop indicator
        _dragDropService.HideDropTargetIndicator();
        _dragDropService.Cleanup();

        // Note: Clip reordering will be implemented when reorder command is available
        System.Diagnostics.Debug.WriteLine($"Drop clip {_draggedClip.Name} onto {border.DataContext}");

        // Clean up drag state
        _draggedClip = null;

        // Restore source element opacity
        if (e.OriginalSource is Border sourceBorder)
        {
          sourceBorder.Opacity = 1.0;
        }
      }
    }

    private void Clip_DragLeave(object sender, DragEventArgs e)
    {
      _dragDropService?.HideDropTargetIndicator();
    }

    private void TrackClipsArea_DragOver(object sender, DragEventArgs e)
    {
      if (sender is Border border && _dragDropService != null)
      {
        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsGlyphVisible = false;
        e.DragUIOverride.IsContentVisible = false;

        // Show drop target indicator for track area
        _dragDropService.ShowDropTargetIndicator(border, DropPosition.On);
      }
    }

    private void TrackClipsArea_Drop(object sender, DragEventArgs e)
    {
      if (sender is Border border && _draggedClip != null && _dragDropService != null)
      {
        e.AcceptedOperation = DataPackageOperation.Move;

        // Hide drop indicator
        _dragDropService.HideDropTargetIndicator();
        _dragDropService.Cleanup();

        // Get the track from the border's parent
        if (border.DataContext is AudioTrack track)
        {
          // Note: Adding clip to track will be implemented when AddClipToTrackCommand is available
          System.Diagnostics.Debug.WriteLine($"Drop clip {_draggedClip.Name} onto track {track.Name}");
        }

        // Clean up drag state
        _draggedClip = null;

        // Restore source element opacity
        if (e.OriginalSource is Border sourceBorder)
        {
          sourceBorder.Opacity = 1.0;
        }
      }
    }

    private void TrackClipsArea_DragLeave(object sender, DragEventArgs e)
    {
      _dragDropService?.HideDropTargetIndicator();
    }

    private DropPosition DetermineDropPosition(Border target, Point position)
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

    private void TranscriptSegment_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
      if (sender is Border border && border.DataContext is TranscriptSegmentDisplay segment)
      {
        // Seek to the transcript segment's start position (already computed in pixels)
        ViewModel.SeekToPositionCommand.Execute(segment.PositionPixels);
        e.Handled = true;
      }
    }
  }
}