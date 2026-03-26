using System;
using System.Collections.ObjectModel;
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
  public partial class HelpViewModel : BaseViewModel, ILifecyclePanelView
  {
    private readonly IHelpClient _helpClient;
    private readonly CancellationTokenSource _disposalCts = new();
    private CancellationTokenSource? _loadCts;
    private readonly IDispatcherTimer? _searchDebounceTimer;
    private const int SearchDebounceMs = 300;

    public string PanelId => PanelIds.Help;
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

    public HelpViewModel(IViewModelContext context, IHelpClient helpClient)
        : base(context)
    {
      _helpClient = helpClient ?? throw new ArgumentNullException(nameof(helpClient));

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
        await RefreshAsyncInternal(ct);
      });

      _searchDebounceTimer = Dispatcher.CreateTimer();
      if (_searchDebounceTimer != null)
      {
        _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(SearchDebounceMs);
        _searchDebounceTimer.IsRepeating = false;
        _searchDebounceTimer.Tick += OnSearchDebounceTick;
      }
    }

    /// <inheritdoc />
    public async Task OnActivatedAsync(CancellationToken cancellationToken = default)
    {
      _loadCts?.Cancel();
      _loadCts?.Dispose();
      using var linked = CancellationTokenSource.CreateLinkedTokenSource(_disposalCts.Token, cancellationToken);
      await LoadCategoriesAsync(linked.Token);
      await LoadTopicsAsync(linked.Token);
      await LoadShortcutsAsync(linked.Token);
    }

    /// <inheritdoc />
    public Task OnDeactivatedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken = default) => RefreshAsyncInternal(cancellationToken);

    private void OnSearchDebounceTick(object? sender, object e)
    {
      if (_disposalCts.IsCancellationRequested) return;
      _loadCts?.Cancel();
      _loadCts?.Dispose();
      _loadCts = CancellationTokenSource.CreateLinkedTokenSource(_disposalCts.Token);
      _ = SearchHelpAsync(_loadCts.Token);
    }

    public IAsyncRelayCommand LoadTopicsCommand { get; }
    public IAsyncRelayCommand SearchHelpCommand { get; }
    public IAsyncRelayCommand LoadShortcutsCommand { get; }
    public IAsyncRelayCommand LoadCategoriesCommand { get; }
    public IAsyncRelayCommand LoadPanelHelpCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    private async Task LoadTopicsAsync(CancellationToken cancellationToken)
    {
      var categorySnapshot = SelectedCategory;
      var panelSnapshot = SelectedPanelId;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var topics = await _helpClient.GetTopicsAsync(categorySnapshot, panelSnapshot, cancellationToken);

        if (SelectedCategory != categorySnapshot || SelectedPanelId != panelSnapshot)
          return;

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

      var querySnapshot = SearchQuery;
      var categorySnapshot = SelectedCategory;
      var panelSnapshot = SelectedPanelId;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var response = await _helpClient.SearchAsync(querySnapshot, categorySnapshot, panelSnapshot, cancellationToken);

        if (SearchQuery != querySnapshot || SelectedCategory != categorySnapshot || SelectedPanelId != panelSnapshot)
          return;

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
        var shortcuts = await _helpClient.GetShortcutsAsync(SelectedPanelId, cancellationToken);

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
        var response = await _helpClient.GetCategoriesAsync(cancellationToken);

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
      var panelSnapshot = SelectedPanelId;
      if (string.IsNullOrEmpty(panelSnapshot))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var response = await _helpClient.GetPanelHelpAsync(panelSnapshot, cancellationToken);

        if (SelectedPanelId != panelSnapshot)
          return;

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

    private async Task RefreshAsyncInternal(CancellationToken cancellationToken)
    {
      await LoadTopicsAsync(cancellationToken);
      await LoadShortcutsAsync(cancellationToken);
      StatusMessage = ResourceHelper.GetString("Help.Refreshed", "Help refreshed");
    }

    partial void OnSelectedCategoryChanged(string? value)
    {
      _loadCts?.Cancel();
      _loadCts?.Dispose();
      _loadCts = CancellationTokenSource.CreateLinkedTokenSource(_disposalCts.Token);
      _ = LoadTopicsAsync(_loadCts.Token);
    }

    partial void OnSelectedPanelIdChanged(string? value)
    {
      _loadCts?.Cancel();
      _loadCts?.Dispose();
      _loadCts = CancellationTokenSource.CreateLinkedTokenSource(_disposalCts.Token);
      _ = LoadPanelHelpAsync(_loadCts.Token);
    }

    partial void OnSearchQueryChanged(string? value)
    {
      _searchDebounceTimer?.Stop();
      _searchDebounceTimer?.Start();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
      if (disposing)
      {
        _searchDebounceTimer?.Stop();
        if (_searchDebounceTimer != null)
          _searchDebounceTimer.Tick -= OnSearchDebounceTick;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
        _disposalCts.Cancel();
        _disposalCts.Dispose();
      }
      base.Dispose(disposing);
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