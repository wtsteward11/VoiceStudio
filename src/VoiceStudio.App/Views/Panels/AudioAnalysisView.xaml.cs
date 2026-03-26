using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using System;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// AudioAnalysisView panel for advanced audio analysis.
  /// </summary>
  public sealed partial class AudioAnalysisView : UserControl
  {
    public AudioAnalysisViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;

    public AudioAnalysisView()
    {
      this.InitializeComponent();
      ViewModel = new AudioAnalysisViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          VoiceStudio.App.Services.ServiceProvider.GetAudioAnalysisClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(AudioAnalysisViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Audio Analysis Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(AudioAnalysisViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Audio Analysis", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += AudioAnalysisView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void AudioAnalysisView_KeyboardNavigation_Loaded(object sender, RoutedEventArgs e)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Audio Analysis Help";
      HelpOverlay.HelpText = "The Audio Analysis panel provides comprehensive audio analysis tools including waveform visualization, frequency analysis, loudness metering, and quality metrics. Analyze audio characteristics, detect issues, and evaluate quality using various analysis methods.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh analysis" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+E", Description = "Export analysis" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Escape", Description = "Close help" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Load audio files to analyze their characteristics");
      HelpOverlay.Tips.Add("Frequency analysis reveals spectral content and potential issues");
      HelpOverlay.Tips.Add("Loudness metering helps ensure consistent audio levels");
      HelpOverlay.Tips.Add("Quality metrics provide objective measurements of audio quality");
      HelpOverlay.Tips.Add("Export analysis reports for documentation or sharing");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void AnalysisResult_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (ViewModel.AnalysisResult == null)
        return;

      e.Handled = true;
      if (_contextMenuService != null)
      {
        var menu = new MenuFlyout();

        var exportItem = new MenuFlyoutItem { Text = "Export Analysis" };
        exportItem.Click += (_, _) =>
        {
          try
          {
            // Export analysis result
            _toastService?.ShowToast(ToastType.Info, "Audio Analysis", "Exporting analysis results...");
            // Note: Export functionality will be implemented when backend export endpoint is available
          }
          catch (Exception ex)
          {
            _toastService?.ShowToast(ToastType.Error, "Export Error", ex.Message);
          }
        };
        menu.Items.Add(exportItem);

        var copyItem = new MenuFlyoutItem { Text = "Copy Audio ID" };
        copyItem.Click += (_, _) =>
        {
          var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
          dataPackage.SetText(ViewModel.AnalysisResult.AudioId);
          Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
          _toastService?.ShowToast(ToastType.Success, "Audio Analysis", "Audio ID copied to clipboard");
        };
        menu.Items.Add(copyItem);

        var refreshItem = new MenuFlyoutItem { Text = "Refresh Analysis" };
        refreshItem.Click += async (_, _) => await ViewModel.RefreshCommand.ExecuteAsync(null);
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

          var analyzeItem = new MenuFlyoutItem { Text = "Analyze Selected" };
          analyzeItem.Click += async (_, _) => await ViewModel.AnalyzeAudioCommand.ExecuteAsync(null);
          menu.Items.Add(analyzeItem);

          var loadItem = new MenuFlyoutItem { Text = "Load Analysis" };
          loadItem.Click += async (_, _) => await ViewModel.LoadAnalysisCommand.ExecuteAsync(null);
          menu.Items.Add(loadItem);

          menu.ShowAt(comboBox, e.GetPosition(comboBox));
        }
      }
    }
  }
}