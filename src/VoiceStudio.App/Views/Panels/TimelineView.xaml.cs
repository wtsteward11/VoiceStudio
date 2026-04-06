using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using VoiceStudio.App.Core.ErrorHandling;
using VoiceStudio.App.Logging;
using VoiceStudio.App.Services;
using MultiSelectSelectionChangedEventArgs = VoiceStudio.App.Services.SelectionChangedEventArgs;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.Core.Events;
using VoiceStudio.App.Core.Services;
using Windows.Foundation;
using Windows.System;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Core;
using Windows.UI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.App.Views.Panels
{
  public sealed partial class TimelineView : UserControl, INavigatablePanel
  {
    public TimelineViewModel ViewModel { get; }
    private bool _isDragging;
    private AudioClip? _draggedClip;

    private Storyboard? _playheadPulseAnimation;
    private DragDropVisualFeedbackService? _dragDropService;
    private IDragDropService? _panelDragDropService;
    private IEventAggregator? _eventAggregator;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;
    private IDialogService? _dialogService;
    private AudioClip? _clipboardClip; // For cut/copy/paste
    private KeyboardShortcutService? _keyboardShortcutService;
    private MultiSelectService? _multiSelectService;

    public TimelineView()
    {
      this.InitializeComponent();
      // Wire DataContext with synthesis service and timeline services
      ViewModel = new TimelineViewModel(
          AppServices.GetTimelineSynthesisService(),
          AppServices.GetTimelineClipService(),
          AppServices.GetTimelineTrackService(),
          AppServices.GetTimelineTranscriptionService(),
          AppServices.GetRequiredService<IProjectAudioClient>(),
          AppServices.GetRequiredService<IAudioVisualizationService>(),
          AppServices.GetRequiredService<IProjectsClient>(),
          AppServices.GetRequiredService<IProfilesClient>(),
          AppServices.GetAudioPlayerService(),
          AppServices.GetMultiSelectService(),
          AppServices.GetRequiredService<IDialogService>(),
          AppServices.TryGetToastNotificationService(),
          AppServices.TryGetUndoRedoService(),
          AppServices.TryGetErrorPresentationService(),
          AppServices.TryGetErrorLoggingService(),
          AppServices.GetService<ISettingsService>(),
          AppServices.TryGetRecentProjectsService(),
          AppServices.GetProjectSessionDirtyState(),
          projectRepository: AppServices.GetProjectRepository(),
          timelineUseCase: AppServices.GetTimelineUseCase()
      );
      this.DataContext = ViewModel;

      // Initialize services
      _dragDropService = AppServices.GetDragDropVisualFeedbackService();
      _panelDragDropService = AppServices.TryGetDragDropService();
      _eventAggregator = AppServices.TryGetEventAggregator();
      _toastService = AppServices.TryGetToastNotificationService();
      _undoRedoService = AppServices.TryGetUndoRedoService();
      _keyboardShortcutService = AppServices.TryGetKeyboardShortcutService();
      _dialogService = AppServices.TryGetDialogService();

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

      // Subscribe to preview state changes (Phase 3: named handlers for disposal)
      ViewModel.PropertyChanged += OnViewModelPropertyChanged;

      // Subscribe to selection changes (Phase 3: store ref for disposal)
      _multiSelectService = ServiceProvider.GetMultiSelectService();
      _multiSelectService.SelectionChanged += OnMultiSelectSelectionChanged;

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

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
      if (e.PropertyName == nameof(TimelineViewModel.PlayheadPulsing))
      {
        if (ViewModel.PlayheadPulsing)
          StartPlayheadPulse();
        else
          StopPlayheadPulse();
      }
      else if (e.PropertyName == nameof(TimelineViewModel.SelectedClipCount) ||
               e.PropertyName == nameof(TimelineViewModel.Tracks))
      {
        UpdateClipSelectionVisuals();
      }
    }

    private void OnMultiSelectSelectionChanged(object? sender, MultiSelectSelectionChangedEventArgs e)
    {
      if (e.PanelId == ViewModel.PanelId)
        UpdateClipSelectionVisuals();
    }

    private void TimelineView_Unloaded(object _, RoutedEventArgs __)
    {
      this.Unloaded -= TimelineView_Unloaded;
      _panelDragDropService?.UnregisterDropTarget(ViewModel.PanelId);
      if (_multiSelectService != null)
      {
        _multiSelectService.SelectionChanged -= OnMultiSelectSelectionChanged;
        _multiSelectService = null;
      }
      if (ViewModel != null)
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
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
    /// GAP-032: audio library assets route through <see cref="AddToTimelineEvent"/> (same authority as Library context menu).
    /// </summary>
    private async Task<DropResult> HandleCrossPanelDropAsync(DragPayload payload, CancellationToken cancellationToken)
    {
      _ = cancellationToken;
      var panelId = ViewModel.PanelId;
      if (_eventAggregator == null)
      {
        _toastService?.ShowToast(ToastType.Warning, "Drop Failed", "Timeline cannot receive library drops (events unavailable).");
        return new DropResult { Success = false, TargetPanelId = panelId, ErrorMessage = "Event aggregator unavailable" };
      }

      switch (payload.PayloadType)
      {
        case DragPayloadType.Asset:
          var anyAssetPublished = false;
          foreach (var item in payload.Items)
          {
            if (!IsLibraryAudioAssetForTimeline(item))
            {
              _toastService?.ShowToast(
                  ToastType.Warning,
                  "Timeline",
                  $"Only audio library assets can be added to the timeline ({item.DisplayName}).");
              continue;
            }

            var duration = TimeSpan.FromSeconds(GetDurationSecondsFromDragItem(item));
            var path = ResolveLibraryAssetPathForTimelineHandoff(item);
            var clipName = string.IsNullOrWhiteSpace(item.DisplayName) ? "Library audio" : item.DisplayName;

            _eventAggregator.Publish(new AddToTimelineEvent(
                payload.SourcePanelId,
                item.Id,
                path,
                duration,
                clipName,
                targetTrackIndex: null,
                insertPosition: null,
                profileId: null));
            anyAssetPublished = true;
            _toastService?.ShowToast(ToastType.Success, "Timeline", $"'{clipName}' sent to Timeline");
          }
          if (payload.Items.Count > 0 && !anyAssetPublished)
          {
            return new DropResult
            {
              Success = false,
              TargetPanelId = panelId,
              ErrorMessage = "No eligible audio assets in drop payload",
            };
          }
          return new DropResult { Success = true, TargetPanelId = panelId, Action = nameof(AddToTimelineEvent) };

        case DragPayloadType.Profile:
          var profileItem = payload.Items.FirstOrDefault();
          if (profileItem == null || string.IsNullOrEmpty(profileItem.Id))
          {
            _toastService?.ShowToast(ToastType.Warning, "Timeline", "Drop did not include a profile id.");
            return new DropResult { Success = false, TargetPanelId = panelId, ErrorMessage = "Missing profile" };
          }
          _eventAggregator.Publish(new ProfileSelectedEvent(
              payload.SourcePanelId,
              profileItem.Id,
              profileItem.DisplayName,
              InteractionIntent.ImmediateUse));
          _toastService?.ShowToast(
              ToastType.Info,
              "Profile",
              $"Profile '{profileItem.DisplayName}' selected (context).");
          return new DropResult { Success = true, TargetPanelId = panelId, Action = nameof(ProfileSelectedEvent) };

        case DragPayloadType.ExternalFile:
          foreach (var item in payload.Items)
          {
            _toastService?.ShowToast(ToastType.Info, "Import Started", $"Importing '{item.DisplayName}' is not wired in this lane — use Library import.");
          }
          return new DropResult { Success = false, TargetPanelId = panelId, ErrorMessage = "External file import deferred" };

        case DragPayloadType.TimelineClip:
          await Task.CompletedTask.ConfigureAwait(true);
          return new DropResult { Success = true, TargetPanelId = panelId, Action = "IgnoredTimelineClip" };

        default:
          await Task.CompletedTask.ConfigureAwait(true);
          return new DropResult { Success = false, TargetPanelId = panelId, ErrorMessage = "Unsupported payload" };
      }
    }

    private static bool IsLibraryAudioAssetForTimeline(DragItem item)
    {
      string? t = null;
      if (item.Metadata != null && item.Metadata.TryGetValue("AssetType", out var raw))
        t = raw?.ToString();
      if (string.IsNullOrEmpty(t))
        return true;
      var voiceTypes = new[] { "voice", "voice_profile", "profile", "clone", "xtts", "rvc" };
      if (voiceTypes.Contains(t.ToLowerInvariant()))
        return false;
      var audioTypes = new[] { "audio", "wav", "mp3", "flac", "ogg", "m4a", "recording" };
      return audioTypes.Contains(t.ToLowerInvariant());
    }

    private static double GetDurationSecondsFromDragItem(DragItem item)
    {
      if (item.Metadata == null || !item.Metadata.TryGetValue("DurationSeconds", out var raw))
        return 0.01;
      switch (raw)
      {
        case double d:
          return d > 0 ? d : 0.01;
        case float f:
          return f > 0 ? f : 0.01;
        case int i:
          return i > 0 ? i : 0.01;
        default:
          if (double.TryParse(raw?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var p))
            return p > 0 ? p : 0.01;
          return 0.01;
      }
    }

    private static string ResolveLibraryAssetPathForTimelineHandoff(DragItem item)
    {
      if (item.Metadata != null && item.Metadata.TryGetValue("FilePath", out var fp) && fp != null)
      {
        var local = fp.ToString();
        if (!string.IsNullOrWhiteSpace(local) && System.IO.File.Exists(local))
          return local;
      }
      var baseUrl = AppServices.GetService<BackendClientConfig>()?.BaseUrl?.TrimEnd('/')
          ?? BackendClientConfig.DefaultHttpBaseUrl;
      return $"{baseUrl}/api/audio/file/{Uri.EscapeDataString(item.Id)}";
    }

    private void TimelineView_Root_DragOver(object sender, DragEventArgs e)
    {
      _ = sender;
      if (_panelDragDropService is { IsDragging: true } && _panelDragDropService.CanDrop(ViewModel.PanelId))
      {
        e.AcceptedOperation = DataPackageOperation.Copy;
        _panelDragDropService.UpdateDragTarget(ViewModel.PanelId);
        e.Handled = true;
      }
    }

    private async void TimelineView_Root_Drop(object sender, DragEventArgs e)
    {
      _ = sender;
      if (_panelDragDropService is { IsDragging: true } && _panelDragDropService.CanDrop(ViewModel.PanelId))
      {
        _ = await _panelDragDropService.ExecuteDropAsync(
            ViewModel.PanelId,
            HandleCrossPanelDropAsync,
            CancellationToken.None).ConfigureAwait(true);
        e.Handled = true;
      }
      await Task.CompletedTask.ConfigureAwait(true);
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
          // Slice 3: completion callback may not run if preview was cancelled — clear VM flag deterministically.
          ViewModel.IsPreviewing = false;
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
          case "split at playhead":
            await ViewModel.SplitClipAtPlayheadAsync(clip);
            break;
          case "trim start to playhead":
            await ViewModel.TrimClipStartToPlayheadAsync(clip);
            break;
          case "trim end to playhead":
            await ViewModel.TrimClipEndToPlayheadAsync(clip);
            break;
          case "fade in 0.5s":
            await ViewModel.SetClipFadeAsync(clip, fadeInSeconds: 0.5, fadeOutSeconds: clip.FadeOutSeconds);
            break;
          case "fade out 0.5s":
            await ViewModel.SetClipFadeAsync(clip, fadeInSeconds: clip.FadeInSeconds, fadeOutSeconds: 0.5);
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

        if (_clipboardClip == null)
        {
          _toastService?.ShowToast(ToastType.Warning, "Paste", "No clip in clipboard");
          return;
        }

        if (ViewModel.PasteClipCommand.CanExecute(_clipboardClip))
        {
          await ViewModel.PasteClipCommand.ExecuteAsync(_clipboardClip);
        }
        else
        {
          _toastService?.ShowToast(ToastType.Warning, "Paste", "No track or project selected");
        }
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
        if (ViewModel.DuplicateClipCommand.CanExecute(clip))
        {
          await ViewModel.DuplicateClipCommand.ExecuteAsync(clip);
        }
        else
        {
          _toastService?.ShowToast(ToastType.Warning, "Duplicate", "No track selected");
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Duplicate Error", $"Failed to duplicate clip: {ex.Message}");
      }
    }

    private async void ShowClipProperties(AudioClip clip)
    {
      try
      {
        var message = FormatClipPropertiesMessage(clip);
        if (_dialogService != null)
          await _dialogService.ShowMessageAsync($"Properties: {clip.Name}", message);
        else
          _toastService?.ShowToast(ToastType.Info, "Properties", message);
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to show properties: {ex.Message}");
      }
    }

    private static string FormatClipPropertiesMessage(AudioClip clip)
    {
      var props = new[]
      {
        ("Name", clip.Name),
        ("ID", clip.Id),
        ("Profile ID", clip.ProfileId),
        ("Audio ID", clip.AudioId),
        ("Audio URL", clip.AudioUrl ?? "N/A"),
        ("Duration", clip.Duration.ToString(@"hh\:mm\:ss\.fff")),
        ("Start Time", $"{clip.StartTime:F2}s"),
        ("End Time", $"{clip.EndTime:F2}s"),
        ("Engine", clip.Engine ?? "N/A"),
        ("Quality Score", clip.QualityScore?.ToString("F2") ?? "N/A")
      };
      return string.Join("\n", props.Select(p => $"{p.Item1}: {p.Item2}"));
    }

    private async Task DeleteClipAsync(AudioClip clip, bool showToast = true)
    {
      try
      {
        await ViewModel.DeleteClipAsync(clip, showConfirmation: showToast);
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Delete", $"Failed to delete clip: {ex.Message}");
      }
    }

    private async void HandleTrackMenuClick(string action, AudioTrack track)
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
            await ViewModel.PersistTrackMixStateAsync(track).ConfigureAwait(true);
            break;
          case "solo":
            track.IsSolo = !track.IsSolo;
            await ViewModel.PersistTrackMixStateAsync(track).ConfigureAwait(true);
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

    /// <inheritdoc />
    public async Task<bool> NavigateToItemAsync(
        string itemId,
        string resultType,
        CancellationToken ct,
        IReadOnlyDictionary<string, object>? searchMetadata = null)
    {
      var type = resultType?.ToLowerInvariant() ?? string.Empty;
      if (type == "project")
        return await ViewModel.NavigateToProjectAsync(itemId, ct);
      if (type == "project_audio" || type == "audio")
      {
        // Audio ID may be "projectId:filename"; extract project ID for navigation
        var projectId = itemId.Contains(':') ? itemId.Substring(0, itemId.IndexOf(':')) : itemId;
        return await ViewModel.NavigateToProjectAsync(projectId, ct);
      }

      if (type == "marker")
      {
        string? projectId = null;
        if (searchMetadata != null && searchMetadata.TryGetValue("project_id", out var pid) && pid != null)
          projectId = pid.ToString();
        if (!string.IsNullOrEmpty(projectId))
          return await ViewModel.NavigateToProjectAsync(projectId, ct);
        return false;
      }

      return false;
    }
  }
}