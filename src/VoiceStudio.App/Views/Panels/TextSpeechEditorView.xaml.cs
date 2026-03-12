using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using System;
using System.Linq;
using System.Collections.Generic;
using Windows.System;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// TextSpeechEditorView panel for text-based speech editing.
  /// </summary>
  public sealed partial class TextSpeechEditorView : UserControl
  {
    public TextSpeechEditorViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;

    public TextSpeechEditorView()
    {
      this.InitializeComponent();
      ViewModel = new TextSpeechEditorViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          VoiceStudio.App.Services.ServiceProvider.GetBackendClient(),
          AppServices.GetProjectsClient(),
          ServiceProvider.GetProfilesClient(),
          VoiceStudio.App.Services.ServiceProvider.GetAudioPlayerService()
      );
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();

      // Add keyboard navigation
      this.KeyDown += TextSpeechEditorView_KeyDown;

      // Setup keyboard navigation
      this.Loaded += TextSpeechEditorView_KeyboardNavigation_Loaded;

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
        if (e.PropertyName == nameof(TextSpeechEditorViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Text Speech Editor Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(TextSpeechEditorViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Text Speech Editor", ViewModel.StatusMessage);
        }
      };
    }

    private void TextSpeechEditorView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
      // Ctrl+S saves session
      if (e.Key == VirtualKey.S)
      {
        if (InputHelper.IsControlPressed() && ViewModel.UpdateSessionCommand.CanExecute(null))
        {
          ViewModel.UpdateSessionCommand.Execute(null);
          e.Handled = true;
        }
      }
      // Ctrl+N creates new session
      else if (e.Key == VirtualKey.N)
      {
        if (InputHelper.IsControlPressed() && ViewModel.CreateSessionCommand.CanExecute(null))
        {
          ViewModel.CreateSessionCommand.Execute(null);
          e.Handled = true;
        }
      }
      // Delete key removes selected segment
      else if (e.Key == VirtualKey.Delete)
      {
        if (ViewModel.SelectedSegment != null && ViewModel.RemoveSegmentCommand.CanExecute(null))
        {
          ViewModel.RemoveSegmentCommand.Execute(null);
          e.Handled = true;
        }
      }
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Text Speech Editor Help";
      HelpOverlay.HelpText = "The Text Speech Editor provides a text-based interface for editing synthesized speech. Edit text directly and regenerate audio, apply word-level timing adjustments, add pauses and emphasis markers, and fine-tune speech characteristics without re-recording. The editor synchronizes text with audio playback, allowing precise control over speech timing and pronunciation.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+S", Description = "Save edits" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Regenerate audio" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Edit text directly to change speech content");
      HelpOverlay.Tips.Add("Word-level timing controls let you adjust speech rhythm");
      HelpOverlay.Tips.Add("Add pauses and emphasis markers for natural speech");
      HelpOverlay.Tips.Add("Regenerate audio after text changes to hear updates");
      HelpOverlay.Tips.Add("Text and audio stay synchronized during editing");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void Session_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var session = element.DataContext ?? listView.SelectedItem;
        if (session != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var editItem = new MenuFlyoutItem { Text = "Edit" };
            editItem.Click += async (_, _) => await HandleSessionMenuClick("Edit", session);
            menu.Items.Add(editItem);

            var duplicateItem = new MenuFlyoutItem { Text = "Duplicate" };
            duplicateItem.Click += async (_, _) => await HandleSessionMenuClick("Duplicate", session);
            menu.Items.Add(duplicateItem);

            var exportItem = new MenuFlyoutItem { Text = "Export" };
            exportItem.Click += async (_, _) => await HandleSessionMenuClick("Export", session);
            menu.Items.Add(exportItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, _) => await HandleSessionMenuClick("Delete", session);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private void Segment_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var segment = element.DataContext ?? listView.SelectedItem;
        if (segment != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var editItem = new MenuFlyoutItem { Text = "Edit" };
            editItem.Click += async (_, _) => await HandleSegmentMenuClick("Edit", segment);
            menu.Items.Add(editItem);

            var duplicateItem = new MenuFlyoutItem { Text = "Duplicate" };
            duplicateItem.Click += async (_, _) => await HandleSegmentMenuClick("Duplicate", segment);
            menu.Items.Add(duplicateItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, _) => await HandleSegmentMenuClick("Delete", segment);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private async System.Threading.Tasks.Task HandleSessionMenuClick(string action, object sessionObj)
    {
      try
      {
        var session = (EditorSessionItem)sessionObj;
        switch (action.ToLower())
        {
          case "edit":
            ViewModel.SelectedSession = session;
            _toastService?.ShowToast(ToastType.Info, "Edit Session", "Session selected for editing");
            break;
          case "duplicate":
            DuplicateSession(session);
            break;
          case "export":
            await ExportSessionAsync(session);
            break;
          case "delete":
            var dialog = new ContentDialog
            {
              Title = "Delete Session",
              Content = "Are you sure you want to delete this session? This action cannot be undone.",
              PrimaryButtonText = "Delete",
              CloseButtonText = "Cancel",
              DefaultButton = ContentDialogButton.Close,
              XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
              var sessionToDelete = session;
              var sessionIndex = ViewModel.Sessions.IndexOf(session);

              ViewModel.Sessions.Remove(session);

              // Register undo action
              if (_undoRedoService != null && sessionIndex >= 0)
              {
                var actionObj = new SimpleAction(
                    "Delete Session",
                    () => ViewModel.Sessions.Insert(sessionIndex, sessionToDelete),
                    () => ViewModel.Sessions.Remove(sessionToDelete));
                _undoRedoService.RegisterAction(actionObj);
              }

              _toastService?.ShowToast(ToastType.Success, "Deleted", "Session deleted");
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private async System.Threading.Tasks.Task HandleSegmentMenuClick(string action, object segmentObj)
    {
      try
      {
        var segment = (TextSegmentItem)segmentObj;
        switch (action.ToLower())
        {
          case "edit":
            ViewModel.SelectedSegment = segment;
            _toastService?.ShowToast(ToastType.Info, "Edit Segment", "Segment selected for editing");
            break;
          case "duplicate":
            DuplicateSegment(segment);
            break;
          case "delete":
            var dialog = new ContentDialog
            {
              Title = "Delete Segment",
              Content = "Are you sure you want to delete this segment? This action cannot be undone.",
              PrimaryButtonText = "Delete",
              CloseButtonText = "Cancel",
              DefaultButton = ContentDialogButton.Close,
              XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
              var segmentToDelete = segment;
              var segmentIndex = ViewModel.Segments.IndexOf(segment);

              ViewModel.Segments.Remove(segment);

              // Register undo action
              if (_undoRedoService != null && segmentIndex >= 0)
              {
                var actionObj = new SimpleAction(
                    "Delete Segment",
                    () => ViewModel.Segments.Insert(segmentIndex, segmentToDelete),
                    () => ViewModel.Segments.Remove(segmentToDelete));
                _undoRedoService.RegisterAction(actionObj);
              }

              _toastService?.ShowToast(ToastType.Success, "Deleted", "Segment deleted");
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private void DuplicateSession(object session)
    {
      if (session is EditorSessionItem originalSession)
      {
        var duplicate = new EditorSessionItem(
            new TextSpeechEditorViewModel.EditorSession
            {
              SessionId = Guid.NewGuid().ToString(),
              ProjectId = originalSession.ProjectId,
              Title = $"{originalSession.Title} (Copy)",
              Segments = originalSession.Segments.Select(s => new TextSpeechEditorViewModel.TextSegment
              {
                Id = Guid.NewGuid().ToString(),
                Text = s.Text,
                StartTime = s.StartTime,
                EndTime = s.EndTime,
                Speaker = s.Speaker,
                Prosody = s.Prosody != null ? new System.Collections.Generic.Dictionary<string, object>(s.Prosody) : null,
                Phonemes = s.Phonemes?.ToArray(),
                Notes = s.Notes
              }).ToArray(),
              AudioId = originalSession.AudioId,
              Language = originalSession.Language,
              Created = DateTime.UtcNow.ToString("O"),
              Modified = DateTime.UtcNow.ToString("O")
            }
        );

        var sessionIndex = ViewModel.Sessions.IndexOf(originalSession);
        ViewModel.Sessions.Insert(sessionIndex + 1, duplicate);
        ViewModel.SelectedSession = duplicate;
        _toastService?.ShowToast(ToastType.Success, "Duplicated", "Session duplicated");
      }
    }

    private async System.Threading.Tasks.Task ExportSessionAsync(object session)
    {
      if (session is EditorSessionItem sessionItem)
      {
        try
        {
          var picker = new Windows.Storage.Pickers.FileSavePicker();
          picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
          picker.FileTypeChoices.Add("JSON", new[] { ".json" });
          picker.SuggestedFileName = $"{sessionItem.Title}_export";

          var window = Microsoft.UI.Xaml.Window.Current;
          if (window != null)
          {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
          }

          var file = await picker.PickSaveFileAsync();
          if (file != null)
          {
            var jsonData = new
            {
              SessionId = sessionItem.SessionId,
              Title = sessionItem.Title,
              ProjectId = sessionItem.ProjectId,
              Language = sessionItem.Language,
              AudioId = sessionItem.AudioId,
              Created = sessionItem.Created,
              Modified = sessionItem.Modified,
              Segments = sessionItem.Segments.Select(s => new
              {
                Text = s.Text,
                StartTime = s.StartTime,
                EndTime = s.EndTime,
                Speaker = s.Speaker,
                Prosody = s.Prosody,
                Phonemes = s.Phonemes,
                Notes = s.Notes
              }).ToList()
            };
            var content = System.Text.Json.JsonSerializer.Serialize(jsonData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await Windows.Storage.FileIO.WriteTextAsync(file, content);
            _toastService?.ShowToast(ToastType.Success, "Export", "Session exported successfully");
          }
        }
        catch (Exception ex)
        {
          _toastService?.ShowToast(ToastType.Error, "Export Failed", ex.Message);
        }
      }
    }

    private void DuplicateSegment(object segment)
    {
      if (segment is TextSegmentItem originalSegment && ViewModel.SelectedSession != null)
      {
        var duplicate = new TextSegmentItem(
            new TextSpeechEditorViewModel.TextSegment
            {
              Id = Guid.NewGuid().ToString(),
              Text = originalSegment.Text,
              StartTime = originalSegment.StartTime,
              EndTime = originalSegment.EndTime,
              Speaker = originalSegment.Speaker,
              Prosody = originalSegment.Prosody != null ? new System.Collections.Generic.Dictionary<string, object>(originalSegment.Prosody) : null,
              Phonemes = originalSegment.Phonemes?.ToArray(),
              Notes = originalSegment.Notes
            }
        );

        var segmentIndex = ViewModel.SelectedSession.Segments.IndexOf(originalSegment);
        ViewModel.SelectedSession.Segments.Insert(segmentIndex + 1, duplicate);
        _toastService?.ShowToast(ToastType.Success, "Duplicated", "Segment duplicated");
      }
    }

    private void TextSpeechEditorView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }
  }
}