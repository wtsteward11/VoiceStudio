using Microsoft.UI.Xaml.Controls;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// GAP-051: batch speech-to-speech panel; ViewModel from <see cref="UserControl.DataContext"/> (panel host).
  /// </summary>
  public sealed partial class SpeechToSpeechView : UserControl
  {
    public SpeechToSpeechView()
    {
      this.InitializeComponent();
    }

    /// <summary>Compiled x:Bind root; mirrors shell-assigned <see cref="UserControl.DataContext"/>.</summary>
    public SpeechToSpeechViewModel? ViewModel => DataContext as SpeechToSpeechViewModel;
  }
}
