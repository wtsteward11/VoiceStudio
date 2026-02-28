using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Plugins.Models;

namespace VoiceStudio.Core.Plugins
{
    /// <summary>
    /// Gateway interface for plugin catalog and installation operations.
    /// Provides methods to browse, search, install, and manage plugins.
    /// </summary>
    public interface IPluginGateway
    {
        #region Catalog Operations

        /// <summary>
        /// Get all available plugin categories.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>List of plugin categories.</returns>
        Task<IReadOnlyList<PluginCategory>> GetCategoriesAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Search the plugin catalog with specified criteria.
        /// </summary>
        /// <param name="criteria">Search and filter criteria.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Paginated search results.</returns>
        Task<PluginSearchResult> SearchPluginsAsync(
            PluginSearchCriteria criteria,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Get featured/recommended plugins.
        /// </summary>
        /// <param name="count">Maximum number of plugins to return.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>List of featured plugins.</returns>
        Task<IReadOnlyList<PluginInfo>> GetFeaturedPluginsAsync(
            int count = 10,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Get recently added plugins.
        /// </summary>
        /// <param name="count">Maximum number of plugins to return.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>List of recent plugins.</returns>
        Task<IReadOnlyList<PluginInfo>> GetRecentPluginsAsync(
            int count = 10,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Get detailed information about a specific plugin.
        /// </summary>
        /// <param name="pluginId">Plugin identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Detailed plugin information, or null if not found.</returns>
        Task<PluginInfo?> GetPluginDetailsAsync(
            string pluginId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Get version history for a plugin.
        /// </summary>
        /// <param name="pluginId">Plugin identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>List of version entries.</returns>
        Task<IReadOnlyList<PluginVersion>> GetPluginVersionsAsync(
            string pluginId,
            CancellationToken cancellationToken = default);

        #endregion

        #region Installation Operations

        /// <summary>
        /// Install a plugin.
        /// </summary>
        /// <param name="pluginId">Plugin identifier.</param>
        /// <param name="version">Specific version to install, or null for latest.</param>
        /// <param name="progress">Progress reporter.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Installation result.</returns>
        Task<PluginInstallResult> InstallPluginAsync(
            string pluginId,
            string? version = null,
            IProgress<PluginInstallProgress>? progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Uninstall a plugin.
        /// </summary>
        /// <param name="pluginId">Plugin identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if uninstalled successfully.</returns>
        Task<bool> UninstallPluginAsync(
            string pluginId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Update a plugin to the latest version.
        /// </summary>
        /// <param name="pluginId">Plugin identifier.</param>
        /// <param name="progress">Progress reporter.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Update result.</returns>
        Task<PluginInstallResult> UpdatePluginAsync(
            string pluginId,
            IProgress<PluginInstallProgress>? progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Check for updates to installed plugins.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>List of plugins with available updates.</returns>
        Task<IReadOnlyList<PluginInfo>> CheckForUpdatesAsync(
            CancellationToken cancellationToken = default);

        #endregion

        #region Local Plugin Management

        /// <summary>
        /// Get all installed plugins.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>List of installed plugins.</returns>
        Task<IReadOnlyList<PluginInfo>> GetInstalledPluginsAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Enable a disabled plugin.
        /// </summary>
        /// <param name="pluginId">Plugin identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if enabled successfully.</returns>
        Task<bool> EnablePluginAsync(
            string pluginId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Disable an enabled plugin without uninstalling.
        /// </summary>
        /// <param name="pluginId">Plugin identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if disabled successfully.</returns>
        Task<bool> DisablePluginAsync(
            string pluginId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Refresh the plugin catalog from remote source.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task RefreshCatalogAsync(CancellationToken cancellationToken = default);

        #endregion

        #region Events

        /// <summary>
        /// Raised when a plugin installation starts.
        /// </summary>
        event EventHandler<PluginInfo>? InstallStarted;

        /// <summary>
        /// Raised when a plugin installation completes.
        /// </summary>
        event EventHandler<PluginInstallResult>? InstallCompleted;

        /// <summary>
        /// Raised when the catalog is refreshed.
        /// </summary>
        event EventHandler? CatalogRefreshed;

        #endregion
    }
}
