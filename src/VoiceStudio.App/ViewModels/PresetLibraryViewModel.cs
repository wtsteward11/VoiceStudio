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
    private readonly IPresetLibraryClient _presetLibraryClient;
    private readonly IDialogService _dialogService;
    private readonly UndoRedoService? _undoRedoService;
    private CancellationTokenSource? _searchDebounceCts;
    private bool _isInitialized;
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

    public PresetLibraryViewModel(IViewModelContext context, IPresetLibraryClient presetLibraryClient, IDialogService dialogService)
        : base(context)
    {
      _presetLibraryClient = presetLibraryClient ?? throw new ArgumentNullException(nameof(presetLibraryClient));
      _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

      // Get undo/redo service (may be null if not initialized)
      try
      {
        _undoRedoService = AppServices.TryGetUndoRedoService();
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[PresetLibraryViewModel] UndoRedoService not available: {ex.Message}");
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
    }

    /// <summary>
    /// Initialize panel data. Call from view Loaded event (ADR-047).
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
      if (_isInitialized)
      {
        return;
      }

      _isInitialized = true;
      await LoadPresetTypesAsync(ct).ConfigureAwait(false);
      await LoadPresetsAsync(ct).ConfigureAwait(false);
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
        var response = await _presetLibraryClient.SearchPresetsAsync(
            SearchQuery,
            SelectedPresetType,
            SelectedCategory,
            cancellationToken).ConfigureAwait(false);

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

        var request = new PresetCreateRequest
        {
          Name = name,
          PresetType = presetType,
          Category = category,
          Description = description,
          Data = new { },
          Tags = Array.Empty<string>(),
          IsPublic = false
        };

        var createdPreset = await _presetLibraryClient.CreatePresetAsync(request, cancellationToken).ConfigureAwait(false);

        if (createdPreset != null)
        {
          Presets.Insert(0, createdPreset);
          SelectedPreset = createdPreset;

          // Register undo action
          if (_undoRedoService != null)
          {
            var action = new CreatePresetAction(
                Presets,
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
        var request = new PresetUpdateRequest
        {
          Name = preset.Name,
          Category = preset.Category,
          Description = preset.Description,
          Tags = preset.Tags?.ToArray(),
          IsPublic = preset.IsPublic
        };

        await _presetLibraryClient.UpdatePresetAsync(preset.Id, request, cancellationToken).ConfigureAwait(false);

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

    /// <summary>
    /// Shows confirmation dialog and deletes preset via backend if confirmed.
    /// </summary>
    public async Task DeletePresetWithConfirmationAsync(Preset preset, CancellationToken ct = default)
    {
      var confirmed = await _dialogService.ShowConfirmationAsync(
          ResourceHelper.GetString("PresetLibrary.DeletePreset.Title", "Delete Preset"),
          ResourceHelper.GetString("PresetLibrary.DeletePreset.Message", "Are you sure you want to delete this preset? This action cannot be undone."),
          ResourceHelper.GetString("PresetLibrary.DeletePreset.Confirm", "Delete"),
          ResourceHelper.GetString("PresetLibrary.DeletePreset.Cancel", "Cancel")).ConfigureAwait(false);

      if (confirmed)
      {
        await DeletePresetAsync(preset, ct).ConfigureAwait(false);
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
        await _presetLibraryClient.DeletePresetAsync(preset.Id, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        var originalIndex = Presets.IndexOf(preset);
        Presets.Remove(preset);

        if (SelectedPreset?.Id == preset.Id)
        {
          SelectedPreset = null;
        }

        // Register undo action
        if (_undoRedoService != null)
        {
          var action = new DeletePresetAction(
              Presets,
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
        await _presetLibraryClient.ApplyPresetAsync(preset.Id, TargetId, cancellationToken).ConfigureAwait(false);

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
        var types = await _presetLibraryClient.GetPresetTypesAsync(cancellationToken).ConfigureAwait(false);

        AvailablePresetTypes.Clear();
        if (types != null)
        {
          foreach (var type in types)
          {
            AvailablePresetTypes.Add(type);
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
        var categories = await _presetLibraryClient.GetCategoriesAsync(SelectedPresetType ?? string.Empty, cancellationToken).ConfigureAwait(false);

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

  }

  // Data models moved to separate file
}