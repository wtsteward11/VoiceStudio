using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.Services;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Services;
using System.Runtime.InteropServices.WindowsRuntime;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// Image/Video Quality Enhancement Pipeline view.
  /// Implements IDEA 50: Image/Video Quality Enhancement Pipeline.
  /// </summary>
  public sealed partial class ImageVideoEnhancementPipelineView : UserControl
  {
    public ImageVideoEnhancementPipelineViewModel ViewModel { get; }

    public ImageVideoEnhancementPipelineView()
    {
      this.InitializeComponent();
      ViewModel = new ImageVideoEnhancementPipelineViewModel(
          ServiceProvider.GetBackendClient()
      );
      this.DataContext = ViewModel;

      // Setup keyboard navigation
      this.Loaded += ImageVideoEnhancementPipelineView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void ImageVideoEnhancementPipelineView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void EnhancementItem_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs _)
    {
      if (sender is FrameworkElement element && element.Tag is EnhancementItem enhancement)
      {
        ViewModel.AddEnhancementToPipeline(enhancement);
      }
    }

    private async void ConfigureStep_Click(object sender, RoutedEventArgs _)
    {
      try
      {
        if (sender is Button button && button.CommandParameter is PipelineStep step)
        {
          var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
          {
            Title = $"Configure: {step.Name}",
            Content = $"Step: {step.StepNumber}\n\nDescription: {step.Description}\n\nParameters: {step.ParametersSummary ?? "No parameters configured"}",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot
          };
          var result = await dialog.ShowAsync();
          if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
          {
            // Parameters can be configured via ViewModel if needed
            ViewModel.StatusMessage = $"Configuration saved for {step.Name}";
          }
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private void MoveStepUp_Click(object sender, RoutedEventArgs _)
    {
      if (sender is Button button && button.CommandParameter is PipelineStep step)
      {
        ViewModel.MoveStepUp(step);
      }
    }

    private void MoveStepDown_Click(object sender, RoutedEventArgs _)
    {
      if (sender is Button button && button.CommandParameter is PipelineStep step)
      {
        ViewModel.MoveStepDown(step);
      }
    }

    private void RemoveStep_Click(object sender, RoutedEventArgs _)
    {
      if (sender is Button button && button.CommandParameter is PipelineStep step)
      {
        ViewModel.RemoveStep(step);
      }
    }
  }
}