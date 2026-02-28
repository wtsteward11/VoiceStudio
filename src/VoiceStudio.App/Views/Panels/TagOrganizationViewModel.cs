using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// ViewModel for Tag-Based Organization UI.
  /// Implements IDEA 32: Tag-Based Organization UI.
  /// </summary>
  public partial class TagOrganizationViewModel : ObservableObject
  {
    private readonly IBackendClient _backendClient;

    [ObservableProperty]
    private string viewMode = "Cloud"; // Cloud, Hierarchy, List

    [ObservableProperty]
    private string? searchQuery;

    [ObservableProperty]
    private ObservableCollection<TagCloudItem> tagCloudItems = new();

    [ObservableProperty]
    private ObservableCollection<TagHierarchyItem> tagHierarchy = new();

    [ObservableProperty]
    private ObservableCollection<TagListItem> tagListItems = new();

    public bool IsCloudView => ViewMode == "Cloud";
    public bool IsHierarchyView => ViewMode == "Hierarchy";
    public bool IsListView => ViewMode == "List";

    public TagOrganizationViewModel(IBackendClient backendClient)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
      RefreshCommand = new AsyncRelayCommand(RefreshAsync);
    }

    public IAsyncRelayCommand RefreshCommand { get; }

    [ObservableProperty]
    private string? activeFilterTag;

    public void FilterByTag(string tagName)
    {
      ActiveFilterTag = tagName;
      SearchQuery = tagName;
      _ = RefreshAsync();
    }

    public async Task UpdateTag(string tagId, string newName)
    {
      try
      {
        // Update tag name via backend API
        var request = new { tag_id = tagId, new_name = newName };
        await _backendClient.SendRequestAsync<object, object>("/api/tags/update", request);
        await RefreshAsync();
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Failed to update tag: {ex.Message}");
      }
    }

    partial void OnViewModeChanged(string value)
    {
      OnPropertyChanged(nameof(IsCloudView));
      OnPropertyChanged(nameof(IsHierarchyView));
      OnPropertyChanged(nameof(IsListView));
      _ = RefreshAsync();
    }

    partial void OnSearchQueryChanged(string? value)
    {
      _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
      try
      {
        // Load all profiles to extract tags
        var profiles = await _backendClient.GetProfilesAsync();

        // Extract and count tags
        var tagCounts = new Dictionary<string, int>();
        var tagCategories = new Dictionary<string, string>();
        var tagColors = new Dictionary<string, string>();

        foreach (var profile in profiles)
        {
          if (profile.Tags != null)
          {
            foreach (var tag in profile.Tags)
            {
              if (!string.IsNullOrWhiteSpace(tag))
              {
                tagCounts[tag] = tagCounts.GetValueOrDefault(tag, 0) + 1;

                // Assign default category and color if not set
                if (!tagCategories.ContainsKey(tag))
                {
                  tagCategories[tag] = "General";
                  tagColors[tag] = GenerateTagColor(tag);
                }
              }
            }
          }
        }

        // Build view-specific collections
        if (ViewMode == "Cloud")
        {
          BuildTagCloud(tagCounts, tagColors);
        }
        else if (ViewMode == "Hierarchy")
        {
          BuildTagHierarchy(tagCounts, tagCategories, tagColors);
        }
        else // List
        {
          BuildTagList(tagCounts, tagCategories, tagColors);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Error refreshing tags: {ex.Message}");
      }
    }

    private void BuildTagCloud(Dictionary<string, int> tagCounts, Dictionary<string, string> tagColors)
    {
      TagCloudItems.Clear();

      if (tagCounts.Count == 0)
        return;

      var maxCount = tagCounts.Values.Max();
      var minCount = tagCounts.Values.Min();
      var range = maxCount - minCount;

      foreach (var kvp in tagCounts.OrderByDescending(t => t.Value))
      {
        if (!string.IsNullOrWhiteSpace(SearchQuery) &&
            !kvp.Key.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
          continue;

        // Calculate font size based on count (12-24px)
        var normalizedCount = range > 0 ? (kvp.Value - minCount) / (double)range : 0.5;
        var fontSize = 12 + (normalizedCount * 12);

        TagCloudItems.Add(new TagCloudItem
        {
          Name = kvp.Key,
          Count = kvp.Value,
          FontSize = fontSize,
          Color = tagColors.GetValueOrDefault(kvp.Key, "#FF00FF00")
        });
      }
    }

    private void BuildTagHierarchy(Dictionary<string, int> tagCounts, Dictionary<string, string> tagCategories, Dictionary<string, string> tagColors)
    {
      TagHierarchy.Clear();

      // Group by category
      var categoryGroups = tagCounts
          .Where(kvp => string.IsNullOrWhiteSpace(SearchQuery) ||
                       kvp.Key.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
          .GroupBy(kvp => tagCategories.GetValueOrDefault(kvp.Key, "General"))
          .OrderBy(g => g.Key);

      foreach (var categoryGroup in categoryGroups)
      {
        var categoryItem = new TagHierarchyItem
        {
          Name = categoryGroup.Key,
          Count = categoryGroup.Sum(t => t.Value),
          Color = "#FF00FFFF",
          Children = new ObservableCollection<TagHierarchyItem>()
        };

        foreach (var tag in categoryGroup.OrderByDescending(t => t.Value))
        {
          categoryItem.Children.Add(new TagHierarchyItem
          {
            Name = tag.Key,
            Count = tag.Value,
            Color = tagColors.GetValueOrDefault(tag.Key, "#FF00FF00")
          });
        }

        TagHierarchy.Add(categoryItem);
      }
    }

    private void BuildTagList(Dictionary<string, int> tagCounts, Dictionary<string, string> tagCategories, Dictionary<string, string> tagColors)
    {
      TagListItems.Clear();

      var filteredTags = tagCounts
          .Where(kvp => string.IsNullOrWhiteSpace(SearchQuery) ||
                       kvp.Key.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
          .OrderByDescending(t => t.Value);

      foreach (var tag in filteredTags)
      {
        TagListItems.Add(new TagListItem
        {
          Name = tag.Key,
          Count = tag.Value,
          Category = tagCategories.GetValueOrDefault(tag.Key, "General"),
          Color = tagColors.GetValueOrDefault(tag.Key, "#FF00FF00")
        });
      }
    }

    private string GenerateTagColor(string tag)
    {
      // Generate consistent color based on tag name
      var hash = tag.GetHashCode();
      var colors = new[]
      {
                "#FF00FF00", // Green
                "#FF00FFFF", // Cyan
                "#FFFF00FF", // Magenta
                "#FFFFFF00", // Yellow
                "#FFFF8000", // Orange
                "#FF0080FF", // Blue
                "#FFFF0080", // Pink
                "#FF80FF00"  // Lime
            };
      return colors[Math.Abs(hash) % colors.Length];
    }
  }

  public class TagCloudItem
  {
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
    public double FontSize { get; set; }
    public string Color { get; set; } = "#FF00FF00";
  }

  public class TagHierarchyItem
  {
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
    public string Color { get; set; } = "#FF00FF00";
    public ObservableCollection<TagHierarchyItem> Children { get; set; } = new();
  }

  public class TagListItem
  {
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Color { get; set; } = "#FF00FF00";
  }
}