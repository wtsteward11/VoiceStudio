using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;
using System;
using System.Threading;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// EmotionControlView panel - Fine-grained emotion control for voice synthesis.
  /// </summary>
  public sealed partial class EmotionControlView : Microsoft.UI.Xaml.Controls.UserControl
  {
    public EmotionControlViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;

    public EmotionControlView()
    {
      this.InitializeComponent();
      ViewModel = new EmotionControlViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IEmotionControlClient>(),
          AppServices.GetRequiredService<IDialogService>()
      );
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();

      // Add keyboard navigation
      this.KeyDown += EmotionControlView_KeyDown;

      // Setup keyboard navigation and initial data load (ADR-047: no constructor fire-and-forget)
      this.Loaded += EmotionControlView_Loaded;

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
        if (e.PropertyName == nameof(EmotionControlViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Emotion Control Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(EmotionControlViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Emotion Control", ViewModel.StatusMessage);
        }
      };
    }

    private void EmotionControlView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
      // Ctrl+A applies emotion
      if (e.Key == VirtualKey.A)
      {
        if (InputHelper.IsControlPressed() && ViewModel.ApplyEmotionCommand.CanExecute(null))
        {
          ViewModel.ApplyEmotionCommand.Execute(null);
          e.Handled = true;
        }
      }
      // Ctrl+S saves preset
      else if (e.Key == VirtualKey.S)
      {
        if (InputHelper.IsControlPressed() && ViewModel.SavePresetCommand.CanExecute(null))
        {
          ViewModel.SavePresetCommand.Execute(null);
          e.Handled = true;
        }
      }
      // Ctrl+L loads selected preset
      else if (e.Key == VirtualKey.L)
      {
        if (InputHelper.IsControlPressed() && ViewModel.LoadPresetCommand.CanExecute(null))
        {
          ViewModel.LoadPresetCommand.Execute(null);
          e.Handled = true;
        }
      }
      // Delete key removes selected preset
      else if (e.Key == VirtualKey.Delete)
      {
        if (ViewModel.SelectedPreset != null && ViewModel.DeletePresetCommand.CanExecute(null))
        {
          ViewModel.DeletePresetCommand.Execute(null);
          e.Handled = true;
        }
      }
      // F5 refreshes presets
      else if (e.Key == VirtualKey.F5)
      {
        if (ViewModel.RefreshCommand.CanExecute(null))
        {
          ViewModel.RefreshCommand.Execute(null);
          e.Handled = true;
        }
      }
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Emotion Control Help";
      HelpOverlay.HelpText = "The Emotion Control panel provides fine-grained control over emotions in voice synthesis. Select a primary emotion and adjust its intensity (0-100%). Enable emotion blending to mix a secondary emotion with the primary one. You can save your emotion settings as presets for quick reuse. Enter an audio ID to apply emotions to, then click Apply Emotion. The panel supports 9 emotions: Happy, Sad, Angry, Excited, Calm, Fearful, Surprised, Disgusted, and Neutral. Use presets to quickly apply commonly used emotion combinations.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+A", Description = "Apply emotion" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+S", Description = "Save preset" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+L", Description = "Load selected preset" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Delete", Description = "Delete selected preset" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh presets" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Primary emotion intensity controls the strength of the emotion (0-100%)");
      HelpOverlay.Tips.Add("Emotion blending allows mixing two emotions for nuanced expression");
      HelpOverlay.Tips.Add("Secondary emotion intensity controls how much the secondary emotion affects the result");
      HelpOverlay.Tips.Add("Save frequently used emotion combinations as presets");
      HelpOverlay.Tips.Add("Load presets to quickly apply saved emotion settings");
      HelpOverlay.Tips.Add("Available emotions: Happy, Sad, Angry, Excited, Calm, Fearful, Surprised, Disgusted, Neutral");
      HelpOverlay.Tips.Add("Higher intensity values create more pronounced emotional expression");
      HelpOverlay.Tips.Add("Blending works best when both emotions are complementary");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void Preset_RightTapped(object sender, RightTappedRoutedEventArgs e)
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

            var loadItem = new MenuFlyoutItem { Text = "Load" };
            loadItem.Click += async (_, _) => await HandlePresetMenuClick("Load", preset);
            menu.Items.Add(loadItem);

            var duplicateItem = new MenuFlyoutItem { Text = "Duplicate" };
            duplicateItem.Click += async (_, _) => await HandlePresetMenuClick("Duplicate", preset);
            menu.Items.Add(duplicateItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (s, e2) => await HandlePresetMenuClick("Delete", preset);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private async void PresetLoadButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs _)
    {
      try
      {
        if (sender is FrameworkElement element && element.DataContext != null)
        {
          await HandlePresetMenuClick("Load", element.DataContext);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private async void PresetDeleteButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
      try
      {
        if (sender is FrameworkElement element && element.DataContext != null)
        {
          await HandlePresetMenuClick("Delete", element.DataContext);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private async System.Threading.Tasks.Task HandlePresetMenuClick(string action, object preset)
    {
      try
      {
        switch (action.ToLower())
        {
          case "load":
            ViewModel.SelectedPreset = preset as EmotionControlPresetItem;
            ViewModel.LoadPresetCommand.Execute(null);
            _toastService?.ShowToast(ToastType.Info, "Load Preset", "Preset loaded");
            break;
          case "duplicate":
            if (preset is EmotionControlPresetItem dupPreset)
            {
              await ViewModel.DuplicatePresetAsync(dupPreset, CancellationToken.None);
              _toastService?.ShowToast(ToastType.Success, "Duplicated", "Preset duplicated");
            }
            break;
          case "delete":
            if (preset is EmotionControlPresetItem delPreset)
            {
              await ViewModel.DeletePresetWithConfirmationAsync(delPreset, CancellationToken.None);
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private async void EmotionControlView_Loaded(object sender, RoutedEventArgs e)
    {
      this.Loaded -= EmotionControlView_Loaded;
      KeyboardNavigationHelper.SetupTabNavigation(this);
      await ViewModel.InitializeAsync(CancellationToken.None);
    }
  }
}