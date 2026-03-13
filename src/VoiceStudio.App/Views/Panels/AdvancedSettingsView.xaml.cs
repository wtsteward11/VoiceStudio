using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Utilities;

namespace VoiceStudio.App.Views.Panels
{
  public sealed partial class AdvancedSettingsView : UserControl
  {
    public AdvancedSettingsViewModel ViewModel { get; }

    public AdvancedSettingsView()
    {
      InitializeComponent();
      ViewModel = new AdvancedSettingsViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetAdvancedSettingsClient()
      );
      DataContext = ViewModel;
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.Hide();
        }
      });
    }

    private void CacheSizeBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs _)
    {
      ViewModel.CacheSizeMb = (int)sender.Value;
    }

    private void MaxThreadsBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs _)
    {
      ViewModel.MaxThreads = (int)sender.Value;
    }

    private void MemoryLimitBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs _)
    {
      ViewModel.MemoryLimitMb = sender.Value;
    }

    private void SampleRateBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs _)
    {
      ViewModel.DefaultSampleRate = (int)sender.Value;
    }

    private void BitDepthBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs _)
    {
      ViewModel.DefaultBitDepth = (int)sender.Value;
    }

    private void FadeDurationBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs _)
    {
      ViewModel.FadeDurationMs = (int)sender.Value;
    }

    private void TimeoutBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs _)
    {
      ViewModel.TimeoutSeconds = (int)sender.Value;
    }

    private void RetryBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs _)
    {
      ViewModel.RetryAttempts = (int)sender.Value;
    }

    private void BatchSizeBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs _)
    {
      ViewModel.BatchSize = (int)sender.Value;
    }
  }
}