using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.Controls;
using VoiceStudio.App.Services;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// Advanced Search with Natural Language view.
  /// Implements IDEA 36: Advanced Search with Natural Language.
  /// </summary>
  public sealed partial class AdvancedSearchView : UserControl
  {
    public AdvancedSearchViewModel ViewModel { get; }

    public AdvancedSearchView()
    {
      this.InitializeComponent();
      ViewModel = new AdvancedSearchViewModel(
          ServiceProvider.GetBackendClient()
      );
      this.DataContext = ViewModel;

      // Setup keyboard navigation
      this.Loaded += AdvancedSearchView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void AdvancedSearchView_KeyboardNavigation_Loaded(object sender, RoutedEventArgs e)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private async void SearchBox_QuerySubmitted(AutoSuggestBox _, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
      try
      {
        if (!string.IsNullOrWhiteSpace(args.QueryText))
        {
          await ViewModel.PerformSearchAsync(args.QueryText);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
      if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
      {
        sender.ItemsSource = ViewModel.QuerySuggestions;
      }
    }

    private void SearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
      sender.Text = args.SelectedItem?.ToString() ?? string.Empty;
    }

    private async void QueryHistory_ItemClick(object _, ItemClickEventArgs e)
    {
      try
      {
        if (e.ClickedItem is string query)
        {
          ViewModel.SearchQuery = query;
          await ViewModel.PerformSearchAsync(query);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private void RemoveFilter_Click(object sender, RoutedEventArgs _)
    {
      if (sender is Button button && button.CommandParameter is SearchFilter filter)
      {
        ViewModel.ActiveFilters.Remove(filter);
      }
    }

    private void ResultItem_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs _)
    {
      if (sender is FrameworkElement element && element.Tag is SearchResult result)
      {
        NavigateToResult(result);
      }
    }

    private void OpenResult_Click(object sender, RoutedEventArgs _)
    {
      if (sender is Button button && button.CommandParameter is SearchResult result)
      {
        NavigateToResult(result);
      }
    }

    private void NavigateToResult(SearchResult result)
    {
      // Navigate based on result type
      var mainWindow = Microsoft.UI.Xaml.Application.Current as App;
      if (mainWindow != null)
      {
        // Switch to appropriate panel based on result type
        var type = result.Type.ToLower();
        if (type == "profile")
        {
          // Navigate to Profiles panel and select the profile
          // This would be handled by MainWindow panel switching logic
        }
        else if (type == "audio")
        {
          // Navigate to Timeline or Analyzer panel
        }
        else if (type == "project")
        {
          // Navigate to project view
        }
      }
    }

    private void HelpButton_Click(object _, RoutedEventArgs __)
    {
      // Phase 5.6: Updated help overlay with comprehensive guidance.
      if (HelpOverlay != null)
      {
        HelpOverlay.Title = "Advanced Search Help";
        HelpOverlay.HelpText = @"Advanced Search allows you to find voice profiles, audio files, and projects using powerful search criteria.

Features:
• Full-text search across all metadata fields
• Filter by file type (voice profile, audio, project)
• Filter by date range, duration, and quality scores
• Tag-based filtering and categorization
• Save and reuse search queries

Tips:
• Use quotes for exact phrase matching
• Combine multiple filters for precise results
• Click on results to navigate to the item
• Use keyboard shortcuts for faster navigation";

        HelpOverlay.Shortcuts.Clear();
        HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+F", Description = "Focus search box" });
        HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Enter", Description = "Execute search" });
        HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Escape", Description = "Clear search" });
        HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh results" });

        HelpOverlay.Visibility = Visibility.Visible;
        HelpOverlay.Show();
      }
    }
  }
}