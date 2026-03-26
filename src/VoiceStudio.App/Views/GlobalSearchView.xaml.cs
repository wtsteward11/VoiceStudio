using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using Windows.System;

namespace VoiceStudio.App.Views
{
  /// <summary>
  /// Global Search View (IDEA 5).
  /// </summary>
  public sealed partial class GlobalSearchView : UserControl
  {
    public GlobalSearchViewModel ViewModel { get; }

    public GlobalSearchView()
    {
      this.InitializeComponent();
      ViewModel = new GlobalSearchViewModel(
          ServiceProvider.GetSearchClient()
      );
      DataContext = ViewModel;

      // Update empty state visibility
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(GlobalSearchViewModel.TotalResults))
        {
          EmptyStatePanel.Visibility = ViewModel.TotalResults == 0 && !ViewModel.IsLoading && string.IsNullOrEmpty(ViewModel.ErrorMessage)
                    ? Visibility.Visible : Visibility.Collapsed;
        }
        else if (e.PropertyName == nameof(GlobalSearchViewModel.IsLoading) || e.PropertyName == nameof(GlobalSearchViewModel.ErrorMessage))
        {
          EmptyStatePanel.Visibility = ViewModel.TotalResults == 0 && !ViewModel.IsLoading && string.IsNullOrEmpty(ViewModel.ErrorMessage)
                    ? Visibility.Visible : Visibility.Collapsed;
        }
      };

      // Set up keyboard navigation
      this.Loaded += GlobalSearchView_Loaded;
    }

    private void GlobalSearchView_Loaded(object sender, RoutedEventArgs e)
    {
      // Focus search box when view is shown
      SearchBox?.Focus(FocusState.Programmatic);
    }

    private void SearchBox_KeyDown(object _, KeyRoutedEventArgs e)
    {
      if (e.Key == VirtualKey.Enter)
      {
        if (ViewModel.SelectedResult != null)
        {
          NavigateToResult(ViewModel.SelectedResult);
        }
        else if (ViewModel.FilteredResults.Count > 0)
        {
          ViewModel.SelectedResult = ViewModel.FilteredResults[0];
          NavigateToResult(ViewModel.FilteredResults[0]);
        }
      }
      else if (e.Key == VirtualKey.Escape)
      {
        Hide();
      }
      else if (e.Key == VirtualKey.Down)
      {
        if (ResultsList.SelectedIndex < ViewModel.FilteredResults.Count - 1)
        {
          ResultsList.SelectedIndex++;
        }
        e.Handled = true;
      }
      else if (e.Key == VirtualKey.Up)
      {
        if (ResultsList.SelectedIndex > 0)
        {
          ResultsList.SelectedIndex--;
        }
        e.Handled = true;
      }
    }

    private void ResultsList_ItemClick(object _, ItemClickEventArgs e)
    {
      if (e.ClickedItem is VoiceStudio.Core.Models.SearchResultItem result)
      {
        NavigateToResult(result);
      }
    }

    private void NavigateToResult(VoiceStudio.Core.Models.SearchResultItem result)
    {
      // Navigate to the panel and item
      // This will be handled by MainWindow when we integrate the search
      // For now, just hide the search view
      Hide();

      // Raise event for MainWindow to handle navigation
      NavigateRequested?.Invoke(this, new SearchNavigationEventArgs(result));
    }

    public event EventHandler<SearchNavigationEventArgs>? NavigateRequested;

    public void Show()
    {
      this.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      SearchBox.Text = string.Empty;
      SearchBox.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
    }

    public void Hide()
    {
      this.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
    }
  }

  public class SearchNavigationEventArgs : EventArgs
  {
    public VoiceStudio.Core.Models.SearchResultItem Result { get; }

    public SearchNavigationEventArgs(VoiceStudio.Core.Models.SearchResultItem result)
    {
      Result = result;
    }
  }
}