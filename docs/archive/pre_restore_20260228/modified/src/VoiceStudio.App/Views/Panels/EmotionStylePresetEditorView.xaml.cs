using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.Services;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// Emotion/Style Preset Visual Editor view.
  /// Implements IDEA 31: Emotion/Style Preset Visual Editor.
  /// </summary>
  public sealed partial class EmotionStylePresetEditorView : UserControl
  {
    public EmotionStylePresetEditorViewModel ViewModel { get; }

    public EmotionStylePresetEditorView()
    {
      this.InitializeComponent();
      ViewModel = new EmotionStylePresetEditorViewModel(
          ServiceProvider.GetBackendClient()
      );
      this.DataContext = ViewModel;

      // Setup keyboard navigation
      this.Loaded += EmotionStylePresetEditorView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void EmotionStylePresetEditorView_KeyboardNavigation_Loaded(object sender, RoutedEventArgs e)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void EmotionButton_Click(object sender, RoutedEventArgs _)
    {
      if (sender is Button button && button.Tag is string emotionName)
      {
        ViewModel.AddEmotion(emotionName);
      }
    }

    private void RemoveEmotion_Click(object sender, RoutedEventArgs _)
    {
      if (sender is Button button && button.CommandParameter is SelectedEmotion emotion)
      {
        ViewModel.RemoveEmotion(emotion);
      }
    }

    private void HelpButton_Click(object _, RoutedEventArgs __)
    {
      HelpOverlay.Title = "Emotion Style Preset Editor Help";
      HelpOverlay.HelpText = "The Emotion Style Preset Editor lets you visually create and edit emotion and style presets. Add emotions with customizable intensity, adjust speaking rate, pitch, energy, and pause duration. Preview presets before applying them to synthesis.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+S", Description = "Save preset" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Delete", Description = "Delete selected preset" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Escape", Description = "Close help" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Click emotion buttons to add them to the preset");
      HelpOverlay.Tips.Add("Adjust speaking rate, pitch, energy, and pause duration for style control");
      HelpOverlay.Tips.Add("Preview the preset to hear how it sounds before saving");
      HelpOverlay.Tips.Add("Apply presets directly to the synthesis panel");

      HelpOverlay.Visibility = Visibility.Visible;
      HelpOverlay.Show();
    }
  }
}