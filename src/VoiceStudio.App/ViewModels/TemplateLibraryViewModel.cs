using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
  public partial class TemplateLibraryViewModel : BaseViewModel, IPanelView
  {
    private readonly IBackendClient _backendClient;
    private readonly UndoRedoService? _undoRedoService;
    private CancellationTokenSource? _searchDebounceCts;
    private const int SearchDebounceMs = 300;

    public string PanelId => "template_library";
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

    public TemplateLibraryViewModel(IViewModelContext context, IBackendClient backendClient)
        : base(context)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));

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
        await RefreshAsync(ct);
      }, () => !IsLoading);

      // Load initial data
      _ = LoadCategoriesAsync(CancellationToken.None);
      _ = LoadTemplatesAsync(CancellationToken.None);
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
        var queryParams = new System.Collections.Specialized.NameValueCollection();
        if (!string.IsNullOrEmpty(SelectedCategory))
          queryParams.Add("category", SelectedCategory);
        if (!string.IsNullOrEmpty(SearchQuery))
          queryParams.Add("search", SearchQuery);

        var queryString = string.Join("&",
            queryParams.AllKeys.SelectMany(key =>
                queryParams.GetValues(key)?.Select(value => $"{key}={Uri.EscapeDataString(value)}") ?? Array.Empty<string>()
            )
        );

        var url = "/api/templates";
        if (!string.IsNullOrEmpty(queryString))
          url += $"?{queryString}";

        var templates = await _backendClient.SendRequestAsync<object, Template[]>(
            url,
            null,
            System.Net.Http.HttpMethod.Get,
            cancellationToken
        );

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
        var request = new
        {
          name = CreatingName,
          category = CreatingCategory ?? "general",
          description = CreatingDescription,
          project_data = new { },
          tags = new string[] { },
          is_public = false
        };

        var created = await _backendClient.SendRequestAsync<object, Template>(
            "/api/templates",
            request,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

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
                _backendClient,
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
        var request = new
        {
          name = template.Name,
          category = template.Category,
          description = template.Description,
          tags = template.Tags,
          is_public = template.IsPublic
        };

        var updated = await _backendClient.SendRequestAsync<object, Template>(
            $"/api/templates/{template.Id}",
            request,
            System.Net.Http.HttpMethod.Put,
            cancellationToken
        );

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
        await _backendClient.SendRequestAsync<object, object>(
            $"/api/templates/{template.Id}",
            null,
            System.Net.Http.HttpMethod.Delete,
            cancellationToken
        );

        var originalIndex = Templates.IndexOf(template);
        Templates.Remove(template);
        var previousSelected = SelectedTemplate;
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
              _backendClient,
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
        var request = new
        {
          project_name = ResourceHelper.FormatString("TemplateLibrary.ProjectNameFromTemplate", template.Name)
        };

        var response = await _backendClient.SendRequestAsync<object, TemplateApplyResponse>(
            $"/api/templates/{template.Id}/apply",
            request,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

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
        var response = await _backendClient.SendRequestAsync<object, TemplateCategoriesResponse>(
            "/api/templates/categories/list",
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
        await HandleErrorAsync(ex, "LoadCategories");
      }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
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
      _ = LoadTemplatesAsync(CancellationToken.None);
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
          Dispatcher.TryEnqueue(() => _ = SearchTemplatesAsync(cts.Token));
        }
        catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "TemplateLibraryViewModel.OnSearchQueryChanged");
      }
      });
    }

    // Response models
    private class TemplateApplyResponse
    {
      public bool Success { get; set; }
      public string ProjectId { get; set; } = string.Empty;
      public string TemplateId { get; set; } = string.Empty;
      public string Message { get; set; } = string.Empty;
    }

    private class TemplateCategoriesResponse
    {
      public string[] Categories { get; set; } = Array.Empty<string>();
    }
  }

  // Data models
  public class Template
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public System.Collections.Generic.Dictionary<string, object> ProjectData { get; set; } = new();
    public System.Collections.Generic.List<string> Tags { get; set; } = new();
    public string? Author { get; set; }
    public string Version { get; set; } = "1.0";
    public bool IsPublic { get; set; }
    public int UsageCount { get; set; }
    public string Created { get; set; } = string.Empty;
    public string Modified { get; set; } = string.Empty;
  }

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

    public TemplateItem(Template template)
    {
      Id = template.Id;
      Name = template.Name;
      Category = template.Category;
      Description = template.Description;
      ThumbnailUrl = template.ThumbnailUrl;
      Tags = template.Tags;
      Author = template.Author;
      IsPublic = template.IsPublic;
      UsageCount = template.UsageCount;
    }

    public void UpdateFrom(Template template)
    {
      Name = template.Name;
      Category = template.Category;
      Description = template.Description;
      Tags = template.Tags;
      IsPublic = template.IsPublic;
      OnPropertyChanged(nameof(Name));
      OnPropertyChanged(nameof(Category));
      OnPropertyChanged(nameof(Description));
    }
  }
}