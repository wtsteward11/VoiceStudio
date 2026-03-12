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
  /// VoiceMorphView panel for voice morphing and blending.
  /// </summary>
  public sealed partial class VoiceMorphView : UserControl
  {
    public VoiceMorphViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;

    public VoiceMorphView()
    {
      this.InitializeComponent();
      ViewModel = new VoiceMorphViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          VoiceStudio.App.Services.ServiceProvider.GetBackendClient(),
          AppServices.GetProjectsClient(),
          ServiceProvider.GetProfilesClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(VoiceMorphViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Voice Morphing Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(VoiceMorphViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Voice Morphing", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += VoiceMorphView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void VoiceMorphView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Voice Morphing Help";
      HelpOverlay.HelpText = "The Voice Morphing panel allows you to blend characteristics from multiple voice profiles to create unique voice variations. Create morph configurations with multiple target voices and adjust blend weights to control how much each voice contributes. Use morphing strength to control the overall intensity of the morph. Voice morphing preserves emotion and prosody while blending voice characteristics like timbre and pitch. Experiment with different combinations to create custom voice variations.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+S", Description = "Save morph configuration" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+M", Description = "Apply morph" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Delete", Description = "Delete selected configuration" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Voice morphing blends characteristics from multiple voices");
      HelpOverlay.Tips.Add("Adjust blend weights to control each voice's contribution");
      HelpOverlay.Tips.Add("Higher morph strength applies more blending");
      HelpOverlay.Tips.Add("Lower morph strength preserves more original voice");
      HelpOverlay.Tips.Add("Morphing preserves emotion and prosody characteristics");
      HelpOverlay.Tips.Add("Experiment with different voice combinations for unique results");
      HelpOverlay.Tips.Add("Save morph configurations for reuse");

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
            editItem.Click += async (_, _) => await HandleConfigMenuClick("Edit", config);
            menu.Items.Add(editItem);

            var duplicateItem = new MenuFlyoutItem { Text = "Duplicate" };
            duplicateItem.Click += async (_, _) => await HandleConfigMenuClick("Duplicate", config);
            menu.Items.Add(duplicateItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, _) => await HandleConfigMenuClick("Delete", config);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private void TargetVoice_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var targetVoice = element.DataContext ?? listView.SelectedItem;
        if (targetVoice != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var removeItem = new MenuFlyoutItem { Text = "Remove" };
            removeItem.Click += async (_, _) => await HandleTargetVoiceMenuClick("Remove", targetVoice);
            menu.Items.Add(removeItem);

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
            ViewModel.SelectedConfig = config as VoiceStudio.App.ViewModels.MorphConfigItem;
            _toastService?.ShowToast(ToastType.Info, "Edit Config", "Configuration selected for editing");
            break;
          case "duplicate":
            if (config is VoiceStudio.App.ViewModels.MorphConfigItem configItem)
            {
              DuplicateConfig(configItem);
            }
            break;
          case "delete":
            var dialog = new ContentDialog
            {
              Title = "Delete Configuration",
              Content = "Are you sure you want to delete this morph configuration? This action cannot be undone.",
              PrimaryButtonText = "Delete",
              CloseButtonText = "Cancel",
              DefaultButton = ContentDialogButton.Close,
              XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && config is VoiceStudio.App.ViewModels.MorphConfigItem configItemToDelete)
            {
              var configIndex = ViewModel.Configs.IndexOf(configItemToDelete);

              ViewModel.Configs.Remove(configItemToDelete);

              // Register undo action
              if (_undoRedoService != null && configIndex >= 0)
              {
                var configToDelete = configItemToDelete; // Capture for closure
                var actionObj = new SimpleAction(
                    "Delete Morph Configuration",
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

    private async System.Threading.Tasks.Task HandleTargetVoiceMenuClick(string action, object targetVoice)
    {
      try
      {
        switch (action.ToLower())
        {
          case "remove":
            var dialog = new ContentDialog
            {
              Title = "Remove Target Voice",
              Content = "Are you sure you want to remove this target voice from the morph configuration?",
              PrimaryButtonText = "Remove",
              CloseButtonText = "Cancel",
              DefaultButton = ContentDialogButton.Close,
              XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
              var voiceToRemove = (VoiceBlendItem)targetVoice;
              var voiceIndex = ViewModel.TargetVoices.IndexOf(voiceToRemove);

              ViewModel.TargetVoices.Remove(voiceToRemove);

              // Register undo action
              if (_undoRedoService != null && voiceIndex >= 0)
              {
                var actionObj = new SimpleAction(
                    "Remove Target Voice",
                    () => ViewModel.TargetVoices.Insert(voiceIndex, voiceToRemove),
                    () => ViewModel.TargetVoices.Remove(voiceToRemove));
                _undoRedoService.RegisterAction(actionObj);
              }

              _toastService?.ShowToast(ToastType.Success, "Removed", "Target voice removed");
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private void DuplicateConfig(VoiceStudio.App.ViewModels.MorphConfigItem config)
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

          var index = ViewModel.Configs.IndexOf(config);
          ViewModel.Configs.Insert(index + 1, (MorphConfigItem)duplicatedConfig);
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