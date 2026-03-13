using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// ImageSearchView panel for image search functionality.
  /// </summary>
  public sealed partial class ImageSearchView : UserControl
  {
    public ImageSearchViewModel ViewModel { get; }
    private ToastNotificationService? _toastService;

    public ImageSearchView()
    {
      this.InitializeComponent();
      ViewModel = new ImageSearchViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetImageSearchClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _toastService = AppServices.TryGetToastNotificationService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(ImageSearchViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Image Search Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(ImageSearchViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Image Search", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += ImageSearchView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void ImageSearchView_KeyboardNavigation_Loaded(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Image Search Help";
      HelpOverlay.HelpText = "The Image Search panel allows you to search for images from various sources including Unsplash, Pexels, Pixabay, and your local library. Enter keywords to search for images, then use filters to refine your search by source, category, orientation, and color. Browse through search results and view image details including dimensions, file size, license, and author information. Some sources require API keys which can be managed in the API Key Manager panel.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Enter", Description = "Search images" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+F", Description = "Focus search box" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "→", Description = "Next page" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "←", Description = "Previous page" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh sources" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Use specific keywords for better search results");
      HelpOverlay.Tips.Add("Filter by category to narrow down results");
      HelpOverlay.Tips.Add("Select orientation (landscape/portrait/square) to match your needs");
      HelpOverlay.Tips.Add("Use color filters to find images with specific color schemes");
      HelpOverlay.Tips.Add("Some sources require API keys - configure them in API Key Manager");
      HelpOverlay.Tips.Add("Check image licenses before using in commercial projects");
      HelpOverlay.Tips.Add("Use pagination to browse through large result sets");
      HelpOverlay.Tips.Add("Search history is saved for quick access to recent searches");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
      if (e.Key == Windows.System.VirtualKey.Enter)
      {
        if (ViewModel.SearchCommand.CanExecute(null))
        {
          ViewModel.SearchCommand.ExecuteAsync(null);
        }
        e.Handled = true;
      }
    }
  }
}