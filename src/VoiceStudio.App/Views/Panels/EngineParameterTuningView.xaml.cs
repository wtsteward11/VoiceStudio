using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// Advanced Engine Parameter Tuning view.
  /// Implements IDEA 51: Advanced Engine Parameter Tuning Interface.
  /// </summary>
  public sealed partial class EngineParameterTuningView : UserControl
  {
    public EngineParameterTuningViewModel ViewModel { get; }

    public EngineParameterTuningView()
    {
      this.InitializeComponent();
      ViewModel = new EngineParameterTuningViewModel(
          ServiceProvider.GetBackendClient()
      );
      this.DataContext = ViewModel;

      // Setup keyboard navigation
      this.Loaded += EngineParameterTuningView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void EngineParameterTuningView_KeyboardNavigation_Loaded(object sender, RoutedEventArgs e)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void ParameterValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs _)
    {
      if (sender is Microsoft.UI.Xaml.Controls.Slider slider && slider.DataContext is EngineParameter parameter)
      {
        ViewModel.OnParameterValueChanged(parameter);
      }
    }

    private async void ParameterInfo_Click(object sender, RoutedEventArgs _)
    {
      try
      {
        if (sender is Button button && button.CommandParameter is EngineParameter parameter)
        {
          var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
          {
            Title = $"Parameter: {parameter.Name}",
            Content = $"ID: {parameter.Id}\n\nDescription: {parameter.Description ?? "No description available"}\n\nType: {parameter.Type}\nRange: {parameter.MinValue} - {parameter.MaxValue}\nDefault: {parameter.DefaultValue}\nCurrent: {parameter.Value}",
            CloseButtonText = "Close",
            XamlRoot = this.XamlRoot
          };
          await dialog.ShowAsync();
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }
  }
}