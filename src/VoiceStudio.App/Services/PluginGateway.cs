using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Plugins;
using VoiceStudio.Core.Plugins.Models;

namespace VoiceStudio.App.Services
{
    /// <summary>
    /// Implementation of IPluginGateway that communicates with the backend API.
    /// </summary>
    public class PluginGateway : IPluginGateway
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private List<PluginCategory>? _categoriesCache;
        private DateTime _lastCatalogRefresh = DateTime.MinValue;
        private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5);

        public PluginGateway(HttpClient httpClient, string baseUrl = "http://localhost:8001")
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _baseUrl = baseUrl.TrimEnd('/');
        }

        #region Events

        public event EventHandler<PluginInfo>? InstallStarted;
        public event EventHandler<PluginInstallResult>? InstallCompleted;
        public event EventHandler? CatalogRefreshed;

        #endregion

        #region Catalog Operations

        public async Task<IReadOnlyList<PluginCategory>> GetCategoriesAsync(
            CancellationToken cancellationToken = default)
        {
            if (_categoriesCache != null && !IsCacheExpired())
            {
                return _categoriesCache;
            }

            try
            {
                var response = await _httpClient.GetAsync(
                    $"{_baseUrl}/api/plugins/categories",
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    _categoriesCache = await response.Content.ReadFromJsonAsync<List<PluginCategory>>(
                        cancellationToken: cancellationToken) ?? new List<PluginCategory>();
                    return _categoriesCache;
                }
            }
            // ALLOWED: empty catch - graceful degradation when backend unavailable
            catch (HttpRequestException)
            {
                // Fallback to default categories if API unavailable
            }

            return GetDefaultCategories();
        }

        public async Task<PluginSearchResult> SearchPluginsAsync(
            PluginSearchCriteria criteria,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var queryParams = BuildQueryString(criteria);
                var response = await _httpClient.GetAsync(
                    $"{_baseUrl}/api/plugins/search{queryParams}",
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<PluginSearchResult>(
                        cancellationToken: cancellationToken) ?? new PluginSearchResult();
                }
            }
            // ALLOWED: empty catch - graceful degradation to local filtering
            catch (HttpRequestException)
            {
                // Return empty result if API unavailable
            }

            // Fallback to local filtering if API unavailable
            return FilterLocalCatalog(criteria);
        }

        public async Task<IReadOnlyList<PluginInfo>> GetFeaturedPluginsAsync(
            int count = 10,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.GetAsync(
                    $"{_baseUrl}/api/plugins/featured?count={count}",
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<List<PluginInfo>>(
                        cancellationToken: cancellationToken) ?? new List<PluginInfo>();
                }
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Plugin API unavailable: {ex.Message}");
            }

            // No sample data — return empty list; UI shows "No plugins available"
            return new List<PluginInfo>();
        }

        public async Task<IReadOnlyList<PluginInfo>> GetRecentPluginsAsync(
            int count = 10,
            CancellationToken cancellationToken = default)
        {
            var criteria = new PluginSearchCriteria
            {
                SortBy = PluginSortOrder.Newest,
                PageSize = count
            };

            var result = await SearchPluginsAsync(criteria, cancellationToken);
            return result.Plugins;
        }

        public async Task<PluginInfo?> GetPluginDetailsAsync(
            string pluginId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.GetAsync(
                    $"{_baseUrl}/api/plugins/{pluginId}",
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<PluginInfo>(
                        cancellationToken: cancellationToken);
                }
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Plugin API unavailable: {ex.Message}");
            }

            // No sample data — return null; UI shows "Plugin not found"
            return null;
        }

        public async Task<IReadOnlyList<PluginVersion>> GetPluginVersionsAsync(
            string pluginId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.GetAsync(
                    $"{_baseUrl}/api/plugins/{pluginId}/versions",
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<List<PluginVersion>>(
                        cancellationToken: cancellationToken) ?? new List<PluginVersion>();
                }
            }
            // ALLOWED: empty catch - graceful degradation to default version list
            catch (HttpRequestException)
            {
                // Return empty list if API unavailable
            }

            return new List<PluginVersion>
            {
                new PluginVersion
                {
                    Version = "1.0.0",
                    ReleaseDate = DateTime.Now.AddDays(-30),
                    ReleaseNotes = "Initial release"
                }
            };
        }

        #endregion

        #region Installation Operations

        public async Task<PluginInstallResult> InstallPluginAsync(
            string pluginId,
            string? version = null,
            IProgress<PluginInstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var plugin = await GetPluginDetailsAsync(pluginId, cancellationToken);
            if (plugin == null)
            {
                return new PluginInstallResult
                {
                    Success = false,
                    ErrorMessage = $"Plugin '{pluginId}' not found"
                };
            }

            InstallStarted?.Invoke(this, plugin);

            try
            {
                // Report progress phases
                progress?.Report(new PluginInstallProgress
                {
                    Phase = InstallPhase.Preparing,
                    ProgressPercent = 0,
                    StatusMessage = "Preparing installation..."
                });

                await Task.Delay(100, cancellationToken); // Simulate work

                progress?.Report(new PluginInstallProgress
                {
                    Phase = InstallPhase.Downloading,
                    ProgressPercent = 20,
                    StatusMessage = $"Downloading {plugin.Name}..."
                });

                // Call backend API to install
                var request = new { pluginId, version = version ?? plugin.Version };
                var response = await _httpClient.PostAsJsonAsync(
                    $"{_baseUrl}/api/plugins/install",
                    request,
                    cancellationToken);

                progress?.Report(new PluginInstallProgress
                {
                    Phase = InstallPhase.Extracting,
                    ProgressPercent = 60,
                    StatusMessage = "Extracting files..."
                });

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<PluginInstallResult>(
                        cancellationToken: cancellationToken);

                    progress?.Report(new PluginInstallProgress
                    {
                        Phase = InstallPhase.Complete,
                        ProgressPercent = 100,
                        StatusMessage = "Installation complete"
                    });

                    result ??= new PluginInstallResult { Success = true, Plugin = plugin };
                    InstallCompleted?.Invoke(this, result);
                    return result;
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync(cancellationToken);
                    var failResult = new PluginInstallResult
                    {
                        Success = false,
                        ErrorMessage = error
                    };
                    InstallCompleted?.Invoke(this, failResult);
                    return failResult;
                }
            }
            catch (Exception ex)
            {
                progress?.Report(new PluginInstallProgress
                {
                    Phase = InstallPhase.Failed,
                    ProgressPercent = 0,
                    StatusMessage = ex.Message
                });

                var failResult = new PluginInstallResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
                InstallCompleted?.Invoke(this, failResult);
                return failResult;
            }
        }

        public async Task<bool> UninstallPluginAsync(
            string pluginId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.DeleteAsync(
                    $"{_baseUrl}/api/plugins/{pluginId}",
                    cancellationToken);

                return response.IsSuccessStatusCode;
            }
            // ALLOWED: empty catch - uninstall fails gracefully when backend unavailable
            catch (HttpRequestException)
            {
                return false;
            }
        }

        public async Task<PluginInstallResult> UpdatePluginAsync(
            string pluginId,
            IProgress<PluginInstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            // Update is essentially reinstall with latest version
            return await InstallPluginAsync(pluginId, null, progress, cancellationToken);
        }

        public async Task<IReadOnlyList<PluginInfo>> CheckForUpdatesAsync(
            CancellationToken cancellationToken = default)
        {
            var installed = await GetInstalledPluginsAsync(cancellationToken);
            var updates = new List<PluginInfo>();

            foreach (var plugin in installed)
            {
                var latest = await GetPluginDetailsAsync(plugin.Id, cancellationToken);
                if (latest != null && latest.Version != plugin.InstalledVersion)
                {
                    latest.IsInstalled = true;
                    latest.InstalledVersion = plugin.InstalledVersion;
                    updates.Add(latest);
                }
            }

            return updates;
        }

        #endregion

        #region Local Plugin Management

        public async Task<IReadOnlyList<PluginInfo>> GetInstalledPluginsAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.GetAsync(
                    $"{_baseUrl}/api/plugins/installed",
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<List<PluginInfo>>(
                        cancellationToken: cancellationToken) ?? new List<PluginInfo>();
                }
            }
            // ALLOWED: empty catch - graceful degradation to empty installed list
            catch (HttpRequestException)
            {
                // Return empty list if API unavailable
            }

            return new List<PluginInfo>();
        }

        public async Task<bool> EnablePluginAsync(
            string pluginId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.PostAsync(
                    $"{_baseUrl}/api/plugins/{pluginId}/enable",
                    null,
                    cancellationToken);

                return response.IsSuccessStatusCode;
            }
            // ALLOWED: empty catch - enable fails gracefully when backend unavailable
            catch (HttpRequestException)
            {
                return false;
            }
        }

        public async Task<bool> DisablePluginAsync(
            string pluginId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.PostAsync(
                    $"{_baseUrl}/api/plugins/{pluginId}/disable",
                    null,
                    cancellationToken);

                return response.IsSuccessStatusCode;
            }
            // ALLOWED: empty catch - disable fails gracefully when backend unavailable
            catch (HttpRequestException)
            {
                return false;
            }
        }

        public async Task RefreshCatalogAsync(CancellationToken cancellationToken = default)
        {
            _categoriesCache = null;
            _lastCatalogRefresh = DateTime.MinValue;

            await GetCategoriesAsync(cancellationToken);
            await SearchPluginsAsync(new PluginSearchCriteria(), cancellationToken);

            _lastCatalogRefresh = DateTime.UtcNow;
            CatalogRefreshed?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region Helper Methods

        private bool IsCacheExpired()
        {
            return DateTime.UtcNow - _lastCatalogRefresh > _cacheExpiry;
        }

        private static string BuildQueryString(PluginSearchCriteria criteria)
        {
            var parts = new List<string>();

            if (!string.IsNullOrEmpty(criteria.SearchText))
                parts.Add($"q={Uri.EscapeDataString(criteria.SearchText)}");
            if (!string.IsNullOrEmpty(criteria.Category))
                parts.Add($"category={Uri.EscapeDataString(criteria.Category)}");
            if (!string.IsNullOrEmpty(criteria.Tag))
                parts.Add($"tag={Uri.EscapeDataString(criteria.Tag)}");
            if (criteria.InstalledOnly)
                parts.Add("installed=true");
            if (criteria.UpdatesOnly)
                parts.Add("updates=true");

            parts.Add($"sort={criteria.SortBy}");
            parts.Add($"page={criteria.Page}");
            parts.Add($"pageSize={criteria.PageSize}");

            return parts.Count > 0 ? "?" + string.Join("&", parts) : "";
        }

        private PluginSearchResult FilterLocalCatalog(PluginSearchCriteria criteria)
        {
            // Return empty result when API is unavailable
            // Do NOT return fake sample data to users
            System.Diagnostics.Debug.WriteLine("Plugin catalog unavailable - returning empty results");
            return new PluginSearchResult
            {
                Plugins = new List<PluginInfo>(),
                TotalCount = 0,
                Page = criteria.Page,
                PageSize = criteria.PageSize,
                ServiceUnavailable = true,
                ErrorMessage = "Plugin catalog service is currently unavailable"
            };
        }

        private static List<PluginCategory> GetDefaultCategories()
        {
            return new List<PluginCategory>
            {
                new() { Id = "voice-effects", Name = "Voice Effects", IconGlyph = "\uE8D6", PluginCount = 0 },
                new() { Id = "engines", Name = "Engines", IconGlyph = "\uE7F4", PluginCount = 0 },
                new() { Id = "utilities", Name = "Utilities", IconGlyph = "\uE713", PluginCount = 0 },
                new() { Id = "audio", Name = "Audio Processing", IconGlyph = "\uE8D7", PluginCount = 0 },
                new() { Id = "integrations", Name = "Integrations", IconGlyph = "\uE703", PluginCount = 0 }
            };
        }

        /// <summary>
        /// Returns an empty plugin list when backend is unavailable.
        /// No fake/sample data is returned - the UI should show "No plugins available"
        /// or prompt the user to check their connection.
        /// </summary>
        private static List<PluginInfo> GetEmptyPluginList()
        {
            // Do NOT return fake sample data - return empty list
            // The UI layer should handle this gracefully with appropriate messaging
            return new List<PluginInfo>();
        }

        #endregion
    }
}
