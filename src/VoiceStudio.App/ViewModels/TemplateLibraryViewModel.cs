using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the TemplateLibraryView panel - Template management.
  /// </summary>
  public partial class TemplateLibraryViewModel : BaseViewModel, IPanelView, IPanelLifecycle
  {
    private readonly ITemplateLibraryClient _templateLibraryClient;
    private readonly UndoRedoService? _undoRedoService;
    private readonly CancellationTokenSource _disposalCts = new();
    private CancellationTokenSource? _loadTemplatesCts;
    private readonly IDispatcherTimer? _searchDebounceTimer;
    private const int SearchDebounceMs = 300;

    public string PanelId => PanelIds.TemplateLibrary;
    public string DisplayName => ResourceHelper.GetString("Panel.TemplateLibrary.DisplayName", "Template Library");
    public PanelRegion Region => PanelRegion.Right;

    [ObservableProperty]
    private ObservableCollection<TemplateItem> templates = new();

    [ObservableProperty]
    private TemplateItem? selectedTemplate;

    [ObservableProperty]
    private string? searchQuery;

    [ObservableProperty]
    private string? selectedCategory;

    [ObservableProperty]
    private ObservableCollection<string> availableCategories = new();

    [ObservableProperty]
    private bool isCreating;

    [ObservableProperty]
    private string? creatingName;

    [ObservableProperty]
    private string? creatingCategory;

    [ObservableProperty]
    private string? creatingDescription;

    public TemplateLibraryViewModel(IViewModelContext context, ITemplateLibraryClient templateLibraryClient)
        : base(context)
    {
      _templateLibraryClient = templateLibraryClient ?? throw new ArgumentNullException(nameof(templateLibraryClient));

      // Get undo/redo service (may be null if not initialized)
      try
      {
        _undoRedoService = AppServices.TryGetUndoRedoService();
      }
      catch
      {
        // Service may not be initialized yet - that's okay
        _undoRedoService = null;
      }

      _searchDebounceTimer = Dispatcher.CreateTimer();
      if (_searchDebounceTimer != null)
      {
        _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(SearchDebounceMs);
        _searchDebounceTimer.IsRepeating = false;
        _searchDebounceTimer.Tick += OnSearchDebounceTick;
      }

      LoadTemplatesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadTemplates");
        await LoadTemplatesAsync(ct);
      }, () => !IsLoading);
      SearchTemplatesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("SearchTemplates");
        await SearchTemplatesAsync(ct);
      }, () => !IsLoading);
      CreateTemplateCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CreateTemplate");
        await CreateTemplateAsync(ct);
      }, () => !string.IsNullOrWhiteSpace(CreatingName) && !IsLoading);
      UpdateTemplateCommand = new EnhancedAsyncRelayCommand<TemplateItem>(async (template, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("UpdateTemplate");
        await UpdateTemplateAsync(template, ct);
      }, (template) => template != null && !IsLoading);
      DeleteTemplateCommand = new EnhancedAsyncRelayCommand<TemplateItem>(async (template, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteTemplate");
        await DeleteTemplateAsync(template, ct);
      }, (template) => template != null && !IsLoading);
      ApplyTemplateCommand = new EnhancedAsyncRelayCommand<TemplateItem>(async (template, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ApplyTemplate");
        await ApplyTemplateAsync(template, ct);
      }, (template) => template != null && !IsLoading);
      LoadCategoriesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadCategories");
        await LoadCategoriesAsync(ct);
      }, () => !IsLoading);
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsyncInternal(ct);
      }, () => !IsLoading);
    }

    /// <inheritdoc />
    public async Task OnActivatedAsync(CancellationToken cancellationToken = default)
    {
      _loadTemplatesCts?.Cancel();
      _loadTemplatesCts?.Dispose();
      using var linked = CancellationTokenSource.CreateLinkedTokenSource(_disposalCts.Token, cancellationToken);
      await LoadCategoriesAsync(linked.Token);
      await LoadTemplatesAsync(linked.Token);
    }

    /// <inheritdoc />
    public Task OnDeactivatedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken = default) => RefreshAsyncInternal(cancellationToken);

    private void OnSearchDebounceTick(object? sender, object e)
    {
      if (_disposalCts.IsCancellationRequested) return;
      _loadTemplatesCts?.Cancel();
      _loadTemplatesCts?.Dispose();
      _loadTemplatesCts = CancellationTokenSource.CreateLinkedTokenSource(_disposalCts.Token);
      _ = LoadTemplatesAsync(_loadTemplatesCts.Token);
    }

    protected override void Dispose(bool disposing)
    {
      if (disposing)
      {
        _searchDebounceTimer?.Stop();
        _loadTemplatesCts?.Cancel();
        _loadTemplatesCts?.Dispose();
        _disposalCts.Cancel();
        _disposalCts.Dispose();
      }
      base.Dispose(disposing);
    }

    public IAsyncRelayCommand LoadTemplatesCommand { get; }
    public IAsyncRelayCommand SearchTemplatesCommand { get; }
    public IAsyncRelayCommand CreateTemplateCommand { get; }
    public IAsyncRelayCommand<TemplateItem> UpdateTemplateCommand { get; }
    public IAsyncRelayCommand<TemplateItem> DeleteTemplateCommand { get; }
    public IAsyncRelayCommand<TemplateItem> ApplyTemplateCommand { get; }
    public IAsyncRelayCommand LoadCategoriesCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    private async Task LoadTemplatesAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var templates = await _templateLibraryClient.GetTemplatesAsync(SelectedCategory, SearchQuery, cancellationToken).ConfigureAwait(false);

        Templates.Clear();
        if (templates != null)
        {
          foreach (var template in templates)
          {
            Templates.Add(new TemplateItem(template));
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadTemplates");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task SearchTemplatesAsync(CancellationToken cancellationToken)
    {
      try
      {
        await LoadTemplatesAsync(cancellationToken);
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "SearchTemplates");
      }
    }

    private async Task CreateTemplateAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(CreatingName))
      {
        ErrorMessage = ResourceHelper.GetString("TemplateLibrary.TemplateNameRequired", "Template name is required");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var created = await _templateLibraryClient.CreateTemplateAsync(CreatingName!, CreatingCategory, CreatingDescription, cancellationToken).ConfigureAwait(false);

        if (created != null)
        {
          var templateItem = new TemplateItem(created);
          Templates.Insert(0, templateItem);
          SelectedTemplate = templateItem;
          StatusMessage = ResourceHelper.GetString("TemplateLibrary.TemplateCreated", "Template created");

          // Register undo action
          if (_undoRedoService != null)
          {
            var action = new CreateTemplateAction(
                Templates,
                _templateLibraryClient,
                templateItem,
                onUndo: (t) =>
                {
                  if (SelectedTemplate == t)
                  {
                    SelectedTemplate = Templates.FirstOrDefault();
                  }
                },
                onRedo: (t) => SelectedTemplate = t);
            _undoRedoService.RegisterAction(action);
          }
        }

        // Reset form
        CreatingName = null;
        CreatingCategory = null;
        CreatingDescription = null;
        IsCreating = false;
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "CreateTemplate");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task UpdateTemplateAsync(TemplateItem? template, CancellationToken cancellationToken)
    {
      if (template == null)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var updated = await _templateLibraryClient.UpdateTemplateAsync(
            template.Id,
            template.Name,
            template.Category,
            template.Description,
            template.Tags,
            template.IsPublic,
            cancellationToken).ConfigureAwait(false);

        if (updated != null)
        {
          template.UpdateFrom(updated);
        }

        await LoadTemplatesAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("TemplateLibrary.TemplateUpdated", "Template updated");
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "UpdateTemplate");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task DeleteTemplateAsync(TemplateItem? template, CancellationToken cancellationToken)
    {
      if (template == null)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        await _templateLibraryClient.DeleteTemplateAsync(template.Id, cancellationToken).ConfigureAwait(false);

        var originalIndex = Templates.IndexOf(template);
        Templates.Remove(template);
        if (SelectedTemplate == template)
        {
          SelectedTemplate = null;
        }
        StatusMessage = ResourceHelper.GetString("TemplateLibrary.TemplateDeleted", "Template deleted");

        // Register undo action
        if (_undoRedoService != null)
        {
          var action = new DeleteTemplateAction(
              Templates,
              _templateLibraryClient,
              template,
              originalIndex,
              onUndo: (t) => SelectedTemplate = t,
              onRedo: (t) =>
              {
                if (SelectedTemplate == t)
                {
                  SelectedTemplate = Templates.FirstOrDefault();
                }
              });
          _undoRedoService.RegisterAction(action);
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "DeleteTemplate");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ApplyTemplateAsync(TemplateItem? template, CancellationToken cancellationToken)
    {
      if (template == null)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var projectName = ResourceHelper.FormatString("TemplateLibrary.ProjectNameFromTemplate", template.Name);
        var response = await _templateLibraryClient.ApplyTemplateAsync(template.Id, projectName, cancellationToken).ConfigureAwait(false);

        if (response != null)
        {
          template.UsageCount++;
          StatusMessage = ResourceHelper.FormatString("TemplateLibrary.TemplateApplied", template.Name);
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "ApplyTemplate");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadCategoriesAsync(CancellationToken cancellationToken)
    {
      try
      {
        var categories = await _templateLibraryClient.GetCategoriesAsync(cancellationToken).ConfigureAwait(false);

        AvailableCategories.Clear();
        if (categories != null)
        {
          foreach (var category in categories)
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
        await HandleErrorAsync(ex, "LoadCategories");
      }
    }

    private async Task RefreshAsyncInternal(CancellationToken cancellationToken)
    {
      try
      {
        await LoadTemplatesAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("TemplateLibrary.TemplatesRefreshed", "Templates refreshed");
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "Refresh");
      }
    }

    partial void OnSelectedCategoryChanged(string? value)
    {
      if (_disposalCts.IsCancellationRequested) return;
      _searchDebounceTimer?.Stop();
      _loadTemplatesCts?.Cancel();
      _loadTemplatesCts?.Dispose();
      _loadTemplatesCts = CancellationTokenSource.CreateLinkedTokenSource(_disposalCts.Token);
      _ = LoadTemplatesAsync(_loadTemplatesCts.Token);
    }

    partial void OnSearchQueryChanged(string? value)
    {
      if (_disposalCts.IsCancellationRequested) return;
      _searchDebounceTimer?.Stop();
      _searchDebounceTimer?.Start();
    }
  }

  /// <summary>
  /// UI wrapper for TemplateLibraryTemplate.
  /// </summary>
  public class TemplateItem : ObservableObject
  {
    public string Id { get; set; }
    public string Name { get; set; }
    public string Category { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public System.Collections.Generic.List<string> Tags { get; set; }
    public string? Author { get; set; }
    public bool IsPublic { get; set; }
    public int UsageCount { get; set; }

    public TemplateItem(TemplateLibraryTemplate template)
    {
      Id = template.Id;
      Name = template.Name;
      Category = template.Category;
      Description = template.Description;
      ThumbnailUrl = template.ThumbnailUrl;
      Tags = template.Tags ?? new System.Collections.Generic.List<string>();
      Author = template.Author;
      IsPublic = template.IsPublic;
      UsageCount = template.UsageCount;
    }

    public void UpdateFrom(TemplateLibraryTemplate template)
    {
      Name = template.Name;
      Category = template.Category;
      Description = template.Description;
      Tags = template.Tags ?? new System.Collections.Generic.List<string>();
      IsPublic = template.IsPublic;
      OnPropertyChanged(nameof(Name));
      OnPropertyChanged(nameof(Category));
      OnPropertyChanged(nameof(Description));
    }
  }
}