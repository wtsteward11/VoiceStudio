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
  /// RealTimeVoiceConverterView panel for real-time voice conversion.
  /// </summary>
  public sealed partial class RealTimeVoiceConverterView : UserControl
  {
    public RealTimeVoiceConverterViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;

    public RealTimeVoiceConverterView()
    {
      this.InitializeComponent();
      ViewModel = new RealTimeVoiceConverterViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          VoiceStudio.App.Services.ServiceProvider.GetBackendClient(),
          ServiceProvider.GetProfilesClient(),
          AppServices.TryGetWebSocketClientFactory()
      );
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(RealTimeVoiceConverterViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Voice Converter Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(RealTimeVoiceConverterViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Voice Converter", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += RealTimeVoiceConverterView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void RealTimeVoiceConverterView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Real-Time Voice Converter Help";
      HelpOverlay.HelpText = "The Real-Time Voice Converter panel performs live voice conversion during audio input or playback. Select source and target voices, configure conversion parameters, and start a real-time conversion session. The converter processes audio in real-time, allowing you to hear converted audio immediately. Use for live streaming, real-time voice transformation, or instant voice conversion applications.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Space", Description = "Start/Stop conversion" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Real-time conversion requires active audio input or playback");
      HelpOverlay.Tips.Add("Lower latency settings provide faster conversion but may reduce quality");
      HelpOverlay.Tips.Add("Source and target voices should be clearly different for best results");
      HelpOverlay.Tips.Add("Conversion quality depends on audio input quality");
      HelpOverlay.Tips.Add("Adjust conversion strength to balance similarity and naturalness");

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

            var startItem = new MenuFlyoutItem { Text = "Start Session" };
            startItem.Click += async (_, _) => await HandleSessionMenuClick("Start", session);
            menu.Items.Add(startItem);

            var stopItem = new MenuFlyoutItem { Text = "Stop Session" };
            stopItem.Click += async (_, _) => await HandleSessionMenuClick("Stop", session);
            menu.Items.Add(stopItem);

            var editItem = new MenuFlyoutItem { Text = "Edit" };
            editItem.Click += async (_, _) => await HandleSessionMenuClick("Edit", session);
            menu.Items.Add(editItem);

            var duplicateItem = new MenuFlyoutItem { Text = "Duplicate" };
            duplicateItem.Click += async (_, _) => await HandleSessionMenuClick("Duplicate", session);
            menu.Items.Add(duplicateItem);

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

    private async System.Threading.Tasks.Task HandleSessionMenuClick(string action, object session)
    {
      try
      {
        if (session is ConverterSessionItem sessionItem)
        {
          switch (action.ToLower())
          {
            case "start":
              ViewModel.SelectedSession = sessionItem;
              if (ViewModel.StartSessionCommand.CanExecute(null))
              {
                await ViewModel.StartSessionCommand.ExecuteAsync(null);
              }
              break;
            case "stop":
              ViewModel.SelectedSession = sessionItem;
              if (ViewModel.StopSessionCommand.CanExecute(null))
              {
                await ViewModel.StopSessionCommand.ExecuteAsync(null);
              }
              break;
            case "edit":
              ViewModel.SelectedSession = sessionItem;
              _toastService?.ShowToast(ToastType.Info, "Edit Session", "Session selected for editing");
              break;
            case "duplicate":
              await DuplicateSession(sessionItem);
              break;
            case "delete":
              var dialog = new ContentDialog
              {
                Title = "Delete Session",
                Content = "Are you sure you want to delete this conversion session? This action cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
              };

              var result = await dialog.ShowAsync();
              if (result == ContentDialogResult.Primary && ViewModel.DeleteSessionCommand.CanExecute(sessionItem))
              {
                await ViewModel.DeleteSessionCommand.ExecuteAsync(sessionItem);
              }
              break;
          }
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private async System.Threading.Tasks.Task DuplicateSession(ConverterSessionItem session)
    {
      try
      {
        // Create a new session with the same source and target profiles
        ViewModel.SourceProfileId = session.SourceProfileId;
        ViewModel.TargetProfileId = session.TargetProfileId;

        // Start a new session using the ViewModel's StartSessionCommand
        if (ViewModel.StartSessionCommand.CanExecute(null))
        {
          await ViewModel.StartSessionCommand.ExecuteAsync(null);
          _toastService?.ShowToast(ToastType.Success, "Duplicated", "New session created with same profiles");
        }
        else
        {
          _toastService?.ShowToast(ToastType.Warning, "Cannot Duplicate", "Please ensure both source and target profiles are available");
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Duplicate Failed", ex.Message);
      }
    }
  }
}