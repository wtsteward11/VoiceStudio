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
  /// ProsodyView panel for prosody and phoneme-level control.
  /// </summary>
  public sealed partial class ProsodyView : UserControl
  {
    public ProsodyViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;

    public ProsodyView()
    {
      this.InitializeComponent();
      ViewModel = new ProsodyViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetProsodyClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(ProsodyViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Prosody Control Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(ProsodyViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Prosody Control", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation and load configs (ILifecyclePanelView; not constructor — RETAINED_ASYNC_RULE)
      this.Loaded += ProsodyView_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void ProsodyView_Loaded(object _, RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
      if (ViewModel is VoiceStudio.Core.Panels.ILifecyclePanelView lifecycle)
      {
        _ = lifecycle.OnActivatedAsync();
      }
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Prosody & Phoneme Control Help";
      HelpOverlay.HelpText = "The Prosody & Phoneme Control panel allows you to control prosody (pitch, rate, volume, intonation) and analyze phonemes in text. Create prosody configurations, analyze text to extract phonemes, and apply prosody settings to voice synthesis. Use the pitch, rate, and volume sliders to adjust speech characteristics, and select intonation patterns to control speech rhythm and melody.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+S", Description = "Save prosody configuration" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Delete", Description = "Delete selected configuration" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Prosody controls affect the rhythm, melody, and stress patterns of speech");
      HelpOverlay.Tips.Add("Pitch controls the fundamental frequency (higher = higher pitch)");
      HelpOverlay.Tips.Add("Rate controls speech speed (faster = quicker delivery)");
      HelpOverlay.Tips.Add("Intonation patterns affect the melodic contour of sentences");
      HelpOverlay.Tips.Add("Phoneme analysis breaks text into individual speech sounds");
      HelpOverlay.Tips.Add("Apply prosody configurations to voice synthesis for natural-sounding speech");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void Config_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var config = element.DataContext ?? listView.SelectedItem;
        if (config != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var editItem = new MenuFlyoutItem { Text = "Edit" };
            editItem.Click += async (_, __) => await HandleConfigMenuClick("Edit", config);
            menu.Items.Add(editItem);

            var applyItem = new MenuFlyoutItem { Text = "Apply to Synthesis" };
            applyItem.Click += async (_, __) => await HandleConfigMenuClick("Apply", config);
            menu.Items.Add(applyItem);

            var duplicateItem = new MenuFlyoutItem { Text = "Duplicate" };
            duplicateItem.Click += async (_, __) => await HandleConfigMenuClick("Duplicate", config);
            menu.Items.Add(duplicateItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, __) => await HandleConfigMenuClick("Delete", config);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private async System.Threading.Tasks.Task HandleConfigMenuClick(string action, object config)
    {
      try
      {
        switch (action.ToLower())
        {
          case "edit":
            ViewModel.SelectedConfig = (ProsodyConfigItem)config;
            _toastService?.ShowToast(ToastType.Info, "Edit Config", "Configuration selected for editing");
            break;
          case "apply":
            _toastService?.ShowToast(ToastType.Info, "Apply Config", "Applying configuration to synthesis");
            break;
          case "duplicate":
            DuplicateConfig(config);
            break;
          case "delete":
            var dialog = new ContentDialog
            {
              Title = "Delete Configuration",
              Content = "Are you sure you want to delete this prosody configuration? This action cannot be undone.",
              PrimaryButtonText = "Delete",
              CloseButtonText = "Cancel",
              DefaultButton = ContentDialogButton.Close,
              XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
              var configToDelete = (ProsodyConfigItem)config;
              var configIndex = ViewModel.Configs.IndexOf(configToDelete);

              ViewModel.Configs.Remove(configToDelete);

              // Register undo action
              if (_undoRedoService != null && configIndex >= 0)
              {
                var actionObj = new SimpleAction(
                    "Delete Prosody Configuration",
                    () => ViewModel.Configs.Insert(configIndex, configToDelete),
                    () => ViewModel.Configs.Remove(configToDelete));
                _undoRedoService.RegisterAction(actionObj);
              }

              _toastService?.ShowToast(ToastType.Success, "Deleted", "Configuration deleted");
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private void DuplicateConfig(object config)
    {
      try
      {
        var configType = config.GetType();
        var duplicatedConfig = Activator.CreateInstance(configType);
        if (duplicatedConfig != null)
        {
          foreach (var prop in configType.GetProperties())
          {
            if (prop.CanRead && prop.CanWrite && prop.GetIndexParameters().Length == 0)
            {
              var value = prop.GetValue(config);
              if (prop.Name == "Name")
              {
                prop.SetValue(duplicatedConfig, $"{value} (Copy)");
              }
              else
              {
                prop.SetValue(duplicatedConfig, value);
              }
            }
          }

          var index = ViewModel.Configs.IndexOf((ProsodyConfigItem)config);
          ViewModel.Configs.Insert(index + 1, (ProsodyConfigItem)duplicatedConfig);
          _toastService?.ShowToast(ToastType.Success, "Duplicated", "Configuration duplicated");
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Duplicate Failed", ex.Message);
      }
    }
  }
}