using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Plugins;
using VoiceStudio.Core.Plugins.Models;

namespace VoiceStudio.App.Tests.Mocks
{
    /// <summary>
    /// Mock implementation of IPluginGateway for unit testing.
    /// </summary>
    public class MockPluginGateway : IPluginGateway
    {
        private readonly List<PluginInfo> _catalog;
        private readonly List<PluginInfo> _installed;
        private readonly List<PluginCategory> _categories;

        public MockPluginGateway()
        {
            _categories = CreateDefaultCategories();
            _catalog = CreateSamplePlugins();
            _installed = new List<PluginInfo>();
        }

        #region Test Configuration

        /// <summary>
        /// Configure the mock to simulate API failures.
        /// </summary>
        public bool SimulateApiFailure { get; set; }

        /// <summary>
        /// Alias for SimulateApiFailure for test convenience.
        /// </summary>
        public bool ShouldFail
        {
            get => SimulateApiFailure;
            set => SimulateApiFailure = value;
        }

        /// <summary>
        /// Configure the mock to simulate slow responses.
        /// </summary>
        public int SimulatedDelayMs { get; set; }

        /// <summary>
        /// Configure the mock to fail installation.
        /// </summary>
        public bool SimulateInstallFailure { get; set; }

        /// <summary>
        /// Alias for SimulateInstallFailure for test convenience.
        /// </summary>
        public bool ShouldFailInstall
        {
            get => SimulateInstallFailure;
            set => SimulateInstallFailure = value;
        }

        /// <summary>
        /// Get the number of times SearchPluginsAsync was called.
        /// </summary>
        public int SearchCallCount { get; private set; }

        /// <summary>
        /// Get the number of times InstallPluginAsync was called.
        /// </summary>
        public int InstallCallCount { get; private set; }

        /// <summary>
        /// Add a plugin to the mock catalog.
        /// </summary>
        public void AddToCatalog(PluginInfo plugin)
        {
            _catalog.Add(plugin);
        }

        /// <summary>
        /// Clear the mock catalog.
        /// </summary>
        public void ClearCatalog()
        {
            _catalog.Clear();
        }

        /// <summary>
        /// Mark a plugin as installed.
        /// </summary>
        public void MarkAsInstalled(string pluginId, string version)
        {
            var plugin = _catalog.Find(p => p.Id == pluginId);
            if (plugin != null)
            {
                plugin.IsInstalled = true;
                plugin.InstalledVersion = version;
                _installed.Add(plugin);
            }
        }

        #endregion

        #region Events

        public event EventHandler<PluginInfo>? InstallStarted;
        public event EventHandler<PluginInstallResult>? InstallCompleted;
        public event EventHandler? CatalogRefreshed;

        #endregion

        #region IPluginGateway Implementation

        public async Task<IReadOnlyList<PluginCategory>> GetCategoriesAsync(
            CancellationToken cancellationToken = default)
        {
            await SimulateDelay(cancellationToken);
            
            if (SimulateApiFailure)
                throw new Exception("Simulated API failure");

            return _categories;
        }

        public async Task<PluginSearchResult> SearchPluginsAsync(
            PluginSearchCriteria criteria,
            CancellationToken cancellationToken = default)
        {
            SearchCallCount++;
            await SimulateDelay(cancellationToken);
            
            if (SimulateApiFailure)
                throw new Exception("Simulated API failure");

            var query = _catalog.AsEnumerable();

            // Apply filters
            if (!string.IsNullOrEmpty(criteria.SearchText))
            {
                var searchLower = criteria.SearchText.ToLower();
                query = query.Where(p =>
                    p.Name.ToLower().Contains(searchLower) ||
                    p.Description.ToLower().Contains(searchLower) ||
                    p.Tags.Any(t => t.ToLower().Contains(searchLower)));
            }

            if (!string.IsNullOrEmpty(criteria.Category))
            {
                query = query.Where(p => p.Category == criteria.Category);
            }

            if (criteria.InstalledOnly)
            {
                query = query.Where(p => p.IsInstalled);
            }

            if (criteria.UpdatesOnly)
            {
                query = query.Where(p => p.HasUpdate);
            }

            // Apply sorting
            query = criteria.SortBy switch
            {
                PluginSortOrder.Popular => query.OrderByDescending(p => p.DownloadCount),
                PluginSortOrder.Rating => query.OrderByDescending(p => p.Rating),
                PluginSortOrder.RecentlyUpdated => query.OrderByDescending(p => p.LastUpdated),
                PluginSortOrder.Name => query.OrderBy(p => p.Name),
                PluginSortOrder.Newest => query.OrderByDescending(p => p.PublishedDate),
                _ => query
            };

            // Pagination
            var total = query.Count();
            var plugins = query
                .Skip((criteria.Page - 1) * criteria.PageSize)
                .Take(criteria.PageSize)
                .ToList();

            return new PluginSearchResult
            {
                Plugins = plugins,
                TotalCount = total,
                Page = criteria.Page,
                PageSize = criteria.PageSize
            };
        }

        public async Task<IReadOnlyList<PluginInfo>> GetFeaturedPluginsAsync(
            int count = 10,
            CancellationToken cancellationToken = default)
        {
            await SimulateDelay(cancellationToken);
            
            if (SimulateApiFailure)
                throw new Exception("Simulated API failure");

            return _catalog
                .Where(p => p.IsVerified)
                .OrderByDescending(p => p.Rating)
                .Take(count)
                .ToList();
        }

        public async Task<IReadOnlyList<PluginInfo>> GetRecentPluginsAsync(
            int count = 10,
            CancellationToken cancellationToken = default)
        {
            await SimulateDelay(cancellationToken);
            
            return _catalog
                .OrderByDescending(p => p.PublishedDate)
                .Take(count)
                .ToList();
        }

        public async Task<PluginInfo?> GetPluginDetailsAsync(
            string pluginId,
            CancellationToken cancellationToken = default)
        {
            await SimulateDelay(cancellationToken);
            
            if (SimulateApiFailure)
                throw new Exception("Simulated API failure");

            return _catalog.Find(p => p.Id == pluginId);
        }

        public async Task<IReadOnlyList<PluginVersion>> GetPluginVersionsAsync(
            string pluginId,
            CancellationToken cancellationToken = default)
        {
            await SimulateDelay(cancellationToken);

            return new List<PluginVersion>
            {
                new() { Version = "1.0.0", ReleaseDate = DateTime.Now.AddDays(-30), ReleaseNotes = "Initial release" },
                new() { Version = "1.1.0", ReleaseDate = DateTime.Now.AddDays(-15), ReleaseNotes = "Bug fixes" },
                new() { Version = "1.2.0", ReleaseDate = DateTime.Now, ReleaseNotes = "New features" }
            };
        }

        public async Task<PluginInstallResult> InstallPluginAsync(
            string pluginId,
            string? version = null,
            IProgress<PluginInstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            InstallCallCount++;
            
            var plugin = _catalog.Find(p => p.Id == pluginId);
            if (plugin == null)
            {
                return new PluginInstallResult
                {
                    Success = false,
                    ErrorMessage = "Plugin not found"
                };
            }

            InstallStarted?.Invoke(this, plugin);

            // Simulate progress
            for (int i = 0; i <= 100; i += 20)
            {
                progress?.Report(new PluginInstallProgress
                {
                    Phase = i < 50 ? InstallPhase.Downloading : InstallPhase.Extracting,
                    ProgressPercent = i,
                    StatusMessage = $"Progress: {i}%"
                });
                await Task.Delay(10, cancellationToken);
            }

            if (SimulateInstallFailure)
            {
                var failResult = new PluginInstallResult
                {
                    Success = false,
                    ErrorMessage = "Simulated installation failure"
                };
                InstallCompleted?.Invoke(this, failResult);
                return failResult;
            }

            plugin.IsInstalled = true;
            plugin.InstalledVersion = version ?? plugin.Version;
            _installed.Add(plugin);

            var result = new PluginInstallResult
            {
                Success = true,
                Plugin = plugin,
                RequiresRestart = false
            };

            InstallCompleted?.Invoke(this, result);
            return result;
        }

        public async Task<bool> UninstallPluginAsync(
            string pluginId,
            CancellationToken cancellationToken = default)
        {
            await SimulateDelay(cancellationToken);

            var plugin = _catalog.Find(p => p.Id == pluginId);
            if (plugin != null)
            {
                plugin.IsInstalled = false;
                plugin.InstalledVersion = null;
                _installed.RemoveAll(p => p.Id == pluginId);
                return true;
            }

            return false;
        }

        public async Task<PluginInstallResult> UpdatePluginAsync(
            string pluginId,
            IProgress<PluginInstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return await InstallPluginAsync(pluginId, null, progress, cancellationToken);
        }

        public async Task<IReadOnlyList<PluginInfo>> CheckForUpdatesAsync(
            CancellationToken cancellationToken = default)
        {
            await SimulateDelay(cancellationToken);
            return _installed.Where(p => p.HasUpdate).ToList();
        }

        public async Task<IReadOnlyList<PluginInfo>> GetInstalledPluginsAsync(
            CancellationToken cancellationToken = default)
        {
            await SimulateDelay(cancellationToken);
            return _installed;
        }

        public async Task<bool> EnablePluginAsync(
            string pluginId,
            CancellationToken cancellationToken = default)
        {
            await SimulateDelay(cancellationToken);
            return _installed.Any(p => p.Id == pluginId);
        }

        public async Task<bool> DisablePluginAsync(
            string pluginId,
            CancellationToken cancellationToken = default)
        {
            await SimulateDelay(cancellationToken);
            return _installed.Any(p => p.Id == pluginId);
        }

        public async Task RefreshCatalogAsync(CancellationToken cancellationToken = default)
        {
            await SimulateDelay(cancellationToken);
            CatalogRefreshed?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region Helper Methods

        private async Task SimulateDelay(CancellationToken ct)
        {
            if (SimulatedDelayMs > 0)
            {
                await Task.Delay(SimulatedDelayMs, ct);
            }
        }

        private static List<PluginCategory> CreateDefaultCategories()
        {
            return new List<PluginCategory>
            {
                new() { Id = "voice-effects", Name = "Voice Effects", PluginCount = 3 },
                new() { Id = "engines", Name = "Engines", PluginCount = 2 },
                new() { Id = "utilities", Name = "Utilities", PluginCount = 2 }
            };
        }

        private static List<PluginInfo> CreateSamplePlugins()
        {
            return new List<PluginInfo>
            {
                new()
                {
                    Id = "reverb-pro",
                    Name = "Reverb Pro",
                    Description = "Professional reverb effects",
                    Author = "VoiceStudio Team",
                    Category = "Voice Effects",
                    Version = "1.2.0",
                    Rating = 4.5,
                    RatingCount = 128,
                    DownloadCount = 1500,
                    IsVerified = true,
                    Tags = new List<string> { "reverb", "effects" },
                    PublishedDate = DateTime.Now.AddDays(-60)
                },
                new()
                {
                    Id = "whisper-engine",
                    Name = "Whisper Integration",
                    Description = "OpenAI Whisper for transcription",
                    Author = "VoiceStudio Team",
                    Category = "Engines",
                    Version = "2.0.0",
                    Rating = 4.8,
                    RatingCount = 256,
                    DownloadCount = 3200,
                    IsVerified = true,
                    Tags = new List<string> { "transcription", "whisper" },
                    PublishedDate = DateTime.Now.AddDays(-30)
                },
                new()
                {
                    Id = "batch-processor",
                    Name = "Batch Processor",
                    Description = "Process multiple files at once",
                    Author = "Community",
                    Category = "Utilities",
                    Version = "1.0.5",
                    Rating = 4.2,
                    RatingCount = 64,
                    DownloadCount = 800,
                    IsVerified = false,
                    Tags = new List<string> { "batch", "automation" },
                    PublishedDate = DateTime.Now.AddDays(-15)
                }
            };
        }

        #endregion
    }
}
