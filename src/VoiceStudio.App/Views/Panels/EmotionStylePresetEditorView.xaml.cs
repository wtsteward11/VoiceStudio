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
  }
}