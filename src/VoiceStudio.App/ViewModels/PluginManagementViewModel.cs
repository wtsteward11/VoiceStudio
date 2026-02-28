using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Plugins;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Utilities;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for Plugin Management Panel.
  /// Phase 1: Refactored to use PluginBridgeService for synchronized state.
  /// </summary>
  public partial class PluginManagementViewModel : BaseViewModel, IPanelView
  {
    private readonly PluginManager? _pluginManager;
    private readonly PluginBridgeService? _pluginBridge;

    public string PanelId => "pluginmanagement";
    public string DisplayName => ResourceHelper.GetString("Panel.PluginManagement.DisplayName", "Plugin Management");
    public PanelRegion Region => PanelRegion.Right;

    [ObservableProperty]
    private ObservableCollection<PluginInfo> plugins = new();

    [ObservableProperty]
    private ObservableCollection<PluginInfo> filteredPlugins = new();

    [ObservableProperty]
    private PluginInfo? selectedPlugin;

    [ObservableProperty]
    private string? searchQuery;

    // IsLoading, ErrorMessage, and StatusMessage are inherited from BaseViewModel
    // No need to duplicate them here (would cause CS0108 shadowing warnings)

    [ObservableProperty]
    private bool showEnabledOnly;

    public PluginManagementViewModel(IViewModelContext context)
        : base(context)
    {
      try
      {
        _pluginManager = AppServices.GetPluginManager();
        _pluginBridge = AppServices.TryGetPluginBridgeService();
        
        // Subscribe to bridge events for real-time updates
        if (_pluginBridge != null)
        {
          _pluginBridge.PluginStateChanged += OnPluginStateChanged;
          _pluginBridge.SyncError += OnSyncError;
        }
      }
      catch
      {
        _pluginManager = null;
        _pluginBridge = null;
      }

      LoadPluginsCommand = new AsyncRelayCommand(async (ct) => await LoadPluginsAsync(ct));
      RefreshPluginsCommand = new AsyncRelayCommand(async (ct) => await RefreshPluginsAsync(ct));
      EnablePluginCommand = new AsyncRelayCommand<PluginInfo>(EnablePluginAsync, CanModifyPlugin);
      DisablePluginCommand = new AsyncRelayCommand<PluginInfo>(DisablePluginAsync, CanModifyPlugin);
      ReloadPluginCommand = new AsyncRelayCommand<PluginInfo>(ReloadPluginAsync, CanModifyPlugin);

      _ = LoadPluginsAsync(CancellationToken.None);
    }
    
    private void OnPluginStateChanged(object? sender, PluginStateChangedEventArgs e)
    {
      // Update UI on state change from WebSocket
      Dispatcher.TryEnqueue(() =>
      {
        if (e.WasRemoved)
        {
          var toRemove = Plugins.FirstOrDefault(p => p.Name == e.PluginId);
          if (toRemove != null)
          {
            Plugins.Remove(toRemove);
            ApplyFilters();
          }
        }
        else if (e.NewState != null)
        {
          var existing = Plugins.FirstOrDefault(p => p.Name == e.PluginId);
          if (existing != null)
          {
            existing.IsEnabled = e.NewState.State == PluginState.Active;
            existing.Status = e.NewState.StatusDescription;
            existing.ErrorMessage = e.NewState.ErrorMessage;
            ApplyFilters();
          }
        }
      });
    }
    
    private void OnSyncError(object? sender, PluginSyncErrorEventArgs e)
    {
      Dispatcher.TryEnqueue(() =>
      {
        ErrorMessage = e.Error;
      });
    }

    public IAsyncRelayCommand LoadPluginsCommand { get; }
    public IAsyncRelayCommand RefreshPluginsCommand { get; }
    public IAsyncRelayCommand<PluginInfo> EnablePluginCommand { get; }
    public IAsyncRelayCommand<PluginInfo> DisablePluginCommand { get; }
    public IAsyncRelayCommand<PluginInfo> ReloadPluginCommand { get; }

    private async Task LoadPluginsAsync(CancellationToken cancellationToken)
    {
      if (_pluginManager == null && _pluginBridge == null)
      {
        ErrorMessage = ResourceHelper.GetString("PluginManagement.PluginManagerNotAvailable", "Plugin Manager is not available");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;
      StatusMessage = ResourceHelper.GetString("PluginManagement.LoadingPlugins", "Loading plugins...");

      try
      {
        // Use bridge service if available for synchronized state
        if (_pluginBridge != null)
        {
          // Request full sync from backend to populate state
          await _pluginBridge.RequestFullSyncAsync(cancellationToken);
          cancellationToken.ThrowIfCancellationRequested();
          UpdatePluginListFromSyncState();
        }
        else if (_pluginManager != null)
        {
          // Fallback to direct plugin manager
          await _pluginManager.LoadPluginsAsync();
          cancellationToken.ThrowIfCancellationRequested();
          UpdatePluginListFromManager();
        }

        StatusMessage = ResourceHelper.FormatString("PluginManagement.PluginsLoaded", Plugins.Count);
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadPlugins");
        StatusMessage = null;
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task RefreshPluginsAsync(CancellationToken cancellationToken)
    {
      if (_pluginBridge != null)
      {
        await _pluginBridge.RequestFullSyncAsync(cancellationToken);
      }
      await LoadPluginsAsync(cancellationToken);
      StatusMessage = ResourceHelper.GetString("PluginManagement.PluginsRefreshed", "Plugins refreshed");
    }

    /// <summary>
    /// Updates the plugin list from the bridge service's synchronized state.
    /// </summary>
    private void UpdatePluginListFromSyncState()
    {
      if (_pluginBridge == null)
        return;

      Plugins.Clear();
      foreach (var kvp in _pluginBridge.GetAllPluginStatuses())
      {
        var syncState = kvp.Value;
        Plugins.Add(new PluginInfo
        {
          Name = syncState.PluginId,
          Version = syncState.Version,
          Author = string.Empty, // Not available in sync state
          Description = string.Empty, // Not available in sync state
          IsEnabled = syncState.State == PluginState.Active,
          IsInitialized = syncState.BackendLoaded || syncState.FrontendLoaded,
          Status = syncState.StatusDescription,
          ErrorMessage = syncState.ErrorMessage,
          IsSynchronized = true, // Considered synchronized if from bridge service
          BackendLoaded = syncState.BackendLoaded,
          FrontendLoaded = syncState.FrontendLoaded
        });
      }

      ApplyFilters();
    }

    /// <summary>
    /// Fallback: updates plugin list from local PluginManager only.
    /// </summary>
    private void UpdatePluginListFromManager()
    {
      if (_pluginManager == null)
        return;

      Plugins.Clear();
      foreach (var plugin in _pluginManager.Plugins)
      {
        Plugins.Add(new PluginInfo
        {
          Name = plugin.Name,
          Version = plugin.Version,
          Author = plugin.Author,
          Description = plugin.Description,
          IsEnabled = true,
          IsInitialized = plugin.IsInitialized,
          Status = plugin.IsInitialized
                ? ResourceHelper.GetString("PluginManagement.PluginStatusInitialized", "Initialized")
                : ResourceHelper.GetString("PluginManagement.PluginStatusNotInitialized", "Not Initialized")
        });
      }

      ApplyFilters();
    }

    private async Task EnablePluginAsync(PluginInfo? plugin)
    {
      if (plugin == null)
        return;

      try
      {
        if (_pluginBridge != null)
        {
          var result = await _pluginBridge.EnablePluginAsync(plugin.Name);
          if (!result.Success)
          {
            ErrorMessage = result.Message ?? "Failed to enable plugin";
            return;
          }
        }
        
        plugin.IsEnabled = true;
        StatusMessage = ResourceHelper.FormatString("PluginManagement.PluginEnabled", plugin.Name);
        ApplyFilters();
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("PluginManagement.EnablePluginFailed", ex.Message);
      }
    }

    private async Task DisablePluginAsync(PluginInfo? plugin)
    {
      if (plugin == null)
        return;

      try
      {
        if (_pluginBridge != null)
        {
          var result = await _pluginBridge.DisablePluginAsync(plugin.Name);
          if (!result.Success)
          {
            ErrorMessage = result.Message ?? "Failed to disable plugin";
            return;
          }
        }
        
        plugin.IsEnabled = false;
        StatusMessage = ResourceHelper.FormatString("PluginManagement.PluginDisabled", plugin.Name);
        ApplyFilters();
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("PluginManagement.DisablePluginFailed", ex.Message);
      }
    }

    private async Task ReloadPluginAsync(PluginInfo? plugin)
    {
      if (plugin == null)
        return;

      try
      {
        StatusMessage = ResourceHelper.FormatString("PluginManagement.ReloadingPlugin", plugin.Name);
        
        if (_pluginBridge != null)
        {
          var result = await _pluginBridge.ReloadPluginAsync(plugin.Name);
          if (!result.Success)
          {
            ErrorMessage = result.Message ?? "Failed to reload plugin";
            return;
          }
        }
        
        await RefreshPluginsAsync(CancellationToken.None);
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("PluginManagement.ReloadPluginFailed", ex.Message);
      }
    }

    private bool CanModifyPlugin(PluginInfo? plugin)
    {
      return plugin != null && !IsLoading;
    }

    partial void OnSearchQueryChanged(string? value)
    {
      ApplyFilters();
    }

    partial void OnShowEnabledOnlyChanged(bool value)
    {
      ApplyFilters();
    }

    partial void OnPluginsChanged(ObservableCollection<PluginInfo> value)
    {
      OnPropertyChanged(nameof(HasPlugins));
      ApplyFilters();
    }

    partial void OnFilteredPluginsChanged(ObservableCollection<PluginInfo> value)
    {
      OnPropertyChanged(nameof(HasFilteredPlugins));
    }

    private void ApplyFilters()
    {
      FilteredPlugins.Clear();

      var filtered = Plugins.AsEnumerable();

      if (ShowEnabledOnly)
      {
        filtered = filtered.Where(p => p.IsEnabled);
      }

      if (!string.IsNullOrWhiteSpace(SearchQuery))
      {
        var searchLower = SearchQuery.ToLowerInvariant();
        filtered = filtered.Where(p =>
            p.Name.ToLowerInvariant().Contains(searchLower) ||
            p.Author.ToLowerInvariant().Contains(searchLower) ||
            p.Description.ToLowerInvariant().Contains(searchLower) ||
            p.Version.ToLowerInvariant().Contains(searchLower)
        );
      }

      foreach (var plugin in filtered)
      {
        FilteredPlugins.Add(plugin);
      }

      OnPropertyChanged(nameof(HasFilteredPlugins));
    }

    public bool HasPlugins => Plugins.Count > 0;
    public bool HasFilteredPlugins => FilteredPlugins.Count > 0;
  }

  /// <summary>
  /// Plugin information model for UI binding.
  /// Phase 1: Extended with synchronization state properties.
  /// </summary>
  public class PluginInfo : ObservableObject
  {
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public bool IsInitialized { get; set; }
    public string Status { get; set; } = ResourceHelper.GetString("PluginManagement.PluginStatusUnknown", "Unknown");
    public string? ErrorMessage { get; set; }
    
    // Phase 1: Synchronization state properties
    public bool IsSynchronized { get; set; } = true;
    public bool BackendLoaded { get; set; }
    public bool FrontendLoaded { get; set; }
  }
}