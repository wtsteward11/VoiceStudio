using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the PresetLibraryView panel - Preset management.
  /// </summary>
  public partial class PresetLibraryViewModel : BaseViewModel, IPanelView
  {
    private readonly IBackendClient _backendClient;
    private readonly IDialogService _dialogService;
    private readonly UndoRedoService? _undoRedoService;
    private CancellationTokenSource? _searchDebounceCts;
    private const int SearchDebounceMs = 300;

    public string PanelId => "preset_library";
    public string DisplayName => ResourceHelper.GetString("Panel.PresetLibrary.DisplayName", "Preset Library");
    public PanelRegion Region => PanelRegion.Right;

    [ObservableProperty]
    private ObservableCollection<Preset> presets = new();

    [ObservableProperty]
    private Preset? selectedPreset;

    [ObservableProperty]
    private string? searchQuery;

    [ObservableProperty]
    private string? selectedPresetType;

    [ObservableProperty]
    private string? selectedCategory;

    [ObservableProperty]
    private ObservableCollection<string> availablePresetTypes = new();

    [ObservableProperty]
    private ObservableCollection<string> availableCategories = new();

    [ObservableProperty]
    private int totalPresets;

    [ObservableProperty]
    private string? targetId; // Project ID, track ID, etc. for applying presets

    public PresetLibraryViewModel(IViewModelContext context, IBackendClient backendClient, IDialogService dialogService)
        : base(context)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
      _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

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

      LoadPresetsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadPresets");
        await LoadPresetsAsync(ct);
      }, () => !IsLoading);
      SearchPresetsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("SearchPresets");
        await SearchPresetsAsync(ct);
      }, () => !IsLoading);
      CreatePresetCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CreatePreset");
        await CreatePresetAsync(ct);
      }, () => !IsLoading);
      UpdatePresetCommand = new EnhancedAsyncRelayCommand<Preset>(async (preset, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("UpdatePreset");
        await UpdatePresetAsync(preset, ct);
      }, (preset) => preset != null && !IsLoading);
      DeletePresetCommand = new EnhancedAsyncRelayCommand<Preset>(async (preset, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeletePreset");
        await DeletePresetAsync(preset, ct);
      }, (preset) => preset != null && !IsLoading);
      ApplyPresetCommand = new EnhancedAsyncRelayCommand<Preset>(async (preset, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ApplyPreset");
        await ApplyPresetAsync(preset, ct);
      }, (preset) => preset != null && !IsLoading);
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      }, () => !IsLoading);
      LoadPresetTypesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadPresetTypes");
        await LoadPresetTypesAsync(ct);
      }, () => !IsLoading);
      LoadCategoriesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadCategories");
        await LoadCategoriesAsync(ct);
      }, () => !IsLoading);

      // Load initial data
      _ = LoadPresetTypesAsync(CancellationToken.None);
      _ = LoadPresetsAsync(CancellationToken.None);
    }

    public IAsyncRelayCommand LoadPresetsCommand { get; }
    public IAsyncRelayCommand SearchPresetsCommand { get; }
    public IAsyncRelayCommand CreatePresetCommand { get; }
    public IAsyncRelayCommand<Preset> UpdatePresetCommand { get; }
    public IAsyncRelayCommand<Preset> DeletePresetCommand { get; }
    public IAsyncRelayCommand<Preset> ApplyPresetCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand LoadPresetTypesCommand { get; }
    public IAsyncRelayCommand LoadCategoriesCommand { get; }

    private async Task LoadPresetsAsync(CancellationToken cancellationToken)
    {
      try
      {
        await SearchPresetsAsync(cancellationToken);
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadPresets");
      }
    }

    private async Task SearchPresetsAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var queryParams = new System.Collections.Specialized.NameValueCollection();
        if (!string.IsNullOrEmpty(SearchQuery))
          queryParams.Add("query", SearchQuery);
        if (!string.IsNullOrEmpty(SelectedPresetType))
          queryParams.Add("preset_type", SelectedPresetType);
        if (!string.IsNullOrEmpty(SelectedCategory))
          queryParams.Add("category", SelectedCategory);

        var queryString = string.Join("&",
            queryParams.AllKeys.SelectMany(key =>
                queryParams.GetValues(key)?.Select(value => $"{key}={Uri.EscapeDataString(value)}") ?? Array.Empty<string>()
            )
        );

        var url = "/api/presets";
        if (!string.IsNullOrEmpty(queryString))
          url += $"?{queryString}";

        var response = await _backendClient.SendRequestAsync<object, PresetSearchResponse>(
            url,
            null,
            System.Net.Http.HttpMethod.Get,
            cancellationToken
        );

        Presets.Clear();
        if (response?.Presets != null)
        {
          foreach (var preset in response.Presets)
          {
            Presets.Add(preset);
          }
        }

        TotalPresets = response?.Total ?? 0;
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "SearchPresets");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task CreatePresetAsync(CancellationToken cancellationToken)
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;

        // Show dialog to get preset details
        var presetDetails = await ShowPresetDialogAsync(cancellationToken);
        if (presetDetails == null)
        {
          // User cancelled
          return;
        }

        var name = presetDetails.Name;
        var presetType = presetDetails.Type;
        var category = presetDetails.Category;
        var description = presetDetails.Description;

        var request = new
        {
          name = name,
          preset_type = presetType,
          category = category,
          description = description,
          data = new { },
          tags = new string[] { },
          is_public = false
        };

        var createdPreset = await _backendClient.SendRequestAsync<object, Preset>(
            "/api/presets",
            request,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        if (createdPreset != null)
        {
          Presets.Insert(0, createdPreset);
          SelectedPreset = createdPreset;

          // Register undo action
          if (_undoRedoService != null)
          {
            var action = new CreatePresetAction(
                Presets,
                _backendClient,
                createdPreset,
                onUndo: (p) =>
                {
                  if (SelectedPreset?.Id == p.Id)
                  {
                    SelectedPreset = Presets.FirstOrDefault();
                  }
                },
                onRedo: (p) => SelectedPreset = p);
            _undoRedoService.RegisterAction(action);
          }
        }

        StatusMessage = ResourceHelper.GetString("PresetLibrary.PresetCreated", "Preset created");
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("PresetLibrary.CreatePresetFailed", ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task UpdatePresetAsync(Preset? preset, CancellationToken cancellationToken)
    {
      if (preset == null)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new
        {
          name = preset.Name,
          category = preset.Category,
          description = preset.Description,
          tags = preset.Tags,
          is_public = preset.IsPublic
        };

        await _backendClient.SendRequestAsync<object, Preset>(
            $"/api/presets/{preset.Id}",
            request,
            System.Net.Http.HttpMethod.Put,
            cancellationToken
        );

        await LoadPresetsAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("PresetLibrary.PresetUpdated", "Preset updated");
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "UpdatePreset");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task DeletePresetAsync(Preset? preset, CancellationToken cancellationToken)
    {
      if (preset == null)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var originalIndex = Presets.IndexOf(preset);
        Presets.Remove(preset);

        if (SelectedPreset?.Id == preset.Id)
        {
          SelectedPreset = null;
        }

        await _backendClient.SendRequestAsync<object, object>(
            $"/api/presets/{preset.Id}",
            null,
            System.Net.Http.HttpMethod.Delete,
            cancellationToken
        );

        // Register undo action
        if (_undoRedoService != null)
        {
          var action = new DeletePresetAction(
              Presets,
              _backendClient,
              preset,
              originalIndex,
              onUndo: (p) => SelectedPreset = p,
              onRedo: (p) =>
              {
                if (SelectedPreset?.Id == p.Id)
                {
                  SelectedPreset = Presets.FirstOrDefault();
                }
              });
          _undoRedoService.RegisterAction(action);
        }

        StatusMessage = ResourceHelper.GetString("PresetLibrary.PresetDeleted", "Preset deleted");
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "DeletePreset");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ApplyPresetAsync(Preset? preset, CancellationToken cancellationToken)
    {
      if (preset == null)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new
        {
          target_id = TargetId
        };

        await _backendClient.SendRequestAsync<object, PresetApplyResponse>(
            $"/api/presets/{preset.Id}/apply",
            request,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        StatusMessage = ResourceHelper.FormatString("PresetLibrary.PresetApplied", preset.Name);
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "ApplyPreset");
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
        await LoadPresetsAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("PresetLibrary.PresetsRefreshed", "Presets refreshed");
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

    private async Task LoadPresetTypesAsync(CancellationToken cancellationToken)
    {
      try
      {
        var response = await _backendClient.SendRequestAsync<object, PresetTypesResponse>(
            "/api/presets/types",
            null,
            System.Net.Http.HttpMethod.Get,
            cancellationToken
        );

        AvailablePresetTypes.Clear();
        if (response?.Types != null)
        {
          foreach (var type in response.Types)
          {
            AvailablePresetTypes.Add(type.Id);
          }
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("PresetLibrary.LoadPresetTypesFailed", ex.Message);
      }
    }

    private async Task LoadCategoriesAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SelectedPresetType))
      {
        AvailableCategories.Clear();
        return;
      }

      try
      {
        var categories = await _backendClient.SendRequestAsync<object, string[]>(
            $"/api/presets/categories/{SelectedPresetType}",
            null,
            System.Net.Http.HttpMethod.Get,
            cancellationToken
        );

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

    partial void OnSelectedPresetTypeChanged(string? value)
    {
      _ = LoadCategoriesAsync(CancellationToken.None);
      _ = SearchPresetsAsync(CancellationToken.None);
    }

    partial void OnSelectedCategoryChanged(string? value)
    {
      _ = SearchPresetsAsync(CancellationToken.None);
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
          Dispatcher.TryEnqueue(() => _ = SearchPresetsAsync(cts.Token));
        }
        catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "PresetLibraryViewModel.OnSearchQueryChanged");
      }
      });
    }

    private async Task<PresetDetails?> ShowPresetDialogAsync(CancellationToken cancellationToken)
    {
      var nameBox = new TextBox
      {
        PlaceholderText = ResourceHelper.GetString("PresetLibrary.PresetNamePlaceholder", "Preset name"),
        Text = ResourceHelper.GetString("PresetLibrary.NewPreset", "New Preset"),
        Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 12),
        HorizontalAlignment = HorizontalAlignment.Stretch
      };

      var typeCombo = new ComboBox
      {
        ItemsSource = AvailablePresetTypes,
        SelectedItem = SelectedPresetType ?? "effect",
        PlaceholderText = ResourceHelper.GetString("PresetLibrary.PresetTypePlaceholder", "Preset type"),
        Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 12),
        HorizontalAlignment = HorizontalAlignment.Stretch
      };

      var categoryBox = new TextBox
      {
        PlaceholderText = "Category (optional)",
        Text = SelectedCategory,
        Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 12),
        HorizontalAlignment = HorizontalAlignment.Stretch
      };

      var descriptionBox = new TextBox
      {
        PlaceholderText = "Description (optional)",
        AcceptsReturn = true,
        TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
        Height = 80,
        Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 12),
        HorizontalAlignment = HorizontalAlignment.Stretch
      };

      var stackPanel = new StackPanel
      {
        Spacing = 8,
        Children =
                {
                    new TextBlock { Text = ResourceHelper.GetString("PresetLibrary.NameLabel", "Name:"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    nameBox,
                    new TextBlock { Text = ResourceHelper.GetString("PresetLibrary.TypeLabel", "Type:"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Microsoft.UI.Xaml.Thickness(0, 8, 0, 0) },
                    typeCombo,
                    new TextBlock { Text = ResourceHelper.GetString("PresetLibrary.CategoryLabel", "Category:"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Microsoft.UI.Xaml.Thickness(0, 8, 0, 0) },
                    categoryBox,
                    new TextBlock { Text = ResourceHelper.GetString("PresetLibrary.DescriptionLabel", "Description:"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Microsoft.UI.Xaml.Thickness(0, 8, 0, 0) },
                    descriptionBox
                }
      };

      // Select all text when dialog opens
      nameBox.Loaded += (_, e) =>
      {
        nameBox.SelectAll();
        nameBox.Focus(FocusState.Programmatic);
      };

      cancellationToken.ThrowIfCancellationRequested();
      var confirmed = await _dialogService.ShowContentAsync(
        ResourceHelper.GetString("PresetLibrary.CreateNewPreset", "Create New Preset"),
        stackPanel,
        ResourceHelper.GetString("PresetLibrary.Create", "Create"),
        ResourceHelper.GetString("PresetLibrary.Cancel", "Cancel"));
      cancellationToken.ThrowIfCancellationRequested();
      if (confirmed)
      {
        var name = nameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
          ErrorMessage = ResourceHelper.GetString("PresetLibrary.PresetNameRequired", "Preset name is required");
          return null;
        }

        // Validate preset name (no invalid characters)
        var invalidChars = System.IO.Path.GetInvalidFileNameChars();
        if (name.IndexOfAny(invalidChars) >= 0)
        {
          ErrorMessage = ResourceHelper.GetString("PresetLibrary.PresetNameInvalidCharacters", "Preset name contains invalid characters");
          return null;
        }

        return new PresetDetails
        {
          Name = name,
          Type = typeCombo.SelectedItem?.ToString() ?? "effect",
          Category = categoryBox.Text?.Trim(),
          Description = descriptionBox.Text?.Trim()
        };
      }

      return null;
    }

    private class PresetDetails
    {
      public string Name { get; set; } = string.Empty;
      public string Type { get; set; } = string.Empty;
      public string? Category { get; set; }
      public string? Description { get; set; }
    }

    // Response models
    private class PresetSearchResponse
    {
      public Preset[] Presets { get; set; } = Array.Empty<Preset>();
      public int Total { get; set; }
      public int Limit { get; set; }
      public int Offset { get; set; }
    }

    private class PresetTypesResponse
    {
      public PresetTypeInfo[] Types { get; set; } = Array.Empty<PresetTypeInfo>();
    }

    private class PresetTypeInfo
    {
      public string Id { get; set; } = string.Empty;
      public string Name { get; set; } = string.Empty;
    }

    private class PresetApplyResponse
    {
      public bool Success { get; set; }
      public string PresetId { get; set; } = string.Empty;
      public string? TargetId { get; set; }
    }
  }

  // Data models moved to separate file
}