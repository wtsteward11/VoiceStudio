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
  /// </summary>
  public partial class PluginManagementViewModel : BaseViewModel, IPanelView
  {
    private readonly PluginManager? _pluginManager;

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
      }
      catch
      {
        _pluginManager = null;
      }

      LoadPluginsCommand = new AsyncRelayCommand(async (ct) => await LoadPluginsAsync(ct));
      RefreshPluginsCommand = new AsyncRelayCommand(async (ct) => await RefreshPluginsAsync(ct));
      EnablePluginCommand = new RelayCommand<PluginInfo>(EnablePlugin, CanModifyPlugin);
      DisablePluginCommand = new RelayCommand<PluginInfo>(DisablePlugin, CanModifyPlugin);
      ReloadPluginCommand = new RelayCommand<PluginInfo>(ReloadPlugin, CanModifyPlugin);

      _ = LoadPluginsAsync(CancellationToken.None);
    }

    public IAsyncRelayCommand LoadPluginsCommand { get; }
    public IAsyncRelayCommand RefreshPluginsCommand { get; }
    public IRelayCommand<PluginInfo> EnablePluginCommand { get; }
    public IRelayCommand<PluginInfo> DisablePluginCommand { get; }
    public IRelayCommand<PluginInfo> ReloadPluginCommand { get; }

    private async Task LoadPluginsAsync(CancellationToken cancellationToken)
    {
      if (_pluginManager == null)
      {
        ErrorMessage = ResourceHelper.GetString("PluginManagement.PluginManagerNotAvailable", "Plugin Manager is not available");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;
      StatusMessage = ResourceHelper.GetString("PluginManagement.LoadingPlugins", "Loading plugins...");

      try
      {
        // Check if PluginManager.LoadPluginsAsync accepts CancellationToken
        // Since SettingsViewModel uses it with cancellationToken, we'll try to pass it
        // If it doesn't compile, we'll need to update PluginManager first
        await _pluginManager.LoadPluginsAsync();
        cancellationToken.ThrowIfCancellationRequested();

        UpdatePluginList();

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
      await LoadPluginsAsync(cancellationToken);
      StatusMessage = ResourceHelper.GetString("PluginManagement.PluginsRefreshed", "Plugins refreshed");
    }

    private void UpdatePluginList()
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

    private void EnablePlugin(PluginInfo? plugin)
    {
      if (plugin == null)
        return;

      try
      {
        plugin.IsEnabled = true;
        StatusMessage = ResourceHelper.FormatString("PluginManagement.PluginEnabled", plugin.Name);
        ApplyFilters();
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("PluginManagement.EnablePluginFailed", ex.Message);
      }
    }

    private void DisablePlugin(PluginInfo? plugin)
    {
      if (plugin == null)
        return;

      try
      {
        plugin.IsEnabled = false;
        StatusMessage = ResourceHelper.FormatString("PluginManagement.PluginDisabled", plugin.Name);
        ApplyFilters();
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("PluginManagement.DisablePluginFailed", ex.Message);
      }
    }

    private void ReloadPlugin(PluginInfo? plugin)
    {
      if (plugin == null)
        return;

      try
      {
        StatusMessage = ResourceHelper.FormatString("PluginManagement.ReloadingPlugin", plugin.Name);
        _ = RefreshPluginsAsync(CancellationToken.None);
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
  }
}