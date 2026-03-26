using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;

// Use Microsoft.UI.Colors for WinUI 3
using Colors = Microsoft.UI.Colors;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// PipelineConversationView panel - Voice AI pipeline conversation interface.
  /// Supports LLM chat with optional TTS output.
  /// </summary>
  public sealed partial class PipelineConversationView : UserControl
  {
    public PipelineConversationViewModel ViewModel { get; }
    private ToastNotificationService? _toastService;

    public PipelineConversationView()
    {
      this.InitializeComponent();
      ViewModel = new PipelineConversationViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          ServiceProvider.GetPipelineConversationClient(),
          AppServices.TryGetWebSocketClientFactory()
      );
      DataContext = ViewModel;

      // Initialize services
      _toastService = ServiceProvider.GetToastNotificationService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(PipelineConversationViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Pipeline Error", ViewModel.ErrorMessage);
        }
      };

      // Handle Enter key to send message
      InputTextBox.KeyDown += OnInputKeyDown;

      // Load providers on load
      Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
      await ViewModel.RefreshProvidersCommand.ExecuteAsync(null);
    }

    private void OnInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
      if (e.Key == Windows.System.VirtualKey.Enter && !string.IsNullOrWhiteSpace(ViewModel.UserInput))
      {
        if (ViewModel.SendMessageCommand.CanExecute(null))
        {
          ViewModel.SendMessageCommand.Execute(null);
          e.Handled = true;
        }
      }
    }

    /// <summary>
    /// Helper method for x:Bind to get connection status color.
    /// </summary>
    public SolidColorBrush GetConnectionColor(bool isConnected)
    {
      return new SolidColorBrush(isConnected ? Colors.LimeGreen : Colors.Gray);
    }

    /// <summary>
    /// Helper method for x:Bind to get connection status text.
    /// </summary>
    public string GetConnectionStatus(bool isConnected)
    {
      return isConnected ? "Connected" : "Disconnected";
    }

    /// <summary>
    /// Helper method for x:Bind to negate boolean.
    /// </summary>
    public bool Not(bool value)
    {
      return !value;
    }
  }
}
