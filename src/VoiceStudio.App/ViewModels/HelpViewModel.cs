using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the HelpView panel - Help system.
  /// </summary>
  public partial class HelpViewModel : BaseViewModel, IPanelView
  {
    private readonly IBackendClient _backendClient;
    private CancellationTokenSource? _searchDebounceCts;
    private const int SearchDebounceMs = 300;

    public string PanelId => "help";
    public string DisplayName => ResourceHelper.GetString("Panel.Help.DisplayName", "Help");
    public PanelRegion Region => PanelRegion.Right;

    [ObservableProperty]
    private ObservableCollection<HelpTopic> topics = new();

    [ObservableProperty]
    private HelpTopic? selectedTopic;

    [ObservableProperty]
    private ObservableCollection<HelpKeyboardShortcut> shortcuts = new();

    [ObservableProperty]
    private string? searchQuery;

    [ObservableProperty]
    private string? selectedCategory;

    [ObservableProperty]
    private string? selectedPanelId;

    [ObservableProperty]
    private ObservableCollection<string> availableCategories = new();

    [ObservableProperty]
    private ObservableCollection<string> availablePanels = new();

    [ObservableProperty]
    private bool showSearchResults;

    public HelpViewModel(IViewModelContext context, IBackendClient backendClient)
        : base(context)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));

      LoadTopicsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadTopics");
        await LoadTopicsAsync(ct);
      });
      SearchHelpCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("SearchHelp");
        await SearchHelpAsync(ct);
      });
      LoadShortcutsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadShortcuts");
        await LoadShortcutsAsync(ct);
      });
      LoadCategoriesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadCategories");
        await LoadCategoriesAsync(ct);
      });
      LoadPanelHelpCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadPanelHelp");
        await LoadPanelHelpAsync(ct);
      });
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      });

      // Load initial data
      _ = LoadCategoriesAsync(CancellationToken.None);
      _ = LoadTopicsAsync(CancellationToken.None);
      _ = LoadShortcutsAsync(CancellationToken.None);
    }

    public IAsyncRelayCommand LoadTopicsCommand { get; }
    public IAsyncRelayCommand SearchHelpCommand { get; }
    public IAsyncRelayCommand LoadShortcutsCommand { get; }
    public IAsyncRelayCommand LoadCategoriesCommand { get; }
    public IAsyncRelayCommand LoadPanelHelpCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    private async Task LoadTopicsAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var queryParams = new System.Collections.Specialized.NameValueCollection();
        if (!string.IsNullOrEmpty(SelectedCategory))
          queryParams.Add("category", SelectedCategory);
        if (!string.IsNullOrEmpty(SelectedPanelId))
          queryParams.Add("panel_id", SelectedPanelId);

        var queryString = string.Join("&",
            queryParams.AllKeys.SelectMany(key =>
                queryParams.GetValues(key)?.Select(value => $"{key}={Uri.EscapeDataString(value)}") ?? Array.Empty<string>()
            )
        );

        var url = "/api/help/topics";
        if (!string.IsNullOrEmpty(queryString))
          url += $"?{queryString}";

        var topics = await _backendClient.SendRequestAsync<object, HelpTopic[]>(
            url,
            null,
            System.Net.Http.HttpMethod.Get,
            cancellationToken
        );

        Topics.Clear();
        if (topics != null)
        {
          foreach (var topic in topics)
          {
            Topics.Add(topic);
          }
        }

        ShowSearchResults = false;
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("Help.LoadTopicsFailed", ex.Message);
        await HandleErrorAsync(ex, "LoadTopics");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task SearchHelpAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(SearchQuery))
      {
        await LoadTopicsAsync(cancellationToken);
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var queryParams = new System.Collections.Specialized.NameValueCollection();
        queryParams.Add("query", SearchQuery);
        if (!string.IsNullOrEmpty(SelectedCategory))
          queryParams.Add("category", SelectedCategory);
        if (!string.IsNullOrEmpty(SelectedPanelId))
          queryParams.Add("panel_id", SelectedPanelId);

        var queryString = string.Join("&",
            queryParams.AllKeys.SelectMany(key =>
                queryParams.GetValues(key)?.Select(value => $"{key}={Uri.EscapeDataString(value)}") ?? Array.Empty<string>()
            )
        );

        var url = $"/api/help/search?{queryString}";

        var response = await _backendClient.SendRequestAsync<object, HelpSearchResponse>(
            url,
            null,
            System.Net.Http.HttpMethod.Get,
            cancellationToken
        );

        Topics.Clear();
        if (response?.Topics != null)
        {
          foreach (var topic in response.Topics)
          {
            Topics.Add(topic);
          }
        }

        ShowSearchResults = true;
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("Help.SearchFailed", ex.Message);
        await HandleErrorAsync(ex, "SearchHelp");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadShortcutsAsync(CancellationToken cancellationToken)
    {
      try
      {
        var queryParams = new System.Collections.Specialized.NameValueCollection();
        if (!string.IsNullOrEmpty(SelectedPanelId))
          queryParams.Add("panel_id", SelectedPanelId);

        var queryString = string.Join("&",
            queryParams.AllKeys.SelectMany(key =>
                queryParams.GetValues(key)?.Select(value => $"{key}={Uri.EscapeDataString(value)}") ?? Array.Empty<string>()
            )
        );

        var url = "/api/help/shortcuts";
        if (!string.IsNullOrEmpty(queryString))
          url += $"?{queryString}";

        var shortcuts = await _backendClient.SendRequestAsync<object, HelpKeyboardShortcut[]>(
            url,
            null,
            System.Net.Http.HttpMethod.Get,
            cancellationToken
        );

        Shortcuts.Clear();
        if (shortcuts != null)
        {
          foreach (var shortcut in shortcuts)
          {
            Shortcuts.Add(shortcut);
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("Help.LoadShortcutsFailed", ex.Message);
        await HandleErrorAsync(ex, "LoadShortcuts");
      }
    }

    private async Task LoadCategoriesAsync(CancellationToken cancellationToken)
    {
      try
      {
        var response = await _backendClient.SendRequestAsync<object, HelpCategoriesResponse>(
            "/api/help/categories",
            null,
            System.Net.Http.HttpMethod.Get,
            cancellationToken
        );

        AvailableCategories.Clear();
        if (response?.Categories != null)
        {
          foreach (var category in response.Categories)
          {
            AvailableCategories.Add(category);
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("Help.LoadCategoriesFailed", ex.Message);
        await HandleErrorAsync(ex, "LoadCategories");
      }
    }

    private async Task LoadPanelHelpAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SelectedPanelId))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var response = await _backendClient.SendRequestAsync<object, PanelHelpResponse>(
            $"/api/help/panel/{SelectedPanelId}",
            null,
            System.Net.Http.HttpMethod.Get,
            cancellationToken
        );

        Topics.Clear();
        if (response?.Topics != null)
        {
          foreach (var topic in response.Topics)
          {
            Topics.Add(topic);
          }
        }

        Shortcuts.Clear();
        if (response?.Shortcuts != null)
        {
          foreach (var shortcut in response.Shortcuts)
          {
            Shortcuts.Add(shortcut);
          }
        }

        ShowSearchResults = false;
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("Help.LoadPanelHelpFailed", ex.Message);
        await HandleErrorAsync(ex, "LoadPanelHelp");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
      await LoadTopicsAsync(cancellationToken);
      await LoadShortcutsAsync(cancellationToken);
      StatusMessage = ResourceHelper.GetString("Help.Refreshed", "Help refreshed");
    }

    partial void OnSelectedCategoryChanged(string? value)
    {
      _ = LoadTopicsAsync(CancellationToken.None);
    }

    partial void OnSelectedPanelIdChanged(string? value)
    {
      _ = LoadPanelHelpAsync(CancellationToken.None);
    }

    partial void OnSearchQueryChanged(string? value)
    {
      _searchDebounceCts?.Cancel();
      _searchDebounceCts = new CancellationTokenSource();
      var cts = _searchDebounceCts;
      _ = Task.Run(async () =>
      {
        try
        {
          await Task.Delay(SearchDebounceMs, cts.Token);
          Dispatcher.TryEnqueue(() => _ = SearchHelpAsync(cts.Token));
        }
        catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "HelpViewModel.OnSearchQueryChanged");
      }
      });
    }

    // Response models
    private class HelpSearchResponse
    {
      public HelpTopic[] Topics { get; set; } = Array.Empty<HelpTopic>();
      public int Total { get; set; }
    }

    private class HelpCategoriesResponse
    {
      public string[] Categories { get; set; } = Array.Empty<string>();
    }

    private class PanelHelpResponse
    {
      public HelpTopic[] Topics { get; set; } = Array.Empty<HelpTopic>();
      public HelpKeyboardShortcut[] Shortcuts { get; set; } = Array.Empty<HelpKeyboardShortcut>();
      public string PanelId { get; set; } = string.Empty;
    }
  }

  // Data models
  public class HelpTopic
  {
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public System.Collections.Generic.List<string> Keywords { get; set; } = new();
    public System.Collections.Generic.List<string> RelatedTopics { get; set; } = new();
    public string? PanelId { get; set; }
  }

  public class HelpKeyboardShortcut
  {
    public string Key { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? PanelId { get; set; }
  }
}