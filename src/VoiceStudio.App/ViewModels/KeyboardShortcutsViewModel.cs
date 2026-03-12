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
using KeyboardShortcut = VoiceStudio.App.ViewModels.KeyboardShortcutsShortcut;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the KeyboardShortcutsView panel - Keyboard shortcuts editor.
  /// </summary>
  public partial class KeyboardShortcutsViewModel : BaseViewModel, IPanelView
  {
    private readonly IBackendClient _backendClient;
    private CancellationTokenSource? _searchDebounceCts;
    private const int SearchDebounceMs = 300;

    public string PanelId => "keyboard_shortcuts";
    public string DisplayName => ResourceHelper.GetString("Panel.KeyboardShortcuts.DisplayName", "Keyboard Shortcuts");
    public PanelRegion Region => PanelRegion.Right;

    [ObservableProperty]
    private ObservableCollection<ShortcutItem> shortcuts = new();

    [ObservableProperty]
    private ShortcutItem? selectedShortcut;

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
    private bool isEditing;

    [ObservableProperty]
    private string? editingKey;

    [ObservableProperty]
    private string? conflictMessage;

    public KeyboardShortcutsViewModel(IViewModelContext context, IBackendClient backendClient)
        : base(context)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));

      LoadShortcutsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadShortcuts");
        await LoadShortcutsAsync(ct);
      });
      SearchShortcutsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("SearchShortcuts");
        await SearchShortcutsAsync(ct);
      });
      UpdateShortcutCommand = new EnhancedAsyncRelayCommand<ShortcutItem>(async (shortcut, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("UpdateShortcut");
        await UpdateShortcutAsync(shortcut, ct);
      });
      ResetShortcutCommand = new EnhancedAsyncRelayCommand<ShortcutItem>(async (shortcut, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ResetShortcut");
        await ResetShortcutAsync(shortcut, ct);
      });
      ResetAllCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ResetAll");
        await ResetAllAsync(ct);
      });
      StartEditCommand = new RelayCommand<ShortcutItem>(StartEdit);
      CancelEditCommand = new RelayCommand(CancelEdit);
      SaveEditCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("SaveEdit");
        await SaveEditAsync(ct);
      });
      LoadCategoriesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadCategories");
        await LoadCategoriesAsync(ct);
      });
      CheckConflictCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CheckConflict");
        await CheckConflictAsync(ct);
      });

      // Load initial data
      _ = LoadCategoriesAsync(CancellationToken.None);
      _ = LoadShortcutsAsync(CancellationToken.None);
    }

    public IAsyncRelayCommand LoadShortcutsCommand { get; }
    public IAsyncRelayCommand SearchShortcutsCommand { get; }
    public IAsyncRelayCommand<ShortcutItem> UpdateShortcutCommand { get; }
    public IAsyncRelayCommand<ShortcutItem> ResetShortcutCommand { get; }
    public IAsyncRelayCommand ResetAllCommand { get; }
    public IRelayCommand<ShortcutItem> StartEditCommand { get; }
    public IRelayCommand CancelEditCommand { get; }
    public IAsyncRelayCommand SaveEditCommand { get; }
    public IAsyncRelayCommand LoadCategoriesCommand { get; }
    public IAsyncRelayCommand CheckConflictCommand { get; }

    private async Task LoadShortcutsAsync(CancellationToken cancellationToken)
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

        var url = "/api/shortcuts";
        if (!string.IsNullOrEmpty(queryString))
          url += $"?{queryString}";

        var shortcuts = await _backendClient.SendRequestAsync<object, KeyboardShortcut[]>(
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
            Shortcuts.Add(new ShortcutItem(shortcut));
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("KeyboardShortcuts.LoadShortcutsFailed", ex.Message);
        await HandleErrorAsync(ex, "LoadShortcuts");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task SearchShortcutsAsync(CancellationToken cancellationToken)
    {
      await LoadShortcutsAsync(cancellationToken);
    }

    private async Task UpdateShortcutAsync(ShortcutItem? shortcut, CancellationToken cancellationToken)
    {
      if (shortcut == null)
        return;

      IsLoading = true;
      ErrorMessage = null;
      ConflictMessage = null;

      try
      {
        var request = new
        {
          key = shortcut.Key,
          key_code = shortcut.KeyCode,
          modifiers = shortcut.Modifiers,
          description = shortcut.Description
        };

        var updated = await _backendClient.SendRequestAsync<object, KeyboardShortcut>(
            $"/api/shortcuts/{shortcut.Id}",
            request,
            System.Net.Http.HttpMethod.Put,
            cancellationToken
        );

        if (updated != null)
        {
          shortcut.UpdateFrom(updated);
        }

        await LoadShortcutsAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("KeyboardShortcuts.ShortcutUpdated", "Shortcut updated");
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("KeyboardShortcuts.UpdateShortcutFailed", ex.Message);
        await HandleErrorAsync(ex, "UpdateShortcut");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ResetShortcutAsync(ShortcutItem? shortcut, CancellationToken cancellationToken)
    {
      if (shortcut == null)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var reset = await _backendClient.SendRequestAsync<object, KeyboardShortcut>(
            $"/api/shortcuts/{shortcut.Id}/reset",
            null,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        if (reset != null)
        {
          shortcut.UpdateFrom(reset);
        }

        await LoadShortcutsAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("KeyboardShortcuts.ShortcutReset", "Shortcut reset to default");
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("KeyboardShortcuts.ResetShortcutFailed", ex.Message);
        await HandleErrorAsync(ex, "ResetShortcut");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ResetAllAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        await _backendClient.SendRequestAsync<object, object>(
            "/api/shortcuts/reset-all",
            null,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        await LoadShortcutsAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("KeyboardShortcuts.AllShortcutsReset", "All shortcuts reset to defaults");
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("KeyboardShortcuts.ResetAllFailed", ex.Message);
        await HandleErrorAsync(ex, "ResetAll");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private void StartEdit(ShortcutItem? shortcut)
    {
      if (shortcut == null)
        return;

      SelectedShortcut = shortcut;
      EditingKey = shortcut.Key;
      IsEditing = true;
      ConflictMessage = null;
    }

    private void CancelEdit()
    {
      IsEditing = false;
      EditingKey = null;
      SelectedShortcut = null;
      ConflictMessage = null;
    }

    private async Task SaveEditAsync(CancellationToken cancellationToken)
    {
      if (SelectedShortcut == null || string.IsNullOrEmpty(EditingKey))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        // Parse the key combination
        var parts = EditingKey.Split('+').Select(p => p.Trim()).ToList();
        var modifiers = parts.Take(parts.Count - 1).ToList();
        var keyCode = parts.Last();

        // Check for conflicts
        await CheckConflictAsync(cancellationToken);

        if (!string.IsNullOrEmpty(ConflictMessage))
        {
          ErrorMessage = ConflictMessage;
          return;
        }

        var request = new
        {
          key = EditingKey,
          key_code = keyCode,
          modifiers = modifiers
        };

        var updated = await _backendClient.SendRequestAsync<object, KeyboardShortcut>(
            $"/api/shortcuts/{SelectedShortcut.Id}",
            request,
            System.Net.Http.HttpMethod.Put,
            cancellationToken
        );

        if (updated != null)
        {
          SelectedShortcut.UpdateFrom(updated);
        }

        CancelEdit();
        await LoadShortcutsAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("KeyboardShortcuts.ShortcutUpdated", "Shortcut updated");
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to save shortcut: {ex.Message}";
        await HandleErrorAsync(ex, "SaveEdit");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task CheckConflictAsync(CancellationToken cancellationToken)
    {
      if (SelectedShortcut == null || string.IsNullOrEmpty(EditingKey))
        return;

      try
      {
        var parts = EditingKey.Split('+').Select(p => p.Trim()).ToList();
        var modifiers = parts.Take(parts.Count - 1).ToList();
        var keyCode = parts.Last();

        var queryParams = new System.Collections.Specialized.NameValueCollection();
        queryParams.Add("key_code", keyCode);
        foreach (var mod in modifiers)
        {
          queryParams.Add("modifiers", mod);
        }
        if (SelectedShortcut != null)
        {
          queryParams.Add("exclude_id", SelectedShortcut.Id);
        }

        var queryString = string.Join("&",
            queryParams.AllKeys.SelectMany(key =>
                queryParams.GetValues(key)?.Select(value => $"{key}={Uri.EscapeDataString(value)}") ?? Array.Empty<string>()
            )
        );

        var response = await _backendClient.SendRequestAsync<object, ConflictCheckResponse>(
            $"/api/shortcuts/check-conflict?{queryString}",
            null,
            System.Net.Http.HttpMethod.Get,
            cancellationToken
        );

        if (response?.HasConflict == true)
        {
          ConflictMessage = ResourceHelper.FormatString("KeyboardShortcuts.ConflictsWith", response.ConflictingShortcut?.Description ?? "");
        }
        else
        {
          ConflictMessage = null;
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ConflictMessage = $"Error checking conflict: {ex.Message}";
      }
    }

    private async Task LoadCategoriesAsync(CancellationToken cancellationToken)
    {
      try
      {
        var response = await _backendClient.SendRequestAsync<object, ShortcutCategoriesResponse>(
            "/api/shortcuts/categories",
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
        ErrorMessage = ResourceHelper.FormatString("KeyboardShortcuts.LoadCategoriesFailed", ex.Message);
        await HandleErrorAsync(ex, "LoadCategories");
      }
    }

    partial void OnSelectedCategoryChanged(string? value)
    {
      _ = LoadShortcutsAsync(CancellationToken.None);
    }

    partial void OnSelectedPanelIdChanged(string? value)
    {
      _ = LoadShortcutsAsync(CancellationToken.None);
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
          Dispatcher.TryEnqueue(() => _ = SearchShortcutsAsync(cts.Token));
        }
        catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "KeyboardShortcutsViewModel.OnSearchQueryChanged");
      }
      });
    }

    // Response models
    private class ConflictCheckResponse
    {
      public bool HasConflict { get; set; }
      public KeyboardShortcut? ConflictingShortcut { get; set; }
    }

    private class ShortcutCategoriesResponse
    {
      public string[] Categories { get; set; } = Array.Empty<string>();
    }
  }

  // Data models
  public class KeyboardShortcutsShortcut
  {
    public string Id { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string KeyCode { get; set; } = string.Empty;
    public System.Collections.Generic.List<string> Modifiers { get; set; } = new();
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? PanelId { get; set; }
    public string? ActionId { get; set; }
    public bool IsCustom { get; set; }
  }

  public class ShortcutItem : ObservableObject
  {
    public string Id { get; set; }
    public string Key { get; set; }
    public string KeyCode { get; set; }
    public System.Collections.Generic.List<string> Modifiers { get; set; }
    public string Description { get; set; }
    public string Category { get; set; }
    public string? PanelId { get; set; }
    public bool IsCustom { get; set; }

    public ShortcutItem(KeyboardShortcut shortcut)
    {
      Id = shortcut.Id;
      Key = shortcut.Key;
      KeyCode = shortcut.KeyCode;
      Modifiers = shortcut.Modifiers;
      Description = shortcut.Description;
      Category = shortcut.Category;
      PanelId = shortcut.PanelId;
      IsCustom = shortcut.IsCustom;
    }

    public void UpdateFrom(KeyboardShortcut shortcut)
    {
      Key = shortcut.Key;
      KeyCode = shortcut.KeyCode;
      Modifiers = shortcut.Modifiers;
      Description = shortcut.Description;
      OnPropertyChanged(nameof(Key));
      OnPropertyChanged(nameof(Description));
    }
  }
}