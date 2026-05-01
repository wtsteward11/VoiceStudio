using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;
using VoiceStudio.App.Services;
using VoiceStudio.App.Core.Services;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.Core.Models;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Transcription;
using Windows.UI;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.App.UseCases;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace VoiceStudio.App.Views.Panels
{
  public sealed partial class TranscribeView : Microsoft.UI.Xaml.Controls.UserControl
  {
    public TranscribeViewModel ViewModel { get; }
    /// <summary>GAP-045 multi-segment: anchor for Shift+click range edit (first segment tapped).</summary>
    private VoiceStudio.Core.Models.TranscriptionSegment? _rangeEditAnchorSegment;
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;
    private DragDropVisualFeedbackService? _dragDropService;
    private TranscriptionResponse? _draggedTranscription;

    public TranscribeView()
    {
      this.InitializeComponent();
      ViewModel = new TranscribeViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetRequiredService<VoiceStudio.Core.Services.ITranscriptionClient>(),
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IProjectAudioClient>(),
          AppServices.GetProjectRepository(),
          AppServices.GetService<IShellProgressPublisher>() ?? NullShellProgressPublisher.Instance,
          AppServices.GetService<IDialogueServiceClient>(),
          AppServices.TryGetTimelineUseCase());
      this.DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();
      _dragDropService = ServiceProvider.GetDragDropVisualFeedbackService();

      // Track collection changes to toggle empty state visibility
      ViewModel.Transcriptions.CollectionChanged += (s, e) => UpdateEmptyStateVisibility();

      // Add keyboard handler for multi-select
      this.KeyDown += TranscribeView_KeyDown;

      // Setup keyboard navigation and initial data load (ADR-047)
      this.Loaded += TranscribeView_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        // Close any open dialogs or overlays
      });

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
      if (e.PropertyName == nameof(TranscribeViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
      {
        _toastService?.ShowToast(ToastType.Error, "Transcribe Error", ViewModel.ErrorMessage);
        return;
      }

      var name = e.PropertyName;
      if (name == nameof(TranscribeViewModel.LinkedTranscriptSegmentIds)
          || name == nameof(TranscribeViewModel.SelectedTranscription)
          || name == nameof(TranscribeViewModel.EditingSegmentId)
          || name == nameof(TranscribeViewModel.EditingRangeEndSegmentId)
          || name == nameof(TranscribeViewModel.RegeneratingSegmentId)
          || name == nameof(TranscribeViewModel.TranscriptSegmentLayoutRevision))
      {
        _ = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
          if (name == nameof(TranscribeViewModel.SelectedTranscription)
              || name == nameof(TranscribeViewModel.TranscriptSegmentLayoutRevision))
            RefreshTranscriptSegmentsItemsSource();
          RefreshSegmentRowVisuals();
        });
      }
    }

    private void TranscribeSegment_Tapped(object sender, TappedRoutedEventArgs e)
    {
      if (sender is FrameworkElement fe && fe.DataContext is TranscriptionSegment seg)
      {
        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
        if (shift && _rangeEditAnchorSegment != null)
        {
          ViewModel.BeginEditRange(_rangeEditAnchorSegment, seg);
          if (ViewModel.IsMultiSegmentRangeEdit)
            ShowSegmentTextEditFlyout(fe, seg);
          else if (!ViewModel.IsEditingSegment && !string.IsNullOrEmpty(ViewModel.TranscriptOperatorMessage))
          {
            _toastService?.ShowToast(ToastType.Warning, "Range edit", ViewModel.TranscriptOperatorMessage);
          }
        }
        else
        {
          ViewModel.OnTargetTranscriptionSegmentTapped(seg);
          _rangeEditAnchorSegment = seg;
        }

        _ = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, RefreshSegmentRowVisuals);
        e.Handled = true;
      }
    }

    private void TranscribeSegment_KeyDown(object sender, KeyRoutedEventArgs e)
    {
      if (sender is not FrameworkElement fe || fe.DataContext is not TranscriptionSegment seg)
        return;
      if (e.Key == VirtualKey.Enter || e.Key == VirtualKey.F2)
      {
        ViewModel.BeginEditSegment(seg);
        _rangeEditAnchorSegment = seg;
        ShowSegmentTextEditFlyout(fe, seg);
        e.Handled = true;
      }
    }

    private void TranscribeSegment_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
      if (sender is FrameworkElement fe && fe.DataContext is TranscriptionSegment seg)
      {
        ViewModel.BeginEditSegment(seg);
        _rangeEditAnchorSegment = seg;
        ShowSegmentTextEditFlyout(fe, seg);
        e.Handled = true;
      }
    }

    private void TranscribeSegment_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is not FrameworkElement fe || fe.DataContext is not TranscriptionSegment seg)
        return;
      e.Handled = true;
      var menu = new MenuFlyout();
      var editItem = new MenuFlyoutItem { Text = "Edit segment text…" };
      editItem.Click += (_, _) =>
      {
        ViewModel.BeginEditSegment(seg);
        _rangeEditAnchorSegment = seg;
        ShowSegmentTextEditFlyout(fe, seg);
      };
      menu.Items.Add(editItem);
      var regenItem = new MenuFlyoutItem { Text = "Regenerate segment audio" };
      regenItem.Click += async (_, _) =>
      {
        var msg = await ViewModel.RegenerateSegmentAudioAsync(seg, cancellationToken: CancellationToken.None).ConfigureAwait(true);
        if (!string.IsNullOrEmpty(msg))
        {
          ViewModel.TranscriptOperatorMessage = msg;
          _toastService?.ShowToast(ToastType.Warning, "Regenerate", msg);
        }
        else
        {
          ViewModel.TranscriptOperatorMessage = "Segment audio regenerated and applied to the timeline clip.";
          _toastService?.ShowToast(ToastType.Success, "Regenerate", "New audio applied to the linked timeline clip.");
        }
      };
      menu.Items.Add(regenItem);
      var opts = new FlyoutShowOptions { Position = e.GetPosition(fe) };
      menu.ShowAt(fe, opts);
    }

    private void EditHistoryList_ItemClick(object sender, ItemClickEventArgs e)
    {
      if (e.ClickedItem is TranscriptEditHistoryEntry entry)
        ViewModel.NavigateFromEditHistoryEntry(entry);
    }

    /// <summary>GOV-VOICESTUDIO-EDIT-APPLY-CONTEXT-JUMP-01: row tap jumps to source segment / timeline (Retry remains on its button).</summary>
    private void ApplyJobStatusList_ItemClick(object sender, ItemClickEventArgs e)
    {
      if (e.ClickedItem is TranscriptApplyJobStatusEntry entry)
        ViewModel.NavigateFromApplyJobStatusEntry(entry);
    }

    private static DataTemplate CreateFillerRemovalToggleItemTemplate()
    {
      const string xaml =
          "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">"
          + "<CheckBox Margin=\"0,2,0,0\" Content=\"{Binding DisplayLabel}\" IsChecked=\"{Binding IsRemoveEnabled, Mode=TwoWay}\" />"
          + "</DataTemplate>";
      return (DataTemplate)XamlReader.Load(xaml);
    }

    /// <summary>GAP-045: buffered segment text edit; Apply runs regen with <c>replacement_text</c>.</summary>
    private void ShowSegmentTextEditFlyout(FrameworkElement anchor, TranscriptionSegment seg)
    {
      var hint = new TextBlock
      {
        TextWrapping = TextWrapping.Wrap,
        FontSize = 12,
        Opacity = 0.9,
      };
      hint.SetBinding(TextBlock.TextProperty, new Binding
      {
        Path = new PropertyPath(nameof(TranscribeViewModel.SegmentEditOperatorHint)),
        Source = ViewModel,
      });

      var box = new TextBox
      {
        MinWidth = 320,
        MinHeight = 80,
        TextWrapping = TextWrapping.Wrap,
        AcceptsReturn = true,
      };
      box.SetBinding(TextBox.TextProperty, new Binding
      {
        Path = new PropertyPath(nameof(TranscribeViewModel.EditingSegmentDraftText)),
        Mode = BindingMode.TwoWay,
        Source = ViewModel,
      });

      var fillerHeader = new TextBlock
      {
        Text = ResourceHelper.GetString("Transcribe.FillerRemovalReviewHeader", "Remove fillers (review toggles)"),
        FontSize = 12,
        Opacity = 0.9,
        TextWrapping = TextWrapping.Wrap,
      };
      var fillerPreviewCaption = new TextBlock
      {
        Text = ResourceHelper.GetString("Transcribe.FillerRemovalPreviewCaption", "Preview"),
        FontSize = 11,
        Opacity = 0.85,
      };
      var fillerPreview = new TextBlock
      {
        TextWrapping = TextWrapping.Wrap,
        FontSize = 12,
        Opacity = 0.95,
      };
      fillerPreview.SetBinding(TextBlock.TextProperty, new Binding
      {
        Path = new PropertyPath(nameof(TranscribeViewModel.FillerRemovalPreviewText)),
        Mode = BindingMode.OneWay,
        Source = ViewModel,
      });
      var fillerList = new ItemsControl
      {
        ItemTemplate = CreateFillerRemovalToggleItemTemplate(),
      };
      fillerList.SetBinding(ItemsControl.ItemsSourceProperty, new Binding
      {
        Path = new PropertyPath(nameof(TranscribeViewModel.FillerRemovalToggles)),
        Mode = BindingMode.OneWay,
        Source = ViewModel,
      });
      var fillerSection = new StackPanel { Spacing = 4 };
      fillerSection.Children.Add(fillerHeader);
      fillerSection.Children.Add(fillerPreviewCaption);
      fillerSection.Children.Add(fillerPreview);
      fillerSection.Children.Add(fillerList);

      var removeFillersBtn = new Button { Content = ResourceHelper.GetString("Transcribe.RemoveFillersButton", "Remove fillers") };
      var applyBtn = new Button { Content = "Apply" };
      var cancelBtn = new Button { Content = "Cancel" };
      var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
      buttons.Children.Add(removeFillersBtn);
      buttons.Children.Add(applyBtn);
      buttons.Children.Add(cancelBtn);
      var panel = new StackPanel { Spacing = 8 };
      panel.Children.Add(hint);
      panel.Children.Add(box);
      panel.Children.Add(fillerSection);
      panel.Children.Add(buttons);

      var flyout = new Flyout { Content = panel };

      async System.Threading.Tasks.Task TryApplyAsync()
      {
        var wasMultiSegmentRange = ViewModel.IsMultiSegmentRangeEdit;
        var msg = await ViewModel.ApplyEditedSegmentAsync(CancellationToken.None).ConfigureAwait(true);
        if (!string.IsNullOrEmpty(msg))
        {
          ViewModel.TranscriptOperatorMessage = msg;
          _toastService?.ShowToast(ToastType.Warning, "Apply edit", msg);
          return;
        }

        flyout.Hide();
        var okMsg = wasMultiSegmentRange
            ? "Regenerated clip audio for the edited range (first segment anchors regen)."
            : "Regenerated segment audio with edited text.";
        _toastService?.ShowToast(ToastType.Success, "Apply edit", okMsg);
      }

      box.KeyDown += (_, ke) =>
      {
        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        if (ctrl && ke.Key == VirtualKey.Enter)
        {
          ke.Handled = true;
          _ = TryApplyAsync();
          return;
        }

        if (ke.Key == VirtualKey.Escape)
        {
          ke.Handled = true;
          flyout.Hide();
        }
      };

      flyout.Closed += (_, _) =>
      {
        if (ViewModel.IsEditingSegment)
          ViewModel.CancelSegmentEdit();
      };

      cancelBtn.Click += (_, _) =>
      {
        flyout.Hide();
      };

      removeFillersBtn.Click += (_, _) =>
      {
        var msg = ViewModel.TryRemoveFillersFromEditingDraft();
        if (!string.IsNullOrEmpty(msg))
          _toastService?.ShowToast(ToastType.Warning, ResourceHelper.GetString("Transcribe.RemoveFillersTitle", "Remove fillers"), msg);
      };

      applyBtn.Click += async (_, _) => await TryApplyAsync().ConfigureAwait(true);

      flyout.ShowAt(anchor);
    }

    private void RefreshTranscriptSegmentsItemsSource()
    {
      if (SegmentsRepeater == null || ViewModel.SelectedTranscription?.Segments == null)
        return;
      var src = ViewModel.SelectedTranscription.Segments;
      SegmentsRepeater.ItemsSource = null;
      SegmentsRepeater.ItemsSource = src;
    }

    private void RefreshSegmentRowVisuals()
    {
      if (SegmentsRepeater == null || ViewModel.SelectedTranscription?.Segments == null)
        return;
      WalkSegmentRowVisuals(SegmentsRepeater);
    }

    private void WalkSegmentRowVisuals(DependencyObject parent)
    {
      var count = VisualTreeHelper.GetChildrenCount(parent);
      for (var i = 0; i < count; i++)
      {
        var child = VisualTreeHelper.GetChild(parent, i);
        if (child is Grid grid && grid.DataContext is TranscriptionSegment seg)
        {
          var id = seg.Id ?? string.Empty;
          var linked = !string.IsNullOrEmpty(id) && ViewModel.LinkedTranscriptSegmentIds.Contains(id);
          var regen = ViewModel.WasSegmentRegeneratedInSession(id);
          var busy = !string.IsNullOrEmpty(id)
              && string.Equals(ViewModel.RegeneratingSegmentId, id, StringComparison.Ordinal);

          grid.Background = linked
              ? new SolidColorBrush(Color.FromArgb(64, 0, 183, 194))
              : null;

          if (regen)
          {
            grid.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 218, 165, 32));
            grid.BorderThickness = new Thickness(3, 0, 0, 0);
          }
          else
          {
            grid.BorderBrush = null;
            grid.BorderThickness = new Thickness(0);
          }

          var ring = FindFirstProgressRing(grid);
          if (ring != null)
            ring.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }

        WalkSegmentRowVisuals(child);
      }
    }

    private static ProgressRing? FindFirstProgressRing(DependencyObject parent)
    {
      var n = VisualTreeHelper.GetChildrenCount(parent);
      for (var i = 0; i < n; i++)
      {
        var c = VisualTreeHelper.GetChild(parent, i);
        if (c is ProgressRing pr)
          return pr;
        var nested = FindFirstProgressRing(c);
        if (nested != null)
          return nested;
      }

      return null;
    }

    private void TranscribeView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
      var isCtrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

      if (isCtrlPressed && e.Key == VirtualKey.A)
      {
        // Ctrl+A - Select all transcriptions
        ViewModel.SelectAllTranscriptionsCommand.Execute(null);
        UpdateTranscriptionSelectionVisuals();
        e.Handled = true;
      }
      else if (e.Key == VirtualKey.Escape)
      {
        // Escape - Clear transcription selection
        ViewModel.ClearTranscriptionSelectionCommand.Execute(null);
        UpdateTranscriptionSelectionVisuals();
        e.Handled = true;
      }
    }

    private void Transcription_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
      if (sender is ListViewItem listViewItem && listViewItem.DataContext is TranscriptionResponse transcription)
      {
        var isCtrlPressed = InputHelper.IsControlPressed();
        var isShiftPressed = InputHelper.IsShiftPressed();

        ViewModel.ToggleTranscriptionSelection(transcription.Id, isCtrlPressed, isShiftPressed);

        UpdateTranscriptionSelectionVisuals();
        e.Handled = true;
      }
    }

    private void UpdateTranscriptionSelectionVisuals()
    {
      // Update visual indicators for all transcription list items
      UpdateTranscriptionSelectionVisualsRecursive(this);
    }

    private void UpdateTranscriptionSelectionVisualsRecursive(DependencyObject element)
    {
      if (element == null || ViewModel == null)
        return;

      // Check if this is a ListViewItem with a TranscriptionResponse
      if (element is ListViewItem listViewItem && listViewItem.DataContext is TranscriptionResponse transcription)
      {
        var isSelected = ViewModel.IsTranscriptionSelected(transcription.Id);

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
        UpdateTranscriptionSelectionVisualsRecursive(child);
      }
    }

    private void UpdateEmptyStateVisibility()
    {
      if (EmptyTranscriptionState != null)
      {
        EmptyTranscriptionState.Visibility = ViewModel.Transcriptions.Count == 0
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
      }
    }

    private void CopyTranscriptionText_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      if (ViewModel.SelectedTranscription != null && !string.IsNullOrEmpty(ViewModel.TranscriptionText))
      {
        var dataPackage = new DataPackage();
        dataPackage.SetText(ViewModel.TranscriptionText);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        _toastService?.ShowToast(ToastType.Success, "Copied", "Transcription text copied to clipboard");
      }
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Transcribe Help";
      HelpOverlay.HelpText = "The Transcribe panel converts audio files to text using speech-to-text engines. Enter an audio ID and optional project ID, select an engine (Whisper, WhisperX, etc.) and language, then transcribe. Enable word timestamps for precise timing information, or diarization for speaker identification. View and edit transcriptions in the text editor below.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh transcriptions list" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+T", Description = "Start transcription" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F2 / Enter", Description = "Edit focused transcript segment (when segment row is focused)" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+Enter", Description = "Apply segment text edit (in edit flyout)" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Escape", Description = "Cancel segment edit (in flyout) or clear transcription selection" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Use 'auto' language detection for automatic language identification");
      HelpOverlay.Tips.Add("Word timestamps provide precise timing for each word in the transcription");
      HelpOverlay.Tips.Add("Diarization identifies different speakers (requires WhisperX engine)");
      HelpOverlay.Tips.Add("Double-click a segment or press F2/Enter on a focused segment to edit text; Ctrl+Enter applies in the flyout");
      HelpOverlay.Tips.Add("Transcriptions can be edited directly in the text editor below");
      HelpOverlay.Tips.Add("Different engines offer different features - WhisperX supports diarization");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void Transcription_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var transcription = element.DataContext ?? listView.SelectedItem;
        if (transcription != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var editItem = new MenuFlyoutItem { Text = "Edit" };
            editItem.Click += async (_, _) => await HandleTranscriptionMenuClick("Edit", transcription);
            menu.Items.Add(editItem);

            var exportItem = new MenuFlyoutItem { Text = "Export" };
            exportItem.Click += async (_, _) => await HandleTranscriptionMenuClick("Export", transcription);
            menu.Items.Add(exportItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, _) => await HandleTranscriptionMenuClick("Delete", transcription);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private async System.Threading.Tasks.Task HandleTranscriptionMenuClick(string action, object transcription)
    {
      try
      {
        switch (action.ToLower())
        {
          case "edit":
            // Select the transcription for editing
            ViewModel.SelectedTranscription = (TranscriptionResponse)transcription;
            _toastService?.ShowToast(ToastType.Info, "Edit Transcription", "Transcription selected for editing");
            break;
          case "export":
            await ExportTranscriptionAsync(transcription as TranscriptionResponse).ConfigureAwait(true);
            break;
          case "delete":
            if (ViewModel.DeleteTranscriptionCommand.CanExecute(transcription))
            {
              var dialog = new ContentDialog
              {
                Title = "Delete Transcription",
                Content = "Are you sure you want to delete this transcription? This action cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
              };

              var result = await dialog.ShowAsync();
              if (result == ContentDialogResult.Primary)
              {
                var transcriptionToDelete = (TranscriptionResponse)transcription;
                var transcriptionIndex = ViewModel.Transcriptions.IndexOf(transcriptionToDelete);

                await ViewModel.DeleteTranscriptionCommand.ExecuteAsync(transcriptionToDelete);

                // Register undo action
                if (_undoRedoService != null && transcriptionIndex >= 0)
                {
                  var actionObj = new SimpleAction(
                      "Delete Transcription",
                      () => ViewModel.Transcriptions.Insert(transcriptionIndex, transcriptionToDelete),
                      () => ViewModel.Transcriptions.Remove(transcriptionToDelete));
                  _undoRedoService.RegisterAction(actionObj);
                }

                _toastService?.ShowToast(ToastType.Success, "Deleted", "Transcription deleted");
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

    private async System.Threading.Tasks.Task ExportTranscriptionAsync(TranscriptionResponse? transcription)
    {
      if (transcription == null)
      {
        _toastService?.ShowToast(ToastType.Warning, "Export", "No transcription selected.");
        return;
      }

      if ((transcription.Segments == null || transcription.Segments.Count == 0)
          && string.IsNullOrWhiteSpace(transcription.Text))
      {
        _toastService?.ShowToast(ToastType.Warning, "Export", "The selected transcription has no text to export.");
        return;
      }

      var picker = new FileSavePicker
      {
        SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        SuggestedFileName = BuildTranscriptExportBaseFileName(transcription),
      };
      picker.FileTypeChoices.Add("SubRip subtitle (.srt)", new List<string> { ".srt" });
      picker.FileTypeChoices.Add("Plain text (.txt)", new List<string> { ".txt" });

      if (App.MainWindowInstance != null)
      {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
      }

      var file = await picker.PickSaveFileAsync();
      if (file == null)
        return;

      var extension = Path.GetExtension(file.Name);
      var content = string.Equals(extension, ".srt", StringComparison.OrdinalIgnoreCase)
          ? TranscriptionExportFormatter.BuildSrt(transcription)
          : TranscriptionExportFormatter.BuildPlainText(transcription);

      await FileIO.WriteTextAsync(file, content);
      _toastService?.ShowToast(ToastType.Success, "Export", $"Transcript exported to {file.Name}");
    }

    private static string BuildTranscriptExportBaseFileName(TranscriptionResponse transcription)
    {
      var source = string.IsNullOrWhiteSpace(transcription.Id) ? "transcription" : transcription.Id;
      var invalidChars = Path.GetInvalidFileNameChars();
      var safeName = new char[source.Length];
      for (var i = 0; i < source.Length; i++)
      {
        var ch = source[i];
        var isInvalid = false;
        for (var j = 0; j < invalidChars.Length; j++)
        {
          if (ch != invalidChars[j])
            continue;
          isInvalid = true;
          break;
        }

        safeName[i] = isInvalid ? '_' : ch;
      }

      return $"{new string(safeName)}_{DateTime.Now:yyyyMMdd_HHmmss}";
    }

    // Drag-and-drop handlers for transcription item reordering
    private void Transcription_DragStarting(UIElement sender, DragStartingEventArgs e)
    {
      if (sender is ListViewItem listViewItem && listViewItem.DataContext is TranscriptionResponse transcription)
      {
        _draggedTranscription = transcription;

        // Set drag data
        e.Data.SetText(transcription.Id);
        e.Data.Properties.Add("TranscriptionId", transcription.Id);
        e.Data.Properties.Add("TranscriptionText", transcription.Text ?? "Unnamed Transcription");

        // Reduce opacity of source element
        listViewItem.Opacity = 0.5;
      }
    }

    private void Transcription_DragItemsCompleted(UIElement sender, DragItemsCompletedEventArgs e)
    {
      // Clean up drag state
      if (sender is ListViewItem listViewItem)
      {
        listViewItem.Opacity = 1.0;
      }

      _dragDropService?.Cleanup();

      _draggedTranscription = null;
    }

    private void Transcription_DragOver(object sender, DragEventArgs e)
    {
      if (sender is ListViewItem listViewItem && _dragDropService != null)
      {
        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsGlyphVisible = false;
        e.DragUIOverride.IsContentVisible = false;

        // Show drop target indicator
        var position = e.GetPosition(listViewItem);
        var dropPosition = DetermineTranscriptionDropPosition(listViewItem, position);
        _dragDropService.ShowDropTargetIndicator(listViewItem, dropPosition);
      }
    }

    private void Transcription_Drop(object sender, DragEventArgs e)
    {
      if (sender is ListViewItem listViewItem && _draggedTranscription != null && _dragDropService != null)
      {
        e.AcceptedOperation = DataPackageOperation.Move;

        // Hide drop indicator
        _dragDropService.HideDropTargetIndicator();
        _dragDropService.Cleanup();

        // Get target transcription
        if (listViewItem.DataContext is TranscriptionResponse targetTranscription)
        {
          var draggedTranscription = _draggedTranscription;
          var draggedIndex = ViewModel.Transcriptions.IndexOf(draggedTranscription);
          var targetIndex = ViewModel.Transcriptions.IndexOf(targetTranscription);

          if (draggedIndex >= 0 && targetIndex >= 0 && draggedIndex != targetIndex)
          {
            // Determine drop position
            var position = e.GetPosition(listViewItem);
            var dropPosition = DetermineTranscriptionDropPosition(listViewItem, position);

            // Reorder transcriptions in the collection
            ViewModel.Transcriptions.RemoveAt(draggedIndex);

            if (dropPosition == DropPosition.Before)
            {
              ViewModel.Transcriptions.Insert(targetIndex, draggedTranscription);
            }
            else if (dropPosition == DropPosition.After)
            {
              var newIndex = targetIndex < draggedIndex ? targetIndex + 1 : targetIndex;
              ViewModel.Transcriptions.Insert(newIndex, draggedTranscription);
            }
            else
            {
              // On - replace target
              ViewModel.Transcriptions.Insert(targetIndex, draggedTranscription);
            }

            _toastService?.ShowToast(ToastType.Success, "Reordered", "Transcription list reordered");
          }
        }

        // Clean up drag state
        _draggedTranscription = null;

        // Restore source element opacity
        if (e.OriginalSource is ListViewItem sourceItem)
        {
          sourceItem.Opacity = 1.0;
        }
      }
    }

    private void Transcription_DragLeave(object sender, DragEventArgs e)
    {
      _dragDropService?.HideDropTargetIndicator();
    }

    /// <summary>GOV-VOICESTUDIO-EDIT-APPLY-RETRY-RECOVERY-01: session retry uses frozen job snapshot in the VM.</summary>
    private void ApplyJobRetryButton_Click(object sender, RoutedEventArgs e)
    {
      if (sender is Button { DataContext: TranscriptApplyJobStatusEntry entry })
        _ = ViewModel.RetryTranscriptApplyJobAsync(entry);
    }

    private async void TranscribeView_Loaded(object _, RoutedEventArgs __)
    {
      this.Loaded -= TranscribeView_Loaded;
      KeyboardNavigationHelper.SetupTabNavigation(this, 0);
      await ViewModel.InitializeAsync(CancellationToken.None);
    }

    private DropPosition DetermineTranscriptionDropPosition(ListViewItem target, Windows.Foundation.Point position)
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