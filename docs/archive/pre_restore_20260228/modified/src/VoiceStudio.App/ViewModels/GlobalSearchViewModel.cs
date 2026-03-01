using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for Global Search (IDEA 5).
  /// </summary>
  public sealed partial class GlobalSearchViewModel : BaseViewModel
  {
    private readonly IBackendClient _backendClient;
    private readonly SemaphoreSlim _searchSemaphore = new(1, 1);
    private int _activeSearchCount;

    [ObservableProperty]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private SearchResultItem? selectedResult;

    public ObservableCollection<SearchResultItem> Results { get; } = new();
    public ObservableCollection<SearchResultItem> FilteredResults { get; } = new();

    [ObservableProperty]
    private int totalResults;

    [ObservableProperty]
    private Dictionary<string, int> resultsByType = new();

    public bool CanSearch => !IsLoading && !string.IsNullOrWhiteSpace(SearchQuery) && SearchQuery.Length >= 2;

    // XAML compiler stability: bind Visibility to Visibility-typed properties (avoid converters in XAML).
    public Visibility ResultsSummaryVisibility => TotalResults > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ResultsListVisibility => IsLoading ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ErrorVisibility => string.IsNullOrEmpty(ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;

    public GlobalSearchViewModel(IBackendClient backendClient)
        : base(AppServices.GetViewModelContext())
    {
      _backendClient = backendClient;
    }

    [RelayCommand]
    public async Task SearchAsync()
    {
      if (string.IsNullOrWhiteSpace(SearchQuery) || SearchQuery.Length < 2)
      {
        Results.Clear();
        FilteredResults.Clear();
        TotalResults = 0;
        ResultsByType.Clear();
        return;
      }

      try
      {
        Interlocked.Increment(ref _activeSearchCount);
        IsLoading = true;
        ErrorMessage = null;

        await _searchSemaphore.WaitAsync();
        try
        {
          var response = await _backendClient.SearchAsync(SearchQuery, null, 50);

          Results.Clear();
          FilteredResults.Clear();

          foreach (var result in response.Results)
          {
            Results.Add(result);
            FilteredResults.Add(result);
          }

          TotalResults = response.TotalResults;
          ResultsByType = response.ResultsByType;

          // Select first result if available
          if (FilteredResults.Count > 0)
          {
            SelectedResult = FilteredResults[0];
          }
        }
        finally
        {
          _searchSemaphore.Release();
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Search failed: {ex.Message}";
        await HandleErrorAsync(ex, "Search", showDialog: false);
        Results.Clear();
        FilteredResults.Clear();
        TotalResults = 0;
        ResultsByType.Clear();
      }
      finally
      {
        if (Interlocked.Decrement(ref _activeSearchCount) <= 0)
        {
          _activeSearchCount = 0;
          IsLoading = false;
        }
      }
    }

    partial void OnSearchQueryChanged(string value)
    {
      OnPropertyChanged(nameof(CanSearch));
      _ = SearchAsync();
    }

    partial void OnIsLoadingChanged(bool value)
    {
      OnPropertyChanged(nameof(CanSearch));
      OnPropertyChanged(nameof(ResultsListVisibility));
    }

    partial void OnTotalResultsChanged(int value)
    {
      OnPropertyChanged(nameof(ResultsSummaryVisibility));
    }

    partial void OnErrorMessageChanged(string? value)
    {
      OnPropertyChanged(nameof(ErrorVisibility));
    }
  }
}