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
  /// LexiconView panel for pronunciation lexicon management.
  /// </summary>
  public sealed partial class LexiconView : UserControl
  {
    public LexiconViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;

    public LexiconView()
    {
      this.InitializeComponent();
      ViewModel = new LexiconViewModel(
          AppServices.GetViewModelContext(),
          AppServices.GetLexiconClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(LexiconViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Lexicon Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(LexiconViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Lexicon", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += LexiconView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void LexiconView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Pronunciation Lexicon Help";
      HelpOverlay.HelpText = "The Pronunciation Lexicon panel allows you to create and manage custom pronunciation dictionaries. Add word-pronunciation mappings, edit phonetic transcriptions, and customize how words are pronounced during synthesis. The lexicon helps ensure correct pronunciation of specialized terms, proper nouns, acronyms, and domain-specific vocabulary. Lexicon entries override default pronunciation for more accurate speech synthesis.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+N", Description = "Add new entry" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Delete", Description = "Delete selected entry" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Use lexicon entries to customize pronunciation of specialized terms");
      HelpOverlay.Tips.Add("Phonetic transcriptions use IPA (International Phonetic Alphabet) format");
      HelpOverlay.Tips.Add("Lexicon entries take precedence over default pronunciations");
      HelpOverlay.Tips.Add("Test pronunciations before adding to lexicon");
      HelpOverlay.Tips.Add("Export/import lexicons to share pronunciation dictionaries");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void Lexicon_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var lexicon = element.DataContext ?? listView.SelectedItem;
        if (lexicon != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var editItem = new MenuFlyoutItem { Text = "Edit" };
            editItem.Click += async (_, __) => await HandleLexiconMenuClick("Edit", lexicon);
            menu.Items.Add(editItem);

            var exportItem = new MenuFlyoutItem { Text = "Export" };
            exportItem.Click += async (_, __) => await HandleLexiconMenuClick("Export", lexicon);
            menu.Items.Add(exportItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, __) => await HandleLexiconMenuClick("Delete", lexicon);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private void Entry_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var entry = element.DataContext ?? listView.SelectedItem;
        if (entry != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var editItem = new MenuFlyoutItem { Text = "Edit" };
            editItem.Click += async (_, __) => await HandleEntryMenuClick("Edit", entry);
            menu.Items.Add(editItem);

            var duplicateItem = new MenuFlyoutItem { Text = "Duplicate" };
            duplicateItem.Click += async (_, __) => await HandleEntryMenuClick("Duplicate", entry);
            menu.Items.Add(duplicateItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, __) => await HandleEntryMenuClick("Delete", entry);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private async System.Threading.Tasks.Task HandleLexiconMenuClick(string action, object lexicon)
    {
      try
      {
        switch (action.ToLower())
        {
          case "edit":
            ViewModel.SelectedLexicon = (VoiceStudio.App.ViewModels.LexiconItem)lexicon;
            _toastService?.ShowToast(ToastType.Info, "Edit Lexicon", "Lexicon selected for editing");
            break;
          case "export":
            await ExportLexiconAsync(lexicon);
            break;
          case "delete":
            var dialog = new ContentDialog
            {
              Title = "Delete Lexicon",
              Content = "Are you sure you want to delete this lexicon? This action cannot be undone.",
              PrimaryButtonText = "Delete",
              CloseButtonText = "Cancel",
              DefaultButton = ContentDialogButton.Close,
              XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
              var lexiconToDelete = (VoiceStudio.App.ViewModels.LexiconItem)lexicon;
              var lexiconIndex = ViewModel.Lexicons.IndexOf(lexiconToDelete);

              ViewModel.Lexicons.Remove(lexiconToDelete);

              // Register undo action
              if (_undoRedoService != null && lexiconIndex >= 0)
              {
                var actionObj = new SimpleAction(
                    "Delete Lexicon",
                    () => ViewModel.Lexicons.Insert(lexiconIndex, lexiconToDelete),
                    () => ViewModel.Lexicons.Remove(lexiconToDelete));
                _undoRedoService.RegisterAction(actionObj);
              }

              _toastService?.ShowToast(ToastType.Success, "Deleted", "Lexicon deleted");
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private async System.Threading.Tasks.Task HandleEntryMenuClick(string action, object entry)
    {
      try
      {
        switch (action.ToLower())
        {
          case "edit":
            ViewModel.SelectedEntry = (VoiceStudio.App.ViewModels.LexiconEntryItem)entry;
            _toastService?.ShowToast(ToastType.Info, "Edit Entry", "Entry selected for editing");
            break;
          case "duplicate":
            DuplicateEntry(entry);
            break;
          case "delete":
            var dialog = new ContentDialog
            {
              Title = "Delete Entry",
              Content = "Are you sure you want to delete this lexicon entry? This action cannot be undone.",
              PrimaryButtonText = "Delete",
              CloseButtonText = "Cancel",
              DefaultButton = ContentDialogButton.Close,
              XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
              var entryToDelete = (VoiceStudio.App.ViewModels.LexiconEntryItem)entry;
              var entryIndex = ViewModel.Entries.IndexOf(entryToDelete);

              ViewModel.Entries.Remove(entryToDelete);

              // Register undo action
              if (_undoRedoService != null && entryIndex >= 0)
              {
                var actionObj = new SimpleAction(
                    "Delete Lexicon Entry",
                    () => ViewModel.Entries.Insert(entryIndex, entryToDelete),
                    () => ViewModel.Entries.Remove(entryToDelete));
                _undoRedoService.RegisterAction(actionObj);
              }

              _toastService?.ShowToast(ToastType.Success, "Deleted", "Lexicon entry deleted");
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private async System.Threading.Tasks.Task ExportLexiconAsync(object lexicon)
    {
      try
      {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("JSON", new[] { ".json" });
        picker.SuggestedFileName = "lexicon_export";

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
          var lexiconType = lexicon.GetType();
          var jsonData = new
          {
            Name = lexiconType.GetProperty("Name")?.GetValue(lexicon)?.ToString() ?? "unknown",
            Id = lexiconType.GetProperty("Id")?.GetValue(lexicon)?.ToString() ?? "unknown"
          };
          var content = System.Text.Json.JsonSerializer.Serialize(jsonData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
          await Windows.Storage.FileIO.WriteTextAsync(file, content);
          _toastService?.ShowToast(ToastType.Success, "Export", "Lexicon exported successfully");
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Export Failed", ex.Message);
      }
    }

    private void DuplicateEntry(object entry)
    {
      try
      {
        var entryType = entry.GetType();
        var entryName = entryType.GetProperty("Word")?.GetValue(entry)?.ToString() ?? "entry";
        var duplicatedEntry = Activator.CreateInstance(entryType);
        if (duplicatedEntry != null)
        {
          var nameProp = entryType.GetProperty("Word");
          if (nameProp?.CanWrite == true)
          {
            nameProp.SetValue(duplicatedEntry, $"{entryName} (Copy)");
          }
          var index = ViewModel.Entries.IndexOf((VoiceStudio.App.ViewModels.LexiconEntryItem)entry);
          ViewModel.Entries.Insert(index + 1, (VoiceStudio.App.ViewModels.LexiconEntryItem)duplicatedEntry);
          _toastService?.ShowToast(ToastType.Success, "Duplicated", "Entry duplicated");
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Duplicate Failed", ex.Message);
      }
    }
  }
}