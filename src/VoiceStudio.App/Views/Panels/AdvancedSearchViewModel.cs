using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;
using Windows.Storage.Pickers;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// ViewModel for Advanced Search with Natural Language.
  /// Implements IDEA 36: Advanced Search with Natural Language.
  /// </summary>
  public partial class AdvancedSearchViewModel : ObservableObject
  {
    private readonly IBackendClient _backendClient;

    [ObservableProperty]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> querySuggestions = new();

    [ObservableProperty]
    private ObservableCollection<string> queryHistory = new();

    [ObservableProperty]
    private ObservableCollection<SearchFilter> activeFilters = new();

    [ObservableProperty]
    private ObservableCollection<SearchResult> searchResults = new();

    [ObservableProperty]
    private string sortBy = "Relevance";

    public bool HasActiveFilters => ActiveFilters.Count > 0;
    public bool HasResults => SearchResults.Count > 0;
    public int ResultsCount => SearchResults.Count;

    public AdvancedSearchViewModel(IBackendClient backendClient)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
      ExportResultsCommand = new RelayCommand(ExportResults, () => HasResults);
      ClearSearchCommand = new RelayCommand(ClearSearch, () => !string.IsNullOrWhiteSpace(SearchQuery) || HasResults);

      LoadQueryHistory();
    }

    public IRelayCommand ExportResultsCommand { get; }
    public IRelayCommand ClearSearchCommand { get; }

    partial void OnSearchQueryChanged(string value)
    {
      UpdateSuggestions();
      ClearSearchCommand.NotifyCanExecuteChanged();
    }

    partial void OnSearchResultsChanged(ObservableCollection<SearchResult> value)
    {
      OnPropertyChanged(nameof(HasResults));
      OnPropertyChanged(nameof(ResultsCount));
      ExportResultsCommand.NotifyCanExecuteChanged();
    }

    private void LoadQueryHistory()
    {
      QueryHistory.Clear();
      try
      {
        // Use file-based storage for unpackaged app compatibility
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var voiceStudioDir = Path.Combine(appDataPath, "VoiceStudio");
        var historyFilePath = Path.Combine(voiceStudioDir, "search_history.json");

        if (File.Exists(historyFilePath))
        {
          var content = File.ReadAllText(historyFilePath);
          var history = System.Text.Json.JsonSerializer.Deserialize<List<string>>(content);
          if (history != null)
          {
            foreach (var item in history)
            {
              QueryHistory.Add(item);
            }
          }
        }
        else
        {
          // Initialize with default examples
          QueryHistory.Add("high quality profiles from last week");
          QueryHistory.Add("sad emotion presets");
          QueryHistory.Add("profiles created today");
        }
      }
      catch
      {
        // Fallback to default examples if storage fails
        QueryHistory.Add("high quality profiles from last week");
        QueryHistory.Add("sad emotion presets");
        QueryHistory.Add("profiles created today");
      }
    }

    private async Task SaveQueryHistoryAsync()
    {
      try
      {
        // Use file-based storage for unpackaged app compatibility
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var voiceStudioDir = Path.Combine(appDataPath, "VoiceStudio");
        Directory.CreateDirectory(voiceStudioDir);
        var historyFilePath = Path.Combine(voiceStudioDir, "search_history.json");

        var content = System.Text.Json.JsonSerializer.Serialize(QueryHistory.ToList());
        await File.WriteAllTextAsync(historyFilePath, content);
      }
      catch
      {
        // Silently fail - history will be lost but app continues
        System.Diagnostics.Debug.WriteLine("Failed to save search history");
      }
    }

    private void UpdateSuggestions()
    {
      QuerySuggestions.Clear();
      if (string.IsNullOrWhiteSpace(SearchQuery))
        return;

      var query = SearchQuery.ToLower();
      var suggestions = new List<string>();

      // Time-based suggestions
      if (query.Contains("last") || query.Contains("recent"))
      {
        suggestions.Add("high quality profiles from last week");
        suggestions.Add("recent audio clips");
      }

      // Quality-based suggestions
      if (query.Contains("quality") || query.Contains("high") || query.Contains("low"))
      {
        suggestions.Add("high quality profiles");
        suggestions.Add("low quality audio clips");
      }

      // Type-based suggestions
      if (query.Contains("profile") || query.Contains("voice"))
      {
        suggestions.Add("profiles created today");
        suggestions.Add("voice profiles with high MOS");
      }

      // Emotion-based suggestions
      if (query.Contains("emotion") || query.Contains("sad") || query.Contains("happy"))
      {
        suggestions.Add("sad emotion presets");
        suggestions.Add("happy emotion styles");
      }

      // Add from history
      foreach (var history in QueryHistory)
      {
        if (history.ToLower().Contains(query) && !suggestions.Contains(history))
        {
          suggestions.Add(history);
        }
      }

      foreach (var suggestion in suggestions.Take(5))
      {
        QuerySuggestions.Add(suggestion);
      }
    }

    public async Task PerformSearchAsync(string query)
    {
      if (string.IsNullOrWhiteSpace(query))
      {
        SearchResults.Clear();
        ActiveFilters.Clear();
        return;
      }

      try
      {
        // Call backend search API with natural language support
        var searchResponse = await _backendClient.SearchAsync(query, types: null, limit: 50);

        // Parse natural language query for UI filters
        var parsedQuery = ParseNaturalLanguageQuery(query);

        // Extract filters from parsed query
        ActiveFilters.Clear();
        foreach (var filter in parsedQuery.Filters)
        {
          ActiveFilters.Add(filter);
        }

        // Map backend SearchResultItem to frontend SearchResult
        SearchResults.Clear();
        foreach (var item in searchResponse.Results)
        {
          // Extract date from metadata if available
          DateTime resultDate = DateTime.Now;
          if (item.Metadata.TryGetValue("created_at", out var createdAtObj))
          {
            if (createdAtObj is DateTime dt)
              resultDate = dt;
            else if (createdAtObj is string dateStr && DateTime.TryParse(dateStr, out var parsedDate))
              resultDate = parsedDate;
          }

          // Extract quality from metadata if available
          double? quality = null;
          if (item.Metadata.TryGetValue("quality_score", out var qualityObj))
          {
            if (qualityObj is double d)
              quality = d;
            else if (qualityObj is float f)
              quality = f;
            else if (qualityObj is int i)
              quality = i;
            else if (qualityObj is string qStr && double.TryParse(qStr, out var parsedQuality))
              quality = parsedQuality;
          }

          // Determine icon based on type
          string icon = item.Type.ToLower() switch
          {
            "profile" => "🎙️",
            "audio" => "🎵",
            "project" => "📁",
            "marker" => "📍",
            "script" => "📝",
            _ => "📄"
          };

          SearchResults.Add(new SearchResult
          {
            Id = item.Id,
            Title = item.Title,
            Description = item.Description ?? item.Preview ?? string.Empty,
            Type = item.Type,
            Date = resultDate,
            Quality = quality,
            Icon = icon
          });
        }

        // Save to history
        if (!QueryHistory.Contains(query))
        {
          QueryHistory.Insert(0, query);
          if (QueryHistory.Count > 20)
          {
            QueryHistory.RemoveAt(QueryHistory.Count - 1);
          }
          await SaveQueryHistoryAsync();
        }

        OnPropertyChanged(nameof(HasActiveFilters));
      }
      catch (Exception ex)
      {
        // Log error and show empty results
        System.Diagnostics.Debug.WriteLine($"Search failed: {ex.Message}");
        SearchResults.Clear();
        ActiveFilters.Clear();
      }
    }

    private ParsedQuery ParseNaturalLanguageQuery(string query)
    {
      var parsed = new ParsedQuery { OriginalQuery = query };
      var lowerQuery = query.ToLower();

      // Extract time filters
      if (lowerQuery.Contains("last week") || lowerQuery.Contains("from last week"))
      {
        parsed.Filters.Add(new SearchFilter { Name = "Date", Value = "Last Week" });
      }
      else if (lowerQuery.Contains("today") || lowerQuery.Contains("created today"))
      {
        parsed.Filters.Add(new SearchFilter { Name = "Date", Value = "Today" });
      }
      else if (lowerQuery.Contains("recent"))
      {
        parsed.Filters.Add(new SearchFilter { Name = "Date", Value = "Recent" });
      }

      // Extract quality filters
      if (lowerQuery.Contains("high quality") || lowerQuery.Contains("good quality"))
      {
        parsed.Filters.Add(new SearchFilter { Name = "Quality", Value = "High" });
      }
      else if (lowerQuery.Contains("low quality") || lowerQuery.Contains("poor quality"))
      {
        parsed.Filters.Add(new SearchFilter { Name = "Quality", Value = "Low" });
      }

      // Extract type filters
      if (lowerQuery.Contains("profile") || lowerQuery.Contains("profiles"))
      {
        parsed.Filters.Add(new SearchFilter { Name = "Type", Value = "Profile" });
      }
      else if (lowerQuery.Contains("audio") || lowerQuery.Contains("clip"))
      {
        parsed.Filters.Add(new SearchFilter { Name = "Type", Value = "Audio Clip" });
      }
      else if (lowerQuery.Contains("preset"))
      {
        parsed.Filters.Add(new SearchFilter { Name = "Type", Value = "Preset" });
      }

      // Extract emotion/style filters
      if (lowerQuery.Contains("sad"))
      {
        parsed.Filters.Add(new SearchFilter { Name = "Emotion", Value = "Sad" });
      }
      else if (lowerQuery.Contains("happy"))
      {
        parsed.Filters.Add(new SearchFilter { Name = "Emotion", Value = "Happy" });
      }

      return parsed;
    }

    private async void ExportResults()
    {
      if (SearchResults.Count == 0)
        return;

      try
      {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("CSV", new List<string> { ".csv" });
        picker.FileTypeChoices.Add("JSON", new List<string> { ".json" });
        picker.SuggestedFileName = $"search_results_{DateTime.Now:yyyyMMdd_HHmmss}";

        // Initialize picker with window handle for unpackaged app
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
          var extension = file.FileType.ToLower();
          var filePath = file.Path;
          if (extension == ".csv")
          {
            var csv = new StringBuilder();
            csv.AppendLine("Id,Title,Description,Type,Date,Quality");
            foreach (var result in SearchResults)
            {
              csv.AppendLine($"{result.Id},\"{result.Title}\",\"{result.Description}\",{result.Type},{result.Date:yyyy-MM-dd HH:mm:ss},{result.Quality?.ToString() ?? "N/A"}");
            }
            await File.WriteAllTextAsync(filePath, csv.ToString());
          }
          else if (extension == ".json")
          {
            var json = System.Text.Json.JsonSerializer.Serialize(SearchResults, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json);
          }
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Failed to export results: {ex.Message}");
      }
    }

    private void ClearSearch()
    {
      SearchQuery = string.Empty;
      SearchResults.Clear();
      ActiveFilters.Clear();
      ClearSearchCommand.NotifyCanExecuteChanged();
    }
  }

  public class SearchResult
  {
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public double? Quality { get; set; }
    public string Icon { get; set; } = "📄";
  }

  public class SearchFilter
  {
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
  }

  public class ParsedQuery
  {
    public string OriginalQuery { get; set; } = string.Empty;
    public List<SearchFilter> Filters { get; set; } = new();
  }
}