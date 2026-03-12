using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace VoiceStudio.App.Controls;

public sealed partial class ErrorOverlay : UserControl
{
  public static readonly DependencyProperty ErrorTitleProperty =
      DependencyProperty.Register(
          nameof(ErrorTitle),
          typeof(string),
          typeof(ErrorOverlay),
          new PropertyMetadata("Failed to load panel"));

  public static readonly DependencyProperty ErrorMessageProperty =
      DependencyProperty.Register(
          nameof(ErrorMessage),
          typeof(string),
          typeof(ErrorOverlay),
          new PropertyMetadata(string.Empty));

  public event EventHandler? RetryRequested;

  public string ErrorTitle
  {
    get => (string)GetValue(ErrorTitleProperty);
    set => SetValue(ErrorTitleProperty, value);
  }

  public string ErrorMessage
  {
    get => (string)GetValue(ErrorMessageProperty);
    set => SetValue(ErrorMessageProperty, value);
  }

  public ErrorOverlay()
  {
    this.InitializeComponent();
  }

  private void RetryButton_Click(object sender, RoutedEventArgs e)
  {
    RetryRequested?.Invoke(this, EventArgs.Empty);
  }
}
