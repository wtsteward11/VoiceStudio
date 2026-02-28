using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using System;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// MultilingualSupportView panel for multi-language voice synthesis.
  /// </summary>
  public sealed partial class MultilingualSupportView : UserControl
  {
    public MultilingualSupportViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;

    public MultilingualSupportView()
    {
      this.InitializeComponent();
      ViewModel = new MultilingualSupportViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          VoiceStudio.App.Services.ServiceProvider.GetBackendClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(MultilingualSupportViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Multilingual Support Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(MultilingualSupportViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Multilingual Support", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += MultilingualSupportView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });

      // Handle ListView selection changes for target languages
      this.Loaded += (_, __) =>
      {
        var listView = this.FindName("TargetLanguagesListView") as Microsoft.UI.Xaml.Controls.ListView;
        if (listView != null)
        {
          listView.SelectionChanged += (_, _) =>
                {
                  ViewModel.SelectedTargetLanguages.Clear();
                  foreach (var item in listView.SelectedItems)
                  {
                    if (item is ViewModels.LanguageItem langItem)
                    {
                      ViewModel.SelectedTargetLanguages.Add(langItem.Code);
                    }
                  }
                };
        }

        // Add right-click context menu for synthesized audios
        var audioListView = this.FindName("SynthesizedAudiosListView") as Microsoft.UI.Xaml.Controls.ListView;
        if (audioListView != null)
        {
          audioListView.RightTapped += Audio_RightTapped;
        }
      };
    }

    private void Audio_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var audio = element.DataContext ?? listView.SelectedItem;
        if (audio != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var playItem = new MenuFlyoutItem { Text = "Play" };
            playItem.Click += async (_, __) => await HandleAudioMenuClick("Play", audio);
            menu.Items.Add(playItem);

            var exportItem = new MenuFlyoutItem { Text = "Export" };
            exportItem.Click += async (_, __) => await HandleAudioMenuClick("Export", audio);
            menu.Items.Add(exportItem);

            var duplicateItem = new MenuFlyoutItem { Text = "Duplicate" };
            duplicateItem.Click += async (_, __) => await HandleAudioMenuClick("Duplicate", audio);
            menu.Items.Add(duplicateItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, __) => await HandleAudioMenuClick("Delete", audio);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private async System.Threading.Tasks.Task HandleAudioMenuClick(string action, object audio)
    {
      try
      {
        switch (action.ToLower())
        {
          case "play":
            _toastService?.ShowToast(ToastType.Info, "Play", "Playing audio");
            break;
          case "export":
            await ExportAudioAsync(audio);
            break;
          case "duplicate":
            DuplicateAudio(audio);
            break;
          case "delete":
            var dialog = new ContentDialog
            {
              Title = "Delete Audio",
              Content = "Are you sure you want to delete this synthesized audio? This action cannot be undone.",
              PrimaryButtonText = "Delete",
              CloseButtonText = "Cancel",
              DefaultButton = ContentDialogButton.Close,
              XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
              var audioToDelete = (MultilingualAudioItem)audio;
              var audioIndex = ViewModel.SynthesizedAudios.IndexOf(audioToDelete);

              ViewModel.SynthesizedAudios.Remove(audioToDelete);

              // Register undo action
              if (_undoRedoService != null && audioIndex >= 0)
              {
                var actionObj = new SimpleAction(
                    "Delete Synthesized Audio",
                    () => ViewModel.SynthesizedAudios.Insert(audioIndex, audioToDelete),
                    () => ViewModel.SynthesizedAudios.Remove(audioToDelete));
                _undoRedoService.RegisterAction(actionObj);
              }

              _toastService?.ShowToast(ToastType.Success, "Deleted", "Audio deleted");
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Multilingual Support Help";
      HelpOverlay.HelpText = "The Multilingual Support panel enables voice synthesis in multiple languages. Translate text between languages, select source and target languages, and synthesize multilingual content. Auto-detect language or manually select the source language for accurate translation and synthesis.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+T", Description = "Translate text" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+S", Description = "Synthesize" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Escape", Description = "Close help" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Enable auto-detect to automatically identify the source language");
      HelpOverlay.Tips.Add("Select target language for translation and synthesis");
      HelpOverlay.Tips.Add("Translation preserves meaning while adapting to target language");
      HelpOverlay.Tips.Add("Synthesized audio uses voice profiles configured for each language");
      HelpOverlay.Tips.Add("Use multilingual support for international content and localization");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private async System.Threading.Tasks.Task ExportAudioAsync(object audio)
    {
      try
      {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("JSON", new[] { ".json" });
        picker.SuggestedFileName = "multilingual_audio_export";

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
          var audioType = audio.GetType();
          var jsonData = new
          {
            Id = audioType.GetProperty("Id")?.GetValue(audio)?.ToString() ?? "unknown",
            Language = audioType.GetProperty("Language")?.GetValue(audio)?.ToString() ?? "unknown",
            Created = audioType.GetProperty("Created")?.GetValue(audio)?.ToString() ?? DateTime.UtcNow.ToString()
          };
          var content = System.Text.Json.JsonSerializer.Serialize(jsonData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
          await Windows.Storage.FileIO.WriteTextAsync(file, content);
          _toastService?.ShowToast(ToastType.Success, "Export", "Audio exported successfully");
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Export Failed", ex.Message);
      }
    }

    private void DuplicateAudio(object audio)
    {
      try
      {
        var audioType = audio.GetType();
        var duplicatedAudio = Activator.CreateInstance(audioType);
        if (duplicatedAudio != null)
        {
          foreach (var prop in audioType.GetProperties())
          {
            if (prop.CanRead && prop.CanWrite && prop.GetIndexParameters().Length == 0)
            {
              var value = prop.GetValue(audio);
              prop.SetValue(duplicatedAudio, value);
            }
          }

          var index = ViewModel.SynthesizedAudios.IndexOf((MultilingualAudioItem)audio);
          ViewModel.SynthesizedAudios.Insert(index + 1, (MultilingualAudioItem)duplicatedAudio);
          _toastService?.ShowToast(ToastType.Success, "Duplicated", "Audio duplicated");
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Duplicate Failed", ex.Message);
      }
    }

    private void MultilingualSupportView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }
  }
}