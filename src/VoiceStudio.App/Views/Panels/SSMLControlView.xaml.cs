using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using System;
using System.Threading;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// SSMLControlView panel for SSML editing.
  /// </summary>
  public sealed partial class SSMLControlView : UserControl
  {
    public SSMLControlViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;

    public SSMLControlView()
    {
      this.InitializeComponent();
      ViewModel = new SSMLControlViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetRequiredService<VoiceStudio.Core.Services.ISSMLClient>(),
          AppServices.GetAudioPlayerService(),
          AppServices.GetRequiredService<IDialogService>()
      );
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(SSMLControlViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "SSML Editor Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(SSMLControlViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "SSML Editor", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation and initial data load (ADR-047)
      this.Loaded += SSMLControlView_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private async void SSMLControlView_Loaded(object _, RoutedEventArgs __)
    {
      this.Loaded -= SSMLControlView_Loaded;
      KeyboardNavigationHelper.SetupTabNavigation(this);
      await ViewModel.InitializeAsync(CancellationToken.None);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "SSML Editor Help";
      HelpOverlay.HelpText = "The SSML Editor allows you to create and edit Speech Synthesis Markup Language (SSML) documents. SSML provides fine-grained control over speech synthesis, including pronunciation, prosody, emphasis, and breaks. Create, validate, preview, and manage SSML documents for advanced voice synthesis control.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+N", Description = "Create new document" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+S", Description = "Save document" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Validate SSML" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+P", Description = "Preview SSML" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Escape", Description = "Close help" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("SSML provides precise control over speech synthesis parameters");
      HelpOverlay.Tips.Add("Use <prosody> tags to control rate, pitch, and volume");
      HelpOverlay.Tips.Add("Use <break> tags to add pauses and control timing");
      HelpOverlay.Tips.Add("Use <emphasis> tags to add emphasis to specific words");
      HelpOverlay.Tips.Add("Validate SSML before previewing to catch syntax errors");
      HelpOverlay.Tips.Add("Preview SSML to hear how it will sound before saving");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void Document_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var document = element.DataContext ?? listView.SelectedItem;
        if (document != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var editItem = new MenuFlyoutItem { Text = "Edit" };
            editItem.Click += async (_, _) => await HandleDocumentMenuClick("Edit", document);
            menu.Items.Add(editItem);

            var validateItem = new MenuFlyoutItem { Text = "Validate" };
            validateItem.Click += async (_, _) => await HandleDocumentMenuClick("Validate", document);
            menu.Items.Add(validateItem);

            var previewItem = new MenuFlyoutItem { Text = "Preview" };
            previewItem.Click += async (_, _) => await HandleDocumentMenuClick("Preview", document);
            menu.Items.Add(previewItem);

            var duplicateItem = new MenuFlyoutItem { Text = "Duplicate" };
            duplicateItem.Click += async (_, _) => await HandleDocumentMenuClick("Duplicate", document);
            menu.Items.Add(duplicateItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, _) => await HandleDocumentMenuClick("Delete", document);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private async System.Threading.Tasks.Task HandleDocumentMenuClick(string action, object document)
    {
      try
      {
        switch (action.ToLower())
        {
          case "edit":
            ViewModel.SelectedDocument = (SSMLDocumentItem)document;
            _toastService?.ShowToast(ToastType.Info, "Edit Document", "Document selected for editing");
            break;
          case "validate":
            _toastService?.ShowToast(ToastType.Info, "Validate", "Validating SSML document");
            break;
          case "preview":
            _toastService?.ShowToast(ToastType.Info, "Preview", "Previewing SSML document");
            break;
          case "duplicate":
            DuplicateDocument(document);
            break;
          case "delete":
            if (document is SSMLDocumentItem delDoc)
            {
              await ViewModel.DeleteDocumentWithConfirmationAsync(delDoc, CancellationToken.None);
              _toastService?.ShowToast(ToastType.Success, "Deleted", "Document deleted");
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private void DuplicateDocument(object document)
    {
      try
      {
        if (document is not SSMLDocumentItem sourceDoc)
        {
          _toastService?.ShowToast(ToastType.Warning, "Warning", "Invalid document type");
          return;
        }

        // Create a new SSMLDocument with a new ID and copied content
        var newDocument = new SSMLDocument
        {
          Id = Guid.NewGuid().ToString(),
          Name = $"{sourceDoc.Name} Copy",
          Content = sourceDoc.Content,
          ProfileId = sourceDoc.ProfileId,
          ProjectId = sourceDoc.ProjectId,
          Created = DateTime.UtcNow.ToString("o"),
          Modified = DateTime.UtcNow.ToString("o")
        };

        // Wrap in SSMLDocumentItem and add to collection
        var duplicatedItem = new SSMLDocumentItem(newDocument);

        // Find the index to insert after the original document
        var insertIndex = ViewModel.Documents.IndexOf(sourceDoc) + 1;
        if (insertIndex < ViewModel.Documents.Count)
        {
          ViewModel.Documents.Insert(insertIndex, duplicatedItem);
        }
        else
        {
          ViewModel.Documents.Add(duplicatedItem);
        }

        // Register undo action
        var undoRedoService = ServiceProvider.GetUndoRedoService();
        if (undoRedoService != null)
        {
          var actionObj = new SimpleAction(
              $"Duplicate Document: {sourceDoc.Name}",
              () =>
              {
                ViewModel.Documents.Remove(duplicatedItem);
                _toastService?.ShowToast(ToastType.Info, "Undo", $"Removed duplicated document '{duplicatedItem.Name}'");
              },
              () =>
              {
                if (insertIndex < ViewModel.Documents.Count)
                {
                  ViewModel.Documents.Insert(insertIndex, duplicatedItem);
                }
                else
                {
                  ViewModel.Documents.Add(duplicatedItem);
                }
                _toastService?.ShowToast(ToastType.Info, "Redo", $"Restored duplicated document '{duplicatedItem.Name}'");
              });
          undoRedoService.RegisterAction(actionObj);
        }

        // Select the duplicated document
        ViewModel.SelectedDocument = duplicatedItem;

        _toastService?.ShowToast(ToastType.Success, "Duplicated", $"Duplicated document '{sourceDoc.Name}'");
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to duplicate document: {ex.Message}");
      }
    }
  }
}