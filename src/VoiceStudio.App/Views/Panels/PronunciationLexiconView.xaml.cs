using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services.UndoableActions;
using System;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// PronunciationLexiconView panel - Custom pronunciation management.
  /// </summary>
  public sealed partial class PronunciationLexiconView : Microsoft.UI.Xaml.Controls.UserControl
  {
    public PronunciationLexiconViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;

    public PronunciationLexiconView()
    {
      this.InitializeComponent();
      ViewModel = new PronunciationLexiconViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetPronunciationLexiconClient(),
          ServiceProvider.GetProfilesClient(),
          AppServices.GetRequiredService<IVoiceSynthesisService>(),
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IAudioPlayerService>()
      );
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(PronunciationLexiconViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Pronunciation Lexicon Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(PronunciationLexiconViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Pronunciation Lexicon", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation and load entries (not in constructor — RETAINED_ASYNC_RULE)
      this.Loaded += PronunciationLexiconView_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void PronunciationLexiconView_Loaded(object _, RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
      ViewModel.LoadEntriesCommand.ExecuteAsync(null);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Pronunciation Lexicon Help";
      HelpOverlay.HelpText = "The Pronunciation Lexicon allows you to define custom pronunciations for words or phrases, ensuring the TTS engine speaks names, acronyms, or domain-specific terms correctly. Add entries by typing a word and its pronunciation in IPA (International Phonetic Alphabet) or phoneme notation. Use the 'Estimate' button to get AI-suggested pronunciations. Test pronunciations using the play button. The lexicon helps prevent mispronunciations without needing manual fixes in every script. Entries can be organized by language and searched for quick access.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+N", Description = "Add new entry" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+S", Description = "Search entries" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+E", Description = "Estimate phonemes" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Delete", Description = "Delete selected entry" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh entries" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Use IPA notation for accurate pronunciations (e.g., /ˈɡuː.i/ for 'GUI')");
      HelpOverlay.Tips.Add("Click 'Estimate' to get AI-suggested pronunciations for words");
      HelpOverlay.Tips.Add("Test pronunciations using the play button to hear how they sound");
      HelpOverlay.Tips.Add("Conflicts are shown when multiple entries exist for the same word");
      HelpOverlay.Tips.Add("Entries are organized by language for better management");
      HelpOverlay.Tips.Add("Search helps you quickly find existing entries");
      HelpOverlay.Tips.Add("The lexicon is automatically used during voice synthesis");
      HelpOverlay.Tips.Add("Import/export functionality is planned for a future release to enable sharing lexicons between projects");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void SearchTextBox_KeyDown(object _, KeyRoutedEventArgs e)
    {
      if (e.Key == Windows.System.VirtualKey.Enter)
      {
        if (ViewModel.SearchCommand.CanExecute(null))
        {
          ViewModel.SearchCommand.ExecuteAsync(null);
        }
        e.Handled = true;
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

            var testItem = new MenuFlyoutItem { Text = "Test Pronunciation" };
            testItem.Click += async (_, __) => await HandleEntryMenuClick("Test", entry);
            menu.Items.Add(testItem);

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

    private async System.Threading.Tasks.Task HandleEntryMenuClick(string action, object entryObj)
    {
      try
      {
        var entry = (PronunciationLexiconEntryItem)entryObj;
        switch (action.ToLower())
        {
          case "edit":
            ViewModel.SelectedEntry = entry;
            _toastService?.ShowToast(ToastType.Info, "Edit Entry", "Entry selected for editing");
            break;
          case "test":
            _toastService?.ShowToast(ToastType.Info, "Test Pronunciation", "Testing pronunciation");
            break;
          case "duplicate":
            DuplicateEntry(entry);
            break;
          case "delete":
            var dialog = new ContentDialog
            {
              Title = "Delete Entry",
              Content = "Are you sure you want to delete this pronunciation entry? This action cannot be undone.",
              PrimaryButtonText = "Delete",
              CloseButtonText = "Cancel",
              DefaultButton = ContentDialogButton.Close,
              XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
              var entryToDelete = entry;
              var entryIndex = ViewModel.Entries.IndexOf(entry);

              ViewModel.Entries.Remove(entry);

              // Register undo action
              if (_undoRedoService != null && entryIndex >= 0)
              {
                var actionObj = new SimpleAction(
                    "Delete Pronunciation Entry",
                    () => ViewModel.Entries.Insert(entryIndex, entryToDelete),
                    () => ViewModel.Entries.Remove(entryToDelete));
                _undoRedoService.RegisterAction(actionObj);
              }

              _toastService?.ShowToast(ToastType.Success, "Deleted", "Entry deleted");
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private void DuplicateEntry(object entry)
    {
      if (entry is PronunciationLexiconEntryItem originalEntry)
      {
        var duplicate = new PronunciationLexiconEntryItem(
            new PronunciationLexiconViewModel.LexiconEntryResponse
            {
              Word = $"{originalEntry.Word} (Copy)",
              Pronunciation = originalEntry.Pronunciation,
              Language = originalEntry.Language,
              PartOfSpeech = originalEntry.PartOfSpeech,
              Notes = originalEntry.Notes
            }
        );

        var entryIndex = ViewModel.Entries.IndexOf(originalEntry);
        ViewModel.Entries.Insert(entryIndex + 1, duplicate);
        ViewModel.SelectedEntry = duplicate;
        _toastService?.ShowToast(ToastType.Success, "Duplicated", "Entry duplicated");
      }
    }
  }
}