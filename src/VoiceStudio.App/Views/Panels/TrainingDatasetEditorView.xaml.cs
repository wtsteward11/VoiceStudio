using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using System;
using System.Threading.Tasks;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// TrainingDatasetEditorView panel for advanced dataset editing.
  /// </summary>
  public sealed partial class TrainingDatasetEditorView : UserControl
  {
    public TrainingDatasetEditorViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;

    public TrainingDatasetEditorView()
    {
      this.InitializeComponent();
      ViewModel = new TrainingDatasetEditorViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetRequiredService<VoiceStudio.Core.Services.ITrainingClient>(),
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IBackendClient>()
      );
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(TrainingDatasetEditorViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Dataset Editor Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(TrainingDatasetEditorViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Dataset Editor", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += TrainingDatasetEditorView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void TrainingDatasetEditorView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Dataset Editor Help";
      HelpOverlay.HelpText = "The Dataset Editor panel allows you to create, edit, and manage training datasets for voice model fine-tuning. Add audio files to datasets, edit metadata (text transcripts, speaker labels), organize samples, and prepare data for training. The editor provides tools for dataset validation, quality checking, WAV repair, bad clip marking, and optimization to ensure the best training results.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh dataset list" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Enter", Description = "Add audio file to dataset" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+A", Description = "Select all samples" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Delete", Description = "Remove selected samples" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("High-quality audio samples improve training results");
      HelpOverlay.Tips.Add("Accurate transcripts are essential for good model training");
      HelpOverlay.Tips.Add("Organize samples by speaker for multi-speaker models");
      HelpOverlay.Tips.Add("Validate dataset quality before starting training");
      HelpOverlay.Tips.Add("Use WAV repair tool to fix corrupted audio files");
      HelpOverlay.Tips.Add("Mark bad clips to exclude them from training");
      HelpOverlay.Tips.Add("Dataset QA reports help identify quality issues");
      HelpOverlay.Tips.Add("Dataset optimization can improve training efficiency");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void AudioFile_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var audioFile = element.DataContext ?? listView.SelectedItem;
        if (audioFile != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var editItem = new MenuFlyoutItem { Text = "Edit" };
            editItem.Click += async (_, _) => await HandleAudioFileMenuClick("Edit", audioFile);
            menu.Items.Add(editItem);

            var duplicateItem = new MenuFlyoutItem { Text = "Duplicate" };
            duplicateItem.Click += async (_, _) => await HandleAudioFileMenuClick("Duplicate", audioFile);
            menu.Items.Add(duplicateItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Remove" };
            deleteItem.Click += async (_, _) => await HandleAudioFileMenuClick("Remove", audioFile);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private async System.Threading.Tasks.Task HandleAudioFileMenuClick(string action, object audioFile)
    {
      try
      {
        switch (action.ToLower())
        {
          case "edit":
            ViewModel.SelectedAudioFile = (DatasetAudioFileItem)audioFile;
            _toastService?.ShowToast(ToastType.Info, "Edit Audio File", "Audio file selected for editing");
            break;
          case "duplicate":
            await DuplicateAudioFile(audioFile);
            break;
          case "remove":
            var dialog = new ContentDialog
            {
              Title = "Remove Audio File",
              Content = "Are you sure you want to remove this audio file from the dataset? This action cannot be undone.",
              PrimaryButtonText = "Remove",
              CloseButtonText = "Cancel",
              DefaultButton = ContentDialogButton.Close,
              XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
              var audioFileToRemove = (DatasetAudioFileItem)audioFile;
              if (ViewModel.DatasetDetail?.AudioFiles == null)
                return;

              var audioFileIndex = ViewModel.DatasetDetail.AudioFiles.IndexOf(audioFileToRemove);

              ViewModel.DatasetDetail.AudioFiles.Remove(audioFileToRemove);

              // Register undo action
              if (_undoRedoService != null && audioFileIndex >= 0)
              {
                var actionObj = new SimpleAction(
                    "Remove Audio File from Dataset",
                    () => ViewModel.DatasetDetail.AudioFiles.Insert(audioFileIndex, audioFileToRemove),
                    () => ViewModel.DatasetDetail.AudioFiles.Remove(audioFileToRemove));
                _undoRedoService.RegisterAction(actionObj);
              }

              _toastService?.ShowToast(ToastType.Success, "Removed", "Audio file removed from dataset");
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private async Task DuplicateAudioFile(object audioFile)
    {
      try
      {
        if (audioFile is DatasetAudioFileItem item && ViewModel.DatasetDetail != null)
        {
          // Set the ViewModel's NewAudioId and NewTranscript to duplicate the audio file
          ViewModel.NewAudioId = item.AudioId;
          ViewModel.NewTranscript = item.Transcript;

          // Add the audio file using the ViewModel's AddAudioCommand
          if (ViewModel.AddAudioCommand.CanExecute(null))
          {
            await ViewModel.AddAudioCommand.ExecuteAsync(null);
            _toastService?.ShowToast(ToastType.Success, "Duplicated", "Audio file duplicated");
          }
          else
          {
            _toastService?.ShowToast(ToastType.Warning, "Cannot Duplicate", "Please select a dataset first");
          }
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Duplicate Failed", ex.Message);
      }
    }
  }
}