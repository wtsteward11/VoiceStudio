using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Plugins;
using VoiceStudio.Core.Services;
using CoreModels = VoiceStudio.Core.Plugins.Models;

namespace VoiceStudio.App.ViewModels
{
    /// <summary>
    /// ViewModel for the Plugin Gallery panel.
    /// Handles catalog browsing, searching, filtering, and plugin installation.
    /// </summary>
    public partial class PluginGalleryViewModel : BaseViewModel
    {
        private readonly IPluginGateway _gateway;
        private CancellationTokenSource? _searchCts;
        private CancellationTokenSource? _installCts;

        public PluginGalleryViewModel(IViewModelContext context, IPluginGateway gateway)
            : base(context)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            
            // Subscribe to gateway events
            _gateway.InstallStarted += OnInstallStarted;
            _gateway.InstallCompleted += OnInstallCompleted;
            _gateway.CatalogRefreshed += OnCatalogRefreshed;
        }

        #region Observable Properties

        [ObservableProperty]
        private ObservableCollection<CoreModels.PluginInfo> _plugins = new();

        [ObservableProperty]
        private ObservableCollection<CoreModels.PluginCategory> _categories = new();

        [ObservableProperty]
        private ObservableCollection<CoreModels.PluginInfo> _featuredPlugins = new();

        [ObservableProperty]
        private CoreModels.PluginInfo? _selectedPlugin;

        [ObservableProperty]
        private CoreModels.PluginCategory? _selectedCategory;

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private CoreModels.PluginSortOrder _sortOrder = CoreModels.PluginSortOrder.Popular;

        [ObservableProperty]
        private bool _showInstalledOnly;

        [ObservableProperty]
        private bool _showUpdatesOnly;

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private bool _isInstalling;

        [ObservableProperty]
        private double _installProgress;

        [ObservableProperty]
        private string _installStatusText = string.Empty;

        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        private string _errorMessage = string.Empty;

        [ObservableProperty]
        private int _currentPage = 1;

        [ObservableProperty]
        private int _totalPages = 1;

        [ObservableProperty]
        private int _totalPlugins;

        // GAP-B18: NotifyCanExecuteChangedFor enables proper button state updates via Command binding
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
        private bool _hasNextPage;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
        private bool _hasPreviousPage;

        #endregion

        #region Initialization

        /// <summary>
        /// Initialize the gallery by loading categories and featured plugins.
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                IsLoading = true;
                ErrorMessage = string.Empty;

                // Load categories
                var categories = await _gateway.GetCategoriesAsync();
                Categories.Clear();
                
                // Add "All" category
                Categories.Add(new CoreModels.PluginCategory 
                { 
                    Id = "all", 
                    Name = "All", 
                    Description = "All plugins" 
                });
                
                foreach (var cat in categories)
                {
                    Categories.Add(cat);
                }

                // Load featured plugins
                await LoadFeaturedPluginsAsync();

                // Load initial catalog
                await SearchAsync();

                StatusMessage = $"Loaded {TotalPlugins} plugins";
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to initialize: {ex.Message}";
                StatusMessage = "Error loading plugins";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadFeaturedPluginsAsync()
        {
            var criteria = new CoreModels.PluginSearchCriteria
            {
                SortBy = CoreModels.PluginSortOrder.Popular,
                PageSize = 4
            };

            var result = await _gateway.SearchPluginsAsync(criteria);
            FeaturedPlugins.Clear();
            foreach (var plugin in result.Plugins.Take(4))
            {
                FeaturedPlugins.Add(plugin);
            }
        }

        #endregion

        #region Search and Filtering

        partial void OnSearchTextChanged(string value)
        {
            // Debounce search
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            
            _ = Task.Delay(300, _searchCts.Token)
                .ContinueWith(async _ => 
                {
                    await SearchAsync();
                }, TaskContinuationOptions.OnlyOnRanToCompletion);
        }

        partial void OnSelectedCategoryChanged(CoreModels.PluginCategory? value)
        {
            CurrentPage = 1;
            _ = SearchAsync();
        }

        partial void OnSortOrderChanged(CoreModels.PluginSortOrder value)
        {
            CurrentPage = 1;
            _ = SearchAsync();
        }

        partial void OnShowInstalledOnlyChanged(bool value)
        {
            CurrentPage = 1;
            _ = SearchAsync();
        }

        partial void OnShowUpdatesOnlyChanged(bool value)
        {
            CurrentPage = 1;
            _ = SearchAsync();
        }

        private async Task SearchAsync()
        {
            try
            {
                IsLoading = true;

                var criteria = new CoreModels.PluginSearchCriteria
                {
                    SearchText = SearchText,
                    Category = SelectedCategory?.Id != "all" ? SelectedCategory?.Id : null,
                    SortBy = SortOrder,
                    InstalledOnly = ShowInstalledOnly,
                    UpdatesOnly = ShowUpdatesOnly,
                    Page = CurrentPage,
                    PageSize = 20
                };

                var result = await _gateway.SearchPluginsAsync(criteria);
                
                Plugins.Clear();
                foreach (var plugin in result.Plugins)
                {
                    Plugins.Add(plugin);
                }

                TotalPlugins = result.TotalCount;
                TotalPages = result.TotalPages;
                HasNextPage = CurrentPage < TotalPages;
                HasPreviousPage = CurrentPage > 1;

                StatusMessage = $"Showing {Plugins.Count} of {TotalPlugins} plugins";
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Search failed: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private void ClearFilters()
        {
            SearchText = string.Empty;
            SelectedCategory = Categories.FirstOrDefault();
            SortOrder = CoreModels.PluginSortOrder.Popular;
            ShowInstalledOnly = false;
            ShowUpdatesOnly = false;
            CurrentPage = 1;
        }

        #endregion

        #region Pagination

        // GAP-B18: Added CanExecute for proper button state management via Command binding
        [RelayCommand(CanExecute = nameof(CanNextPage))]
        private async Task NextPageAsync()
        {
            if (HasNextPage)
            {
                CurrentPage++;
                await SearchAsync();
            }
        }

        private bool CanNextPage() => HasNextPage;

        // GAP-B18: Added CanExecute for proper button state management via Command binding
        [RelayCommand(CanExecute = nameof(CanPreviousPage))]
        private async Task PreviousPageAsync()
        {
            if (HasPreviousPage)
            {
                CurrentPage--;
                await SearchAsync();
            }
        }

        private bool CanPreviousPage() => HasPreviousPage;

        #endregion

        #region Plugin Installation

        [RelayCommand]
        private async Task InstallPluginAsync(CoreModels.PluginInfo plugin)
        {
            if (plugin == null) return;

            try
            {
                _installCts?.Cancel();
                _installCts = new CancellationTokenSource();
                
                IsInstalling = true;
                InstallProgress = 0;
                InstallStatusText = $"Installing {plugin.Name}...";

                var progress = new Progress<CoreModels.PluginInstallProgress>(p =>
                {
                    InstallProgress = p.ProgressPercent;
                    InstallStatusText = p.StatusMessage;
                });

                var result = await _gateway.InstallPluginAsync(plugin.Id, progress: progress, cancellationToken: _installCts.Token);

                if (result.Success)
                {
                    StatusMessage = $"Successfully installed {plugin.Name}";
                    
                    // Update plugin state in list
                    var existingPlugin = Plugins.FirstOrDefault(p => p.Id == plugin.Id);
                    if (existingPlugin != null)
                    {
                        existingPlugin.IsInstalled = true;
                        existingPlugin.InstalledVersion = result.Plugin?.Version;
                    }
                }
                else
                {
                    ErrorMessage = $"Installation failed: {result.ErrorMessage}";
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Installation cancelled";
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Installation failed: {ex.Message}";
            }
            finally
            {
                IsInstalling = false;
            }
        }

        [RelayCommand]
        private void CancelInstall()
        {
            _installCts?.Cancel();
            IsInstalling = false;
            InstallStatusText = "Cancelling...";
        }

        [RelayCommand]
        private async Task UninstallPluginAsync(CoreModels.PluginInfo plugin)
        {
            if (plugin == null) return;

            try
            {
                IsInstalling = true;
                InstallStatusText = $"Uninstalling {plugin.Name}...";

                var success = await _gateway.UninstallPluginAsync(plugin.Id);

                if (success)
                {
                    StatusMessage = $"Successfully uninstalled {plugin.Name}";
                    
                    // Update plugin state in list
                    var existingPlugin = Plugins.FirstOrDefault(p => p.Id == plugin.Id);
                    if (existingPlugin != null)
                    {
                        existingPlugin.IsInstalled = false;
                        existingPlugin.InstalledVersion = null;
                    }
                }
                else
                {
                    ErrorMessage = $"Failed to uninstall {plugin.Name}";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Uninstall failed: {ex.Message}";
            }
            finally
            {
                IsInstalling = false;
            }
        }

        #endregion

        #region Refresh and Updates

        [RelayCommand]
        private async Task RefreshAsync()
        {
            await InitializeAsync();
        }

        [RelayCommand]
        private async Task CheckForUpdatesAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Checking for updates...";

                var updates = await _gateway.CheckForUpdatesAsync();
                
                if (updates.Count > 0)
                {
                    StatusMessage = $"Found {updates.Count} plugin update(s)";
                    
                    // Mark plugins with updates by updating their Version to the latest available
                    // HasUpdate is computed from IsInstalled && InstalledVersion != Version
                    foreach (var update in updates)
                    {
                        var plugin = Plugins.FirstOrDefault(p => p.Id == update.Id);
                        if (plugin != null && plugin.IsInstalled)
                        {
                            // Update the available version (which should be newer than installed)
                            plugin.Version = update.Version;
                        }
                    }
                }
                else
                {
                    StatusMessage = "All plugins are up to date";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to check for updates: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private void ViewPluginDetails(CoreModels.PluginInfo plugin)
        {
            SelectedPlugin = plugin;
            // Navigation to detail view would be handled by the view
        }

        #endregion

        #region Event Handlers

        private void OnInstallStarted(object? sender, CoreModels.PluginInfo e)
        {
            System.Diagnostics.Debug.WriteLine($"[PluginGallery] Install started: {e.Name}");
        }

        private void OnInstallCompleted(object? sender, CoreModels.PluginInstallResult e)
        {
            System.Diagnostics.Debug.WriteLine($"[PluginGallery] Install completed: {e.Success}");
        }

        private void OnCatalogRefreshed(object? sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[PluginGallery] Catalog refreshed");
        }

        #endregion

        #region Cleanup

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _searchCts?.Cancel();
                _searchCts?.Dispose();
                _installCts?.Cancel();
                _installCts?.Dispose();
                
                _gateway.InstallStarted -= OnInstallStarted;
                _gateway.InstallCompleted -= OnInstallCompleted;
                _gateway.CatalogRefreshed -= OnCatalogRefreshed;
            }
            
            base.Dispose(disposing);
        }

        #endregion
    }
}
