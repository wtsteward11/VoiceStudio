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
  /// EmotionStyleControlView panel for emotion and style control.
  /// </summary>
  public sealed partial class EmotionStyleControlView : UserControl
  {
    public EmotionStyleControlViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;

    public EmotionStyleControlView()
    {
      this.InitializeComponent();
      ViewModel = new EmotionStyleControlViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IEmotionStyleClient>()
      );
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(EmotionStyleControlViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Emotion & Style Control Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(EmotionStyleControlViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Emotion & Style Control", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation and initial data load (ADR-047: no constructor fire-and-forget)
      this.Loaded += EmotionStyleControlView_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private async void EmotionStyleControlView_Loaded(object sender, RoutedEventArgs e)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
      await ViewModel.InitializeAsync(System.Threading.CancellationToken.None);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Emotion & Style Control Help";
      HelpOverlay.HelpText = "The Emotion & Style Control panel allows you to apply emotional and stylistic variations to voice synthesis. Select emotion presets or create custom emotions with adjustable intensity. Apply style presets to modify the speaking style of synthesized speech.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+Enter", Description = "Apply emotion & style" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh presets" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Escape", Description = "Close help" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Emotion presets provide quick access to common emotional expressions");
      HelpOverlay.Tips.Add("Adjust intensity to control how strongly the emotion is expressed");
      HelpOverlay.Tips.Add("Style presets modify speaking characteristics like pace and tone");
      HelpOverlay.Tips.Add("Custom emotions allow fine-grained control over emotional expression");
      HelpOverlay.Tips.Add("Combine emotion and style for unique voice characteristics");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void EmotionPreset_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var preset = element.DataContext ?? listView.SelectedItem;
        if (preset != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var applyItem = new MenuFlyoutItem { Text = "Apply" };
            applyItem.Click += async (_, _) => await HandleEmotionPresetMenuClick("Apply", preset);
            menu.Items.Add(applyItem);

            var duplicateItem = new MenuFlyoutItem { Text = "Duplicate" };
            duplicateItem.Click += async (_, _) => await HandleEmotionPresetMenuClick("Duplicate", preset);
            menu.Items.Add(duplicateItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, _) => await HandleEmotionPresetMenuClick("Delete", preset);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private void StylePreset_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var preset = element.DataContext ?? listView.SelectedItem;
        if (preset != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var applyItem = new MenuFlyoutItem { Text = "Apply" };
            applyItem.Click += async (_, _) => await HandleStylePresetMenuClick("Apply", preset);
            menu.Items.Add(applyItem);

            var duplicateItem = new MenuFlyoutItem { Text = "Duplicate" };
            duplicateItem.Click += async (_, _) => await HandleStylePresetMenuClick("Duplicate", preset);
            menu.Items.Add(duplicateItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, _) => await HandleStylePresetMenuClick("Delete", preset);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private async System.Threading.Tasks.Task HandleEmotionPresetMenuClick(string action, object preset)
    {
      try
      {
        switch (action.ToLower())
        {
          case "apply":
            ViewModel.SelectedEmotionPreset = (EmotionStylePresetItem)preset;
            _toastService?.ShowToast(ToastType.Success, "Applied", "Emotion preset applied");
            break;
          case "duplicate":
            DuplicateEmotionPreset((EmotionStylePresetItem)preset);
            break;
          case "delete":
            var dialog = new ContentDialog
            {
              Title = "Delete Emotion Preset",
              Content = "Are you sure you want to delete this emotion preset? This action cannot be undone.",
              PrimaryButtonText = "Delete",
              CloseButtonText = "Cancel",
              DefaultButton = ContentDialogButton.Close,
              XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
              var presetToDelete = (EmotionStylePresetItem)preset;
              var presetIndex = ViewModel.EmotionPresets.IndexOf(presetToDelete);

              ViewModel.EmotionPresets.Remove(presetToDelete);

              // Register undo action
              if (_undoRedoService != null && presetIndex >= 0)
              {
                var actionObj = new SimpleAction(
                    "Delete Emotion Preset",
                    () => ViewModel.EmotionPresets.Insert(presetIndex, presetToDelete),
                    () => ViewModel.EmotionPresets.Remove(presetToDelete));
                _undoRedoService.RegisterAction(actionObj);
              }

              _toastService?.ShowToast(ToastType.Success, "Deleted", "Emotion preset deleted");
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private async System.Threading.Tasks.Task HandleStylePresetMenuClick(string action, object preset)
    {
      try
      {
        switch (action.ToLower())
        {
          case "apply":
            ViewModel.SelectedStylePreset = (StylePresetItem)preset;
            _toastService?.ShowToast(ToastType.Success, "Applied", "Style preset applied");
            break;
          case "duplicate":
            DuplicateStylePreset((StylePresetItem)preset);
            break;
          case "delete":
            var dialog = new ContentDialog
            {
              Title = "Delete Style Preset",
              Content = "Are you sure you want to delete this style preset? This action cannot be undone.",
              PrimaryButtonText = "Delete",
              CloseButtonText = "Cancel",
              DefaultButton = ContentDialogButton.Close,
              XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
              var presetToDelete = (StylePresetItem)preset;
              var presetIndex = ViewModel.StylePresets.IndexOf(presetToDelete);

              ViewModel.StylePresets.Remove(presetToDelete);

              // Register undo action
              if (_undoRedoService != null && presetIndex >= 0)
              {
                var actionObj = new SimpleAction(
                    "Delete Style Preset",
                    () => ViewModel.StylePresets.Insert(presetIndex, presetToDelete),
                    () => ViewModel.StylePresets.Remove(presetToDelete));
                _undoRedoService.RegisterAction(actionObj);
              }

              _toastService?.ShowToast(ToastType.Success, "Deleted", "Style preset deleted");
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private void DuplicateEmotionPreset(object preset)
    {
      try
      {
        var presetType = preset.GetType();
        var duplicatedPreset = Activator.CreateInstance(presetType);
        if (duplicatedPreset != null)
        {
          foreach (var prop in presetType.GetProperties())
          {
            if (prop.CanRead && prop.CanWrite && prop.GetIndexParameters().Length == 0)
            {
              var value = prop.GetValue(preset);
              if (prop.Name == "Name")
              {
                prop.SetValue(duplicatedPreset, $"{value} (Copy)");
              }
              else
              {
                prop.SetValue(duplicatedPreset, value);
              }
            }
          }

          var index = ViewModel.EmotionPresets.IndexOf((EmotionStylePresetItem)preset);
          ViewModel.EmotionPresets.Insert(index + 1, (EmotionStylePresetItem)duplicatedPreset);
          _toastService?.ShowToast(ToastType.Success, "Duplicated", "Emotion preset duplicated");
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Duplicate Failed", ex.Message);
      }
    }

    private void DuplicateStylePreset(object preset)
    {
      try
      {
        var presetType = preset.GetType();
        var duplicatedPreset = Activator.CreateInstance(presetType);
        if (duplicatedPreset != null)
        {
          foreach (var prop in presetType.GetProperties())
          {
            if (prop.CanRead && prop.CanWrite && prop.GetIndexParameters().Length == 0)
            {
              var value = prop.GetValue(preset);
              if (prop.Name == "Name")
              {
                prop.SetValue(duplicatedPreset, $"{value} (Copy)");
              }
              else
              {
                prop.SetValue(duplicatedPreset, value);
              }
            }
          }

          var index = ViewModel.StylePresets.IndexOf((StylePresetItem)preset);
          ViewModel.StylePresets.Insert(index + 1, (StylePresetItem)duplicatedPreset);
          _toastService?.ShowToast(ToastType.Success, "Duplicated", "Style preset duplicated");
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Duplicate Failed", ex.Message);
      }
    }
  }
}