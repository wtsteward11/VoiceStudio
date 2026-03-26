using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.Core.Models;
using System;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// SpectrogramView panel for advanced spectrogram visualization.
  /// </summary>
  public sealed partial class SpectrogramView : UserControl
  {
    public SpectrogramViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;

    public SpectrogramView()
    {
      this.InitializeComponent();
      ViewModel = new SpectrogramViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          ServiceProvider.GetSpectrogramClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();

      // Update spectrogram control when data changes
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(ViewModel.SpectrogramData))
        {
          UpdateSpectrogramControl();
        }
        else if (e.PropertyName == nameof(SpectrogramViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Spectrogram Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(SpectrogramViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Spectrogram", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += SpectrogramView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void UpdateSpectrogramControl()
    {
      if (SpectrogramControl == null)
      {
        return;
      }

      if (ViewModel.SpectrogramData?.Frames != null)
      {
        SpectrogramControl.Frames = ViewModel.SpectrogramData.Frames;
      }
      else
      {
        SpectrogramControl.Frames = Array.Empty<object>();
      }
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Spectrogram Help";
      HelpOverlay.HelpText = "The Spectrogram panel displays frequency content over time for audio files. Spectrograms help visualize audio characteristics, identify frequencies, and analyze audio quality. Adjust parameters like window size and overlap to customize the visualization.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Enter", Description = "Load spectrogram" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Escape", Description = "Close help" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Enter an audio ID or filename to generate a spectrogram");
      HelpOverlay.Tips.Add("Window size affects frequency resolution - larger windows = better frequency detail");
      HelpOverlay.Tips.Add("Overlap improves time resolution but increases computation time");
      HelpOverlay.Tips.Add("Spectrograms help identify noise, harmonics, and frequency content");
      HelpOverlay.Tips.Add("Use spectrograms to verify audio quality and identify issues");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void SpectrogramControl_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (ViewModel.SpectrogramData == null)
        return;

      e.Handled = true;
      if (_contextMenuService != null)
      {
        var menu = new MenuFlyout();

        var exportItem = new MenuFlyoutItem { Text = "Export Spectrogram" };
        exportItem.Click += (_, _) =>
        {
          try
          {
            _toastService?.ShowToast(ToastType.Info, "Spectrogram", "Exporting spectrogram...");
            // Note: Export functionality will be implemented when backend export endpoint is available
          }
          catch (Exception ex)
          {
            _toastService?.ShowToast(ToastType.Error, "Export Error", ex.Message);
          }
        };
        menu.Items.Add(exportItem);

        var refreshItem = new MenuFlyoutItem { Text = "Refresh" };
        refreshItem.Click += async (_, _) =>
        {
          if (ViewModel.LoadSpectrogramCommand.CanExecute(null))
            await ViewModel.LoadSpectrogramCommand.ExecuteAsync(null);
        };
        menu.Items.Add(refreshItem);

        menu.ShowAt(sender as FrameworkElement, e.GetPosition(sender as FrameworkElement));
      }
    }

    private void AudioComboBox_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
      {
        e.Handled = true;
        if (_contextMenuService != null)
        {
          var menu = new MenuFlyout();

          var loadItem = new MenuFlyoutItem { Text = "Load Spectrogram" };
          loadItem.Click += async (_, _) =>
          {
            if (ViewModel.LoadSpectrogramCommand.CanExecute(null))
              await ViewModel.LoadSpectrogramCommand.ExecuteAsync(null);
          };
          menu.Items.Add(loadItem);

          menu.ShowAt(comboBox, e.GetPosition(comboBox));
        }
      }
    }

    private void SpectrogramView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }
  }
}