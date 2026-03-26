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
  public partial class KeyboardShortcutsViewModel : BaseViewModel, ILifecyclePanelView
  {
    private readonly IKeyboardShortcutsClient _shortcutsClient;
    private readonly CancellationTokenSource _disposalCts = new();
    private CancellationTokenSource? _loadCts;
    private readonly IDispatcherTimer? _searchDebounceTimer;
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

    public KeyboardShortcutsViewModel(IViewModelContext context, IKeyboardShortcutsClient shortcutsClient)
        : base(context)
    {
      _shortcutsClient = shortcutsClient ?? throw new ArgumentNullException(nameof(shortcutsClient));

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
      await LoadShortcutsAsync(linked.Token);
    }

    /// <inheritdoc />
    public Task OnDeactivatedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken = default) => LoadShortcutsAsync(cancellationToken);

    private void OnSearchDebounceTick(object? sender, object e)
    {
      if (_disposalCts.IsCancellationRequested) return;
      _loadCts?.Cancel();
      _loadCts?.Dispose();
      _loadCts = CancellationTokenSource.CreateLinkedTokenSource(_disposalCts.Token);
      _ = SearchShortcutsAsync(_loadCts.Token);
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
      var categorySnapshot = SelectedCategory;
      var panelSnapshot = SelectedPanelId;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var shortcuts = await _shortcutsClient.GetShortcutsAsync(categorySnapshot, panelSnapshot, cancellationToken);

        if (SelectedCategory != categorySnapshot || SelectedPanelId != panelSnapshot)
          return;

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
        var request = new { key = shortcut.Key, key_code = shortcut.KeyCode, modifiers = shortcut.Modifiers, description = shortcut.Description };
        var updated = await _shortcutsClient.UpdateShortcutAsync(shortcut.Id, request, cancellationToken);

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
        var reset = await _shortcutsClient.ResetShortcutAsync(shortcut.Id, cancellationToken);

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
        await _shortcutsClient.ResetAllAsync(cancellationToken);

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

        var request = new { key = EditingKey, key_code = keyCode, modifiers };
        var updated = await _shortcutsClient.UpdateShortcutAsync(SelectedShortcut.Id, request, cancellationToken);

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

        var response = await _shortcutsClient.CheckConflictAsync(keyCode, modifiers.ToArray(), SelectedShortcut?.Id, cancellationToken);

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
        var response = await _shortcutsClient.GetCategoriesAsync(cancellationToken);

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
      _loadCts?.Cancel();
      _loadCts?.Dispose();
      _loadCts = CancellationTokenSource.CreateLinkedTokenSource(_disposalCts.Token);
      _ = LoadShortcutsAsync(_loadCts.Token);
    }

    partial void OnSelectedPanelIdChanged(string? value)
    {
      _loadCts?.Cancel();
      _loadCts?.Dispose();
      _loadCts = CancellationTokenSource.CreateLinkedTokenSource(_disposalCts.Token);
      _ = LoadShortcutsAsync(_loadCts.Token);
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