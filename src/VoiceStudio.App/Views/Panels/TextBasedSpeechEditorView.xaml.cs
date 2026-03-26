using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// TextBasedSpeechEditorView panel - Edit audio by editing its transcript.
  /// </summary>
  public sealed partial class TextBasedSpeechEditorView : UserControl
  {
    public TextBasedSpeechEditorViewModel ViewModel { get; }

    public TextBasedSpeechEditorView()
    {
      this.InitializeComponent();
      ViewModel = new TextBasedSpeechEditorViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetRequiredService<VoiceStudio.Core.Services.ITextBasedSpeechEditorClient>(),
          ServiceProvider.GetProfilesClient()
      );
      this.DataContext = ViewModel;
    }

    private void HelpButton_Click(object _, RoutedEventArgs __)
    {
      HelpOverlay.Title = "Text-Based Speech Editor Help";
      HelpOverlay.HelpText =
          "Edit audio by editing its transcript. Load audio by ID, transcribe to generate segments, align the transcript, then replace/delete words or insert new text to create an edited result.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Right-click segment", Description = "Segment actions (select)" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Transcribe generates word timestamps for segment editing");
      HelpOverlay.Tips.Add("Align improves segment timing against the audio");
      HelpOverlay.Tips.Add("Replace/delete operates on the selected word");

      HelpOverlay.Visibility = Visibility.Visible;
      HelpOverlay.Show();
    }

    private void Segment_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var segment = element.DataContext as TranscriptSegmentItem ?? listView.SelectedItem as TranscriptSegmentItem;
        if (segment != null)
        {
          e.Handled = true;
          ViewModel.SelectedSegment = segment;
        }
      }
    }
  }
}