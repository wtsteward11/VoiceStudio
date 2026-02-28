using System;
using System.Collections.Generic;

namespace VoiceStudio.Core.Plugins.Models
{
    /// <summary>
    /// Represents a plugin available in the gallery.
    /// </summary>
    public class PluginInfo
    {
        /// <summary>
        /// Unique identifier for the plugin.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Display name of the plugin.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Short description of the plugin's functionality.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Full detailed description with markdown support.
        /// </summary>
        public string? LongDescription { get; set; }

        /// <summary>
        /// Current version of the plugin.
        /// </summary>
        public string Version { get; set; } = "1.0.0";

        /// <summary>
        /// Plugin author or publisher.
        /// </summary>
        public string Author { get; set; } = string.Empty;

        /// <summary>
        /// URL to the plugin's icon image.
        /// </summary>
        public string? IconUrl { get; set; }

        /// <summary>
        /// Category of the plugin (e.g., "Voice Effects", "Engines", "Utilities").
        /// </summary>
        public string Category { get; set; } = "Uncategorized";

        /// <summary>
        /// Tags for search and filtering.
        /// </summary>
        public List<string> Tags { get; set; } = new();

        /// <summary>
        /// Minimum VoiceStudio version required.
        /// </summary>
        public string? MinimumAppVersion { get; set; }

        /// <summary>
        /// Plugin license type (e.g., "MIT", "GPL-3.0", "Proprietary").
        /// </summary>
        public string? License { get; set; }

        /// <summary>
        /// URL to the plugin's homepage or repository.
        /// </summary>
        public string? HomepageUrl { get; set; }

        /// <summary>
        /// URL to download the plugin package.
        /// </summary>
        public string? DownloadUrl { get; set; }

        /// <summary>
        /// Size of the plugin download in bytes.
        /// </summary>
        public long? DownloadSize { get; set; }

        /// <summary>
        /// Number of downloads or installs.
        /// </summary>
        public int DownloadCount { get; set; }

        /// <summary>
        /// Average rating (0-5 scale).
        /// </summary>
        public double Rating { get; set; }

        /// <summary>
        /// Number of ratings.
        /// </summary>
        public int RatingCount { get; set; }

        /// <summary>
        /// Date when the plugin was published.
        /// </summary>
        public DateTime? PublishedDate { get; set; }

        /// <summary>
        /// Date when the plugin was last updated.
        /// </summary>
        public DateTime? LastUpdated { get; set; }

        /// <summary>
        /// Whether the plugin is verified/trusted.
        /// </summary>
        public bool IsVerified { get; set; }

        /// <summary>
        /// Whether the plugin is currently installed locally.
        /// </summary>
        public bool IsInstalled { get; set; }

        /// <summary>
        /// The installed version if different from latest.
        /// </summary>
        public string? InstalledVersion { get; set; }

        /// <summary>
        /// Whether an update is available.
        /// </summary>
        public bool HasUpdate => IsInstalled && InstalledVersion != Version;

        /// <summary>
        /// List of plugin dependencies.
        /// </summary>
        public List<PluginDependency> Dependencies { get; set; } = new();

        /// <summary>
        /// Screenshots or preview images.
        /// </summary>
        public List<string> Screenshots { get; set; } = new();
    }

    /// <summary>
    /// Represents a plugin dependency.
    /// </summary>
    public class PluginDependency
    {
        /// <summary>
        /// Plugin ID of the dependency.
        /// </summary>
        public string PluginId { get; set; } = string.Empty;

        /// <summary>
        /// Minimum required version.
        /// </summary>
        public string? MinVersion { get; set; }

        /// <summary>
        /// Whether this dependency is optional.
        /// </summary>
        public bool IsOptional { get; set; }
    }

    /// <summary>
    /// Represents a version entry in the plugin's history.
    /// </summary>
    public class PluginVersion
    {
        /// <summary>
        /// Version number.
        /// </summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// Release date.
        /// </summary>
        public DateTime ReleaseDate { get; set; }

        /// <summary>
        /// Changelog/release notes for this version.
        /// </summary>
        public string? ReleaseNotes { get; set; }

        /// <summary>
        /// Download URL for this specific version.
        /// </summary>
        public string? DownloadUrl { get; set; }

        /// <summary>
        /// Download size in bytes.
        /// </summary>
        public long? DownloadSize { get; set; }
    }

    /// <summary>
    /// Represents a plugin category for filtering.
    /// </summary>
    public class PluginCategory
    {
        /// <summary>
        /// Category identifier.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Display name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Category description.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Icon for the category.
        /// </summary>
        public string? IconGlyph { get; set; }

        /// <summary>
        /// Number of plugins in this category.
        /// </summary>
        public int PluginCount { get; set; }
    }

    /// <summary>
    /// Result of a plugin installation operation.
    /// </summary>
    public class PluginInstallResult
    {
        /// <summary>
        /// Whether the installation succeeded.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// The installed plugin info.
        /// </summary>
        public PluginInfo? Plugin { get; set; }

        /// <summary>
        /// Error message if installation failed.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Whether a restart is required.
        /// </summary>
        public bool RequiresRestart { get; set; }

        /// <summary>
        /// Dependencies that were also installed.
        /// </summary>
        public List<string> InstalledDependencies { get; set; } = new();
    }

    /// <summary>
    /// Progress information for plugin installation.
    /// </summary>
    public class PluginInstallProgress
    {
        /// <summary>
        /// Current phase of installation.
        /// </summary>
        public InstallPhase Phase { get; set; }

        /// <summary>
        /// Progress percentage (0-100).
        /// </summary>
        public int ProgressPercent { get; set; }

        /// <summary>
        /// Description of current activity.
        /// </summary>
        public string StatusMessage { get; set; } = string.Empty;

        /// <summary>
        /// Bytes downloaded so far.
        /// </summary>
        public long BytesDownloaded { get; set; }

        /// <summary>
        /// Total bytes to download.
        /// </summary>
        public long TotalBytes { get; set; }
    }

    /// <summary>
    /// Phases of plugin installation.
    /// </summary>
    public enum InstallPhase
    {
        /// <summary>
        /// Preparing for installation.
        /// </summary>
        Preparing = 0,

        /// <summary>
        /// Downloading the plugin package.
        /// </summary>
        Downloading = 1,

        /// <summary>
        /// Verifying the download.
        /// </summary>
        Verifying = 2,

        /// <summary>
        /// Extracting plugin files.
        /// </summary>
        Extracting = 3,

        /// <summary>
        /// Installing dependencies.
        /// </summary>
        InstallingDependencies = 4,

        /// <summary>
        /// Running post-install scripts.
        /// </summary>
        PostInstall = 5,

        /// <summary>
        /// Installation complete.
        /// </summary>
        Complete = 6,

        /// <summary>
        /// Installation failed.
        /// </summary>
        Failed = 7
    }

    /// <summary>
    /// Search/filter criteria for plugin catalog.
    /// </summary>
    public class PluginSearchCriteria
    {
        /// <summary>
        /// Text to search for in name, description, tags.
        /// </summary>
        public string? SearchText { get; set; }

        /// <summary>
        /// Filter by category.
        /// </summary>
        public string? Category { get; set; }

        /// <summary>
        /// Filter by tag.
        /// </summary>
        public string? Tag { get; set; }

        /// <summary>
        /// Only show installed plugins.
        /// </summary>
        public bool InstalledOnly { get; set; }

        /// <summary>
        /// Only show plugins with updates.
        /// </summary>
        public bool UpdatesOnly { get; set; }

        /// <summary>
        /// Sort order.
        /// </summary>
        public PluginSortOrder SortBy { get; set; } = PluginSortOrder.Popular;

        /// <summary>
        /// Page number for pagination.
        /// </summary>
        public int Page { get; set; } = 1;

        /// <summary>
        /// Page size for pagination.
        /// </summary>
        public int PageSize { get; set; } = 20;
    }

    /// <summary>
    /// Sort order options for plugin catalog.
    /// </summary>
    public enum PluginSortOrder
    {
        /// <summary>
        /// Most popular (by downloads).
        /// </summary>
        Popular = 0,

        /// <summary>
        /// Highest rated.
        /// </summary>
        Rating = 1,

        /// <summary>
        /// Recently updated.
        /// </summary>
        RecentlyUpdated = 2,

        /// <summary>
        /// Alphabetical by name.
        /// </summary>
        Name = 3,

        /// <summary>
        /// Recently added to catalog.
        /// </summary>
        Newest = 4
    }

    /// <summary>
    /// Paginated result set for plugin searches.
    /// </summary>
    public class PluginSearchResult
    {
        /// <summary>
        /// The plugins matching the search.
        /// </summary>
        public List<PluginInfo> Plugins { get; set; } = new();

        /// <summary>
        /// Total number of matching plugins.
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// Current page number.
        /// </summary>
        public int Page { get; set; }

        /// <summary>
        /// Page size.
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// Total number of pages.
        /// </summary>
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

        /// <summary>
        /// Whether there are more pages.
        /// </summary>
        public bool HasMore => Page < TotalPages;

        /// <summary>
        /// Indicates if the plugin service is unavailable.
        /// When true, the results are empty due to service unavailability, not lack of matches.
        /// </summary>
        public bool ServiceUnavailable { get; set; }

        /// <summary>
        /// Error message when service is unavailable.
        /// </summary>
        public string? ErrorMessage { get; set; }
    }
}
