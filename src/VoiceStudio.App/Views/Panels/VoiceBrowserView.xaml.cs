using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using System.Runtime.InteropServices.WindowsRuntime;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// VoiceBrowserView panel for browsing and discovering voice profiles.
  /// </summary>
  public sealed partial class VoiceBrowserView : UserControl
  {
    public VoiceBrowserViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;

    public VoiceBrowserView()
    {
      this.InitializeComponent();
      ViewModel = new VoiceBrowserViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetVoiceBrowserClient(),
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IAudioPlayerService>()
      );
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(VoiceBrowserViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Voice Browser Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(VoiceBrowserViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Voice Browser", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += VoiceBrowserView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.Close();
        }
      });
    }

    private void VoiceBrowserView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      // Setup Tab navigation order for this panel
      KeyboardNavigationHelper.SetupTabNavigation(this, 0);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Voice Browser Help";
      HelpOverlay.HelpText = "The Voice Browser panel allows you to browse, search, and discover voice profiles. Filter and sort voices by quality, language, emotion, and other characteristics. Preview voices, compare multiple voices side-by-side, and select voices for synthesis. The browser provides a comprehensive interface for exploring the available voice library and finding the perfect voice for your project.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+F", Description = "Search voices" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Use filters to narrow down voice selection by criteria");
      HelpOverlay.Tips.Add("Preview voices to hear how they sound before using them");
      HelpOverlay.Tips.Add("Compare multiple voices side-by-side to find the best match");
      HelpOverlay.Tips.Add("Quality scores help identify the best voices for your project");
      HelpOverlay.Tips.Add("Sort voices by quality, popularity, or date to find what you need");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void Voice_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var voice = element.DataContext ?? listView.SelectedItem;
        if (voice != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var previewItem = new MenuFlyoutItem { Text = "Preview" };
            previewItem.Click += async (_, _) => await HandleVoiceMenuClick("Preview", voice);
            menu.Items.Add(previewItem);

            var playItem = new MenuFlyoutItem { Text = "Play" };
            playItem.Click += async (_, _) => await HandleVoiceMenuClick("Play", voice);
            menu.Items.Add(playItem);

            var useItem = new MenuFlyoutItem { Text = "Use for Synthesis" };
            useItem.Click += async (_, _) => await HandleVoiceMenuClick("Use", voice);
            menu.Items.Add(useItem);

            var compareItem = new MenuFlyoutItem { Text = "Compare" };
            compareItem.Click += async (_, _) => await HandleVoiceMenuClick("Compare", voice);
            menu.Items.Add(compareItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var exportItem = new MenuFlyoutItem { Text = "Export" };
            exportItem.Click += async (_, _) => await HandleVoiceMenuClick("Export", voice);
            menu.Items.Add(exportItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private async System.Threading.Tasks.Task HandleVoiceMenuClick(string action, object voice)
    {
      try
      {
        switch (action.ToLower())
        {
          case "preview":
            ViewModel.SelectedVoice = (VoiceProfileSummaryItem)voice;
            _toastService?.ShowToast(ToastType.Info, "Preview Voice", "Previewing voice");
            break;
          case "play":
            ViewModel.SelectedVoice = (VoiceProfileSummaryItem)voice;
            if (ViewModel.PlayCommand.CanExecute(null))
              await ViewModel.PlayCommand.ExecuteAsync(null);
            else
              _toastService?.ShowToast(ToastType.Warning, "Play", "No preview audio available for this voice");
            break;
          case "use":
            ViewModel.SelectedVoice = (VoiceProfileSummaryItem)voice;
            _toastService?.ShowToast(ToastType.Success, "Voice Selected", "Voice selected for synthesis");
            break;
          case "compare":
            await CompareVoicesAsync(voice);
            break;
          case "export":
            await ExportVoiceAsync(voice);
            break;
        }
      }
      catch (System.Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private System.Threading.Tasks.Task CompareVoicesAsync(object voice)
    {
      try
      {
        var voiceType = voice.GetType();
        var voiceName = voiceType.GetProperty("Name")?.GetValue(voice)?.ToString() ?? "unknown";
        _toastService?.ShowToast(ToastType.Info, "Compare", $"Comparing voice '{voiceName}' with selected voices");
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Compare Failed", ex.Message);
      }

      return System.Threading.Tasks.Task.CompletedTask;
    }

    private async System.Threading.Tasks.Task ExportVoiceAsync(object voice)
    {
      try
      {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("JSON", new[] { ".json" });
        picker.SuggestedFileName = "voice_export";

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
          var voiceType = voice.GetType();
          var jsonData = new
          {
            Name = voiceType.GetProperty("Name")?.GetValue(voice)?.ToString() ?? "unknown",
            Id = voiceType.GetProperty("Id")?.GetValue(voice)?.ToString() ?? "unknown"
          };
          var content = System.Text.Json.JsonSerializer.Serialize(jsonData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
          await Windows.Storage.FileIO.WriteTextAsync(file, content);
          _toastService?.ShowToast(ToastType.Success, "Export", "Voice exported successfully");
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Export Failed", ex.Message);
      }
    }
  }
}