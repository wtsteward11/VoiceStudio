using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Utilities;
using MixSuggestionModel = VoiceStudio.App.ViewModels.MixAssistantViewModel.MixSuggestion;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the MixAssistantView panel - AI mixing & mastering assistant.
  /// </summary>
  public partial class MixAssistantViewModel : BaseViewModel, IPanelView
  {
    private readonly IBackendClient _backendClient;
    private readonly IProjectsClient _projectsClient;

    public string PanelId => "mix-assistant";
    public string DisplayName => ResourceHelper.GetString("Panel.MixAssistant.DisplayName", "AI Mix Assistant");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private string? selectedProjectId;

    [ObservableProperty]
    private ObservableCollection<MixAssistantProjectItem> availableProjects = new();

    [ObservableProperty]
    private MixAssistantProjectItem? selectedProject;

    [ObservableProperty]
    private bool analyzeLevels = true;

    [ObservableProperty]
    private bool analyzeFrequency = true;

    [ObservableProperty]
    private bool analyzeStereo = true;

    [ObservableProperty]
    private bool analyzeDynamics = true;

    [ObservableProperty]
    private ObservableCollection<MixAssistantMixSuggestionItem> suggestions = new();

    [ObservableProperty]
    private MixAssistantMixSuggestionItem? selectedSuggestion;

    [ObservableProperty]
    private string? selectedCategory;

    [ObservableProperty]
    private ObservableCollection<string> availableCategories = new() { "all", "levels", "frequency", "stereo", "dynamics", "effects" };

    [ObservableProperty]
    private string? selectedPriority;

    [ObservableProperty]
    private ObservableCollection<string> availablePriorities = new() { "all", "high", "medium", "low" };

    [ObservableProperty]
    private string presetName = string.Empty;

    [ObservableProperty]
    private string? selectedGenre;

    [ObservableProperty]
    private ObservableCollection<string> availableGenres = new() { "pop", "rock", "jazz", "classical", "electronic", "hip-hop", "country" };

    public MixAssistantViewModel(IViewModelContext context, IBackendClient backendClient, IProjectsClient projectsClient)
        : base(context)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
      _projectsClient = projectsClient ?? throw new ArgumentNullException(nameof(projectsClient));

      AnalyzeMixCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("AnalyzeMix");
        await AnalyzeMixAsync(ct);
      }, () => !string.IsNullOrEmpty(SelectedProjectId) && !IsLoading);
      ApplySuggestionCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ApplySuggestion");
        await ApplySuggestionAsync(ct);
      }, () => SelectedSuggestion != null && !IsLoading);
      ApplyAllSuggestionsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ApplyAllSuggestions");
        await ApplyAllSuggestionsAsync(ct);
      }, () => Suggestions.Count > 0 && !IsLoading);
      DismissSuggestionCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DismissSuggestion");
        await DismissSuggestionAsync(ct);
      }, () => SelectedSuggestion != null && !IsLoading);
      GeneratePresetCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("GeneratePreset");
        await GeneratePresetAsync(ct);
      }, () => !string.IsNullOrEmpty(SelectedProjectId) && !string.IsNullOrEmpty(PresetName) && !IsLoading);
      LoadSuggestionsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadSuggestions");
        await LoadSuggestionsAsync(ct);
      }, () => !IsLoading);
      LoadProjectsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadProjects");
        await LoadProjectsAsync(ct);
      }, () => !IsLoading);
      LoadProjectCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadProject");
        await LoadProjectAsync(ct);
      }, () => !string.IsNullOrEmpty(SelectedProjectId) && !IsLoading);
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      }, () => !IsLoading);

      // Load initial data
      _ = LoadProjectsAsync(CancellationToken.None);
      _ = LoadSuggestionsAsync(CancellationToken.None);
    }

    public IAsyncRelayCommand AnalyzeMixCommand { get; }
    public IAsyncRelayCommand ApplySuggestionCommand { get; }
    public IAsyncRelayCommand ApplyAllSuggestionsCommand { get; }
    public IAsyncRelayCommand DismissSuggestionCommand { get; }
    public IAsyncRelayCommand GeneratePresetCommand { get; }
    public IAsyncRelayCommand LoadSuggestionsCommand { get; }
    public IAsyncRelayCommand LoadProjectsCommand { get; }
    public IAsyncRelayCommand LoadProjectCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    partial void OnSelectedProjectIdChanged(string? value)
    {
      ((System.Windows.Input.ICommand)LoadProjectCommand).NotifyCanExecuteChanged();

      // Update selected project object
      if (!string.IsNullOrEmpty(value))
      {
        SelectedProject = AvailableProjects.FirstOrDefault(p => p.Id == value);
      }
      else
      {
        SelectedProject = null;
      }
    }

    partial void OnSelectedProjectChanged(MixAssistantProjectItem? value)
    {
      if (value != null && SelectedProjectId != value.Id)
      {
        SelectedProjectId = value.Id;
      }
    }

    private async Task AnalyzeMixAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SelectedProjectId))
      {
        ErrorMessage = ResourceHelper.GetString("MixAssistant.ProjectRequired", "Project must be selected");
        return;
      }

      // Validate project exists
      if (!AvailableProjects.Any(p => p.Id == SelectedProjectId))
      {
        ErrorMessage = ResourceHelper.GetString("MixAssistant.ProjectDoesNotExist", "Selected project does not exist. Please refresh and select a valid project.");
        SelectedProjectId = null;
        SelectedProject = null;
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new
        {
          project_id = SelectedProjectId,
          analyze_levels = AnalyzeLevels,
          analyze_frequency = AnalyzeFrequency,
          analyze_stereo = AnalyzeStereo,
          analyze_dynamics = AnalyzeDynamics
        };

        var suggestions = await _backendClient.SendRequestAsync<object, MixSuggestion[]>(
            "/api/mix-assistant/analyze",
            request,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        if (suggestions != null)
        {
          foreach (var suggestion in suggestions)
          {
            Suggestions.Add(new MixAssistantMixSuggestionItem(suggestion));
          }
          StatusMessage = ResourceHelper.FormatString("MixAssistant.SuggestionsGenerated", suggestions.Length);
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "AnalyzeMix");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ApplySuggestionAsync(CancellationToken cancellationToken)
    {
      if (SelectedSuggestion == null)
      {
        ErrorMessage = ResourceHelper.GetString("MixAssistant.NoSuggestionSelected", "No suggestion selected");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new
        {
          suggestion_ids = new[] { SelectedSuggestion.SuggestionId },
          apply_all = false
        };

        await _backendClient.SendRequestAsync<object, object>(
            "/api/mix-assistant/apply",
            request,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        Suggestions.Remove(SelectedSuggestion);
        SelectedSuggestion = null;
        StatusMessage = ResourceHelper.GetString("MixAssistant.SuggestionApplied", "Suggestion applied");
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "ApplySuggestion");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ApplyAllSuggestionsAsync(CancellationToken cancellationToken)
    {
      if (Suggestions.Count == 0)
      {
        ErrorMessage = ResourceHelper.GetString("MixAssistant.NoSuggestionsToApply", "No suggestions to apply");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new
        {
          suggestion_ids = Suggestions.Select(s => s.SuggestionId).ToArray(),
          apply_all = true
        };

        await _backendClient.SendRequestAsync<object, object>(
            "/api/mix-assistant/apply",
            request,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        Suggestions.Clear();
        StatusMessage = ResourceHelper.GetString("MixAssistant.AllSuggestionsApplied", "All suggestions applied");
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "ApplyAllSuggestions");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task DismissSuggestionAsync(CancellationToken cancellationToken)
    {
      if (SelectedSuggestion == null)
      {
        ErrorMessage = ResourceHelper.GetString("MixAssistant.NoSuggestionSelected", "No suggestion selected");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        await _backendClient.SendRequestAsync<object, object>(
            $"/api/mix-assistant/suggestions/{Uri.EscapeDataString(SelectedSuggestion.SuggestionId)}",
            null,
            System.Net.Http.HttpMethod.Delete,
            cancellationToken
        );

        Suggestions.Remove(SelectedSuggestion);
        SelectedSuggestion = null;
        StatusMessage = ResourceHelper.GetString("MixAssistant.SuggestionDismissed", "Suggestion dismissed");
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "DismissSuggestion");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task GeneratePresetAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SelectedProjectId))
      {
        ErrorMessage = ResourceHelper.GetString("MixAssistant.ProjectRequired", "Project must be selected");
        return;
      }

      // Validate project exists
      if (!AvailableProjects.Any(p => p.Id == SelectedProjectId))
      {
        ErrorMessage = ResourceHelper.GetString("MixAssistant.ProjectDoesNotExist", "Selected project does not exist. Please refresh and select a valid project.");
        SelectedProjectId = null;
        SelectedProject = null;
        return;
      }

      if (string.IsNullOrEmpty(PresetName))
      {
        ErrorMessage = ResourceHelper.GetString("MixAssistant.PresetNameRequired", "Preset name is required");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var preset = await _backendClient.SendRequestAsync<object, MixPreset>(
            $"/api/mix-assistant/presets/generate?project_id={Uri.EscapeDataString(SelectedProjectId)}&name={Uri.EscapeDataString(PresetName)}&genre={Uri.EscapeDataString(SelectedGenre ?? "")}",
            null,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        if (preset != null)
        {
          StatusMessage = ResourceHelper.FormatString("MixAssistant.PresetGenerated", preset.Name);
          PresetName = string.Empty;
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "GeneratePreset");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadSuggestionsAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var queryParams = new List<string>();
        if (!string.IsNullOrEmpty(SelectedProjectId))
        {
          queryParams.Add($"project_id={Uri.EscapeDataString(SelectedProjectId)}");
        }
        if (!string.IsNullOrEmpty(SelectedCategory) && SelectedCategory != "all")
        {
          queryParams.Add($"category={Uri.EscapeDataString(SelectedCategory)}");
        }
        if (!string.IsNullOrEmpty(SelectedPriority) && SelectedPriority != "all")
        {
          queryParams.Add($"priority={Uri.EscapeDataString(SelectedPriority)}");
        }

        var url = "/api/mix-assistant/suggestions";
        if (queryParams.Count > 0)
        {
          url += "?" + string.Join("&", queryParams);
        }

        var suggestions = await _backendClient.SendRequestAsync<object, MixSuggestion[]>(
            url,
            null,
            System.Net.Http.HttpMethod.Get,
            cancellationToken
        );

        if (suggestions != null)
        {
          Suggestions.Clear();
          foreach (var suggestion in suggestions)
          {
            Suggestions.Add(new MixAssistantMixSuggestionItem(suggestion));
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadSuggestions");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadProjectsAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var projects = await _projectsClient.GetProjectsAsync(cancellationToken);

        AvailableProjects.Clear();
        foreach (var project in projects)
        {
          AvailableProjects.Add(new MixAssistantProjectItem
          {
            Id = project.Id,
            Name = project.Name ?? project.Id,
            Description = project.Description,
            Created = project.CreatedAt,
            Modified = project.UpdatedAt
          });
        }

        // Validate selected project still exists
        if (!string.IsNullOrEmpty(SelectedProjectId))
        {
          if (!AvailableProjects.Any(p => p.Id == SelectedProjectId))
          {
            SelectedProjectId = null;
            SelectedProject = null;
            StatusMessage = ResourceHelper.GetString("MixAssistant.ProjectNoLongerExists", "Previously selected project no longer exists");
          }
          else
          {
            SelectedProject = AvailableProjects.FirstOrDefault(p => p.Id == SelectedProjectId);
          }
        }

        StatusMessage = ResourceHelper.FormatString("MixAssistant.ProjectsLoaded", AvailableProjects.Count);
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadProjects");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadProjectAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SelectedProjectId))
      {
        ErrorMessage = ResourceHelper.GetString("MixAssistant.NoProjectSelected", "No project selected");
        return;
      }

      // Validate project exists
      if (!AvailableProjects.Any(p => p.Id == SelectedProjectId))
      {
        ErrorMessage = ResourceHelper.GetString("MixAssistant.ProjectDoesNotExist", "Selected project does not exist. Please refresh and select a valid project.");
        SelectedProjectId = null;
        SelectedProject = null;
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var project = await _backendClient.GetProjectAsync(SelectedProjectId, cancellationToken);

        if (project != null)
        {
          // Update project details
          var projectItem = AvailableProjects.FirstOrDefault(p => p.Id == SelectedProjectId);
          if (projectItem != null)
          {
            projectItem.Name = project.Name ?? project.Id;
            projectItem.Description = project.Description;
            projectItem.Modified = project.UpdatedAt;
            SelectedProject = projectItem;
          }

          StatusMessage = ResourceHelper.FormatString("MixAssistant.ProjectLoaded", project.Name ?? project.Id);
        }
        else
        {
          ErrorMessage = ResourceHelper.GetString("MixAssistant.ProjectNotFound", "Project not found");
          SelectedProjectId = null;
          SelectedProject = null;
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadProject");
        // If project doesn't exist, clear selection
        if (ex.Message.Contains("not found") || ex.Message.Contains("404"))
        {
          SelectedProjectId = null;
          SelectedProject = null;
        }
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
      try
      {
        await LoadProjectsAsync(cancellationToken);
        await LoadSuggestionsAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("MixAssistant.Refreshed", "Refreshed");
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

    // Response models
    public class MixSuggestion
    {
      public string SuggestionId { get; set; } = string.Empty;
      public string ProjectId { get; set; } = string.Empty;
      public string Category { get; set; } = string.Empty;
      public string Priority { get; set; } = string.Empty;
      public string Description { get; set; } = string.Empty;
      public string? Parameter { get; set; }
      public double? CurrentValue { get; set; }
      public double? SuggestedValue { get; set; }
      public double Confidence { get; set; }
      public string Created { get; set; } = string.Empty;
    }

    private class MixPreset
    {
      public string PresetId { get; set; } = string.Empty;
      public string Name { get; set; } = string.Empty;
      public string? Description { get; set; }
      public string? Genre { get; set; }
      public Dictionary<string, object> Settings { get; set; } = new();
      public string Created { get; set; } = string.Empty;
    }
  }

  // Data models
  public class MixAssistantMixSuggestionItem : ObservableObject
  {
    public string SuggestionId { get; set; }
    public string ProjectId { get; set; }
    public string Category { get; set; }
    public string Priority { get; set; }
    public string Description { get; set; }
    public string? Parameter { get; set; }
    public double? CurrentValue { get; set; }
    public double? SuggestedValue { get; set; }
    public double Confidence { get; set; }
    public string Created { get; set; }
    public string ConfidenceDisplay => $"{Confidence:P0}";
    public string ValueChangeDisplay
    {
      get
      {
        if (CurrentValue.HasValue && SuggestedValue.HasValue)
        {
          var change = SuggestedValue.Value - CurrentValue.Value;
          return $"{CurrentValue:F1} → {SuggestedValue:F1} ({change:+#0.0;-#0.0})";
        }
        return string.Empty;
      }
    }

    public MixAssistantMixSuggestionItem(MixSuggestionModel suggestion)
    {
      SuggestionId = suggestion.SuggestionId;
      ProjectId = suggestion.ProjectId;
      Category = suggestion.Category;
      Priority = suggestion.Priority;
      Description = suggestion.Description;
      Parameter = suggestion.Parameter;
      CurrentValue = suggestion.CurrentValue;
      SuggestedValue = suggestion.SuggestedValue;
      Confidence = suggestion.Confidence;
      Created = suggestion.Created;
    }
  }

  public class MixAssistantProjectItem : ObservableObject
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Created { get; set; }
    public string? Modified { get; set; }

    public string DisplayName => !string.IsNullOrEmpty(Name) ? Name : Id;
  }
}