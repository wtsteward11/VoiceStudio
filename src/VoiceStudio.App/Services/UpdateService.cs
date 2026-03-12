using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Service for checking and installing application updates.
  /// Uses GitHub Releases API for update checking.
  /// </summary>
  public class UpdateService : IUpdateService
  {
    private readonly HttpClient _httpClient;
    private readonly UpdateServiceConfig _updateConfig;
    private UpdateHistory? _lastUpdateHistory;
    private const string UpdateConfigFileName = "update_config.json";
    private const string UpdateHistoryFileName = "update_history.json";
    private Version? _latestVersion;
    private bool _isCheckingForUpdates;
    private bool _isDownloadingUpdate;
    private double _downloadProgress;
    private string? _updateDownloadPath;
    private DateTime _lastUpdateCheck = DateTime.MinValue;
    private const int UpdateCheckIntervalMinutes = 60;

    public Version CurrentVersion { get; }
    public Version LatestVersion => _latestVersion ?? CurrentVersion;
    public bool IsUpdateAvailable => _latestVersion != null && _latestVersion > CurrentVersion;
    public bool IsCheckingForUpdates => _isCheckingForUpdates;
    public bool IsDownloadingUpdate => _isDownloadingUpdate;
    public double DownloadProgress => _downloadProgress;
    public string UpdateDownloadPath => _updateDownloadPath ?? string.Empty;

    public event EventHandler<UpdateCheckCompletedEventArgs>? UpdateCheckCompleted;
    public event EventHandler<DownloadProgressEventArgs>? DownloadProgressChanged;
    public event EventHandler<DownloadCompletedEventArgs>? DownloadCompleted;

    public UpdateService(HttpClient? httpClient = null)
    {
      _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
      if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "VoiceStudio-Quantum-Plus/1.0");

      _updateConfig = LoadUpdateConfig();
      _lastUpdateHistory = LoadUpdateHistory();

      // Get current version from assembly
      CurrentVersion = GetCurrentVersion();
    }

    /// <summary>
    /// Gets the current application version from the assembly.
    /// </summary>
    private Version GetCurrentVersion()
    {
      var assembly = System.Reflection.Assembly.GetExecutingAssembly();
      var version = assembly.GetName().Version;
      return version ?? new Version(1, 0, 0, 0);
    }

    private UpdateServiceConfig LoadUpdateConfig()
    {
      var config = new UpdateServiceConfig();

      var configPath = Path.Combine(AppContext.BaseDirectory, UpdateConfigFileName);
      if (File.Exists(configPath))
      {
        try
        {
          var json = File.ReadAllText(configPath);
          var fileConfig = JsonSerializer.Deserialize<UpdateServiceConfig>(json, new JsonSerializerOptions
          {
            PropertyNameCaseInsensitive = true
          });
          if (fileConfig != null)
          {
            config = config.Merge(fileConfig);
          }
        }
        catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "UpdateService.Unknown");
      }
      }

      var envServer = Environment.GetEnvironmentVariable("VOICESTUDIO_UPDATE_SERVER_URL");
      var envOwner = Environment.GetEnvironmentVariable("VOICESTUDIO_UPDATE_REPO_OWNER");
      var envRepo = Environment.GetEnvironmentVariable("VOICESTUDIO_UPDATE_REPO_NAME");

      if (!string.IsNullOrWhiteSpace(envServer))
        config.UpdateServerUrl = envServer.TrimEnd('/');
      if (!string.IsNullOrWhiteSpace(envOwner))
        config.RepositoryOwner = envOwner;
      if (!string.IsNullOrWhiteSpace(envRepo))
        config.RepositoryName = envRepo;

      return config;
    }

    private UpdateHistory? LoadUpdateHistory()
    {
      var historyPath = Path.Combine(GetUpdateDirectory(), UpdateHistoryFileName);
      if (!File.Exists(historyPath))
        return null;

      try
      {
        var json = File.ReadAllText(historyPath);
        return JsonSerializer.Deserialize<UpdateHistory>(json, new JsonSerializerOptions
        {
          PropertyNameCaseInsensitive = true
        });
      }
      catch
      {
        return null;
      }
    }

    private void SaveUpdateHistory(UpdateHistory history)
    {
      try
      {
        var historyPath = Path.Combine(GetUpdateDirectory(), UpdateHistoryFileName);
        var json = JsonSerializer.Serialize(history, new JsonSerializerOptions
        {
          PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
          WriteIndented = true
        });
        File.WriteAllText(historyPath, json);
        _lastUpdateHistory = history;
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "UpdateService.SaveUpdateHistory");
      }
    }

    private static string GetUpdateDirectory()
    {
      // Use Environment.SpecialFolder for unpackaged app compatibility
      // ApplicationData.Current throws InvalidOperationException in unpackaged apps
      var localAppData = Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
          "VoiceStudio");
      var downloadDir = Path.Combine(localAppData, "Updates");
      Directory.CreateDirectory(downloadDir);
      return downloadDir;
    }

    private bool IsConfigValid()
    {
      return _updateConfig.IsValid;
    }

    private string BuildApiUrl(string path)
    {
      return $"{_updateConfig.UpdateServerUrl.TrimEnd('/')}/{path.TrimStart('/')}";
    }

    private async Task<GitHubRelease?> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
      var apiUrl = BuildApiUrl($"repos/{_updateConfig.RepositoryOwner}/{_updateConfig.RepositoryName}/releases/latest");
      var response = await _httpClient.GetAsync(apiUrl, cancellationToken).ConfigureAwait(false);
      if (!response.IsSuccessStatusCode)
      {
        return null;
      }

      var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
      return JsonSerializer.Deserialize<GitHubRelease>(jsonContent, new JsonSerializerOptions
      {
        PropertyNameCaseInsensitive = true
      });
    }

    private async Task<GitHubRelease?> GetReleaseByTagAsync(Version version, CancellationToken cancellationToken)
    {
      var apiUrl = BuildApiUrl($"repos/{_updateConfig.RepositoryOwner}/{_updateConfig.RepositoryName}/releases/tags/v{version}");
      var response = await _httpClient.GetAsync(apiUrl, cancellationToken).ConfigureAwait(false);
      if (!response.IsSuccessStatusCode)
      {
        return null;
      }

      var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
      return JsonSerializer.Deserialize<GitHubRelease>(jsonContent, new JsonSerializerOptions
      {
        PropertyNameCaseInsensitive = true
      });
    }

    private static bool IsInstallerAsset(GitHubAsset asset)
    {
      if (asset == null || string.IsNullOrWhiteSpace(asset.Name))
        return false;

      var name = asset.Name;
      return name.Contains("Setup", StringComparison.OrdinalIgnoreCase)
             || name.Contains("Installer", StringComparison.OrdinalIgnoreCase)
             || name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
             || name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDeltaAsset(GitHubAsset asset)
    {
      if (asset == null || string.IsNullOrWhiteSpace(asset.Name))
        return false;

      var name = asset.Name;
      return name.Contains("delta", StringComparison.OrdinalIgnoreCase)
             || name.Contains("patch", StringComparison.OrdinalIgnoreCase)
             || name.Contains("diff", StringComparison.OrdinalIgnoreCase);
    }

    private static GitHubAsset? SelectUpdateAsset(IReadOnlyList<GitHubAsset> assets)
    {
      var candidates = assets.Where(IsInstallerAsset).ToList();
      if (candidates.Count == 0)
      {
        return null;
      }

      var deltaCandidates = candidates.Where(IsDeltaAsset).ToList();
      var selectionPool = deltaCandidates.Count > 0 ? deltaCandidates : candidates;
      return selectionPool.OrderBy(a => a.Size).FirstOrDefault();
    }

    private static string EnsureFileName(string fileName)
    {
      return string.IsNullOrWhiteSpace(fileName) ? $"VoiceStudio_Update_{DateTime.UtcNow:yyyyMMdd_HHmmss}.exe" : fileName;
    }

    private async Task<string> DownloadAssetAsync(GitHubAsset asset, CancellationToken cancellationToken, Action<double>? progressCallback = null)
    {
      var downloadDir = GetUpdateDirectory();
      var assetFileName = EnsureFileName(asset.Name ?? string.Empty);
      var downloadPath = Path.Combine(downloadDir, assetFileName);

      _updateDownloadPath = downloadPath;

      await using (var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None))
      {
        var downloadResponse = await _httpClient.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        downloadResponse.EnsureSuccessStatusCode();

        var totalBytes = downloadResponse.Content.Headers.ContentLength ?? asset.Size;
        var bytesDownloaded = 0L;
        var stopwatch = Stopwatch.StartNew();

        await using (var contentStream = await downloadResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        {
          var buffer = new byte[8192];
          int bytesRead;

          while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
          {
            await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
            bytesDownloaded += bytesRead;

            if (totalBytes > 0)
            {
              _downloadProgress = (double)bytesDownloaded / totalBytes;
              var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
              var speedMbps = elapsedSeconds > 0 ? bytesDownloaded * 8 / (elapsedSeconds * 1_000_000) : 0;

              progressCallback?.Invoke(_downloadProgress);
              OnDownloadProgressChanged(_downloadProgress, bytesDownloaded, totalBytes, speedMbps);
            }
          }
        }
      }

      return downloadPath;
    }

    public async Task<bool> CheckForUpdatesAsync(bool forceCheck = false, CancellationToken cancellationToken = default)
    {
      // Don't check if recently checked (unless forced)
      if (!forceCheck && _lastUpdateCheck.AddMinutes(UpdateCheckIntervalMinutes) > DateTime.UtcNow)
      {
        return IsUpdateAvailable;
      }

      if (_isCheckingForUpdates)
      {
        return IsUpdateAvailable;
      }

      _isCheckingForUpdates = true;
      _lastUpdateCheck = DateTime.UtcNow;

      try
      {
        if (!IsConfigValid())
        {
          OnUpdateCheckCompleted(false, null, "Update repository not configured. Set VOICESTUDIO_UPDATE_REPO_OWNER and VOICESTUDIO_UPDATE_REPO_NAME.");
          return false;
        }

        // Check GitHub Releases API
        var apiUrl = BuildApiUrl($"repos/{_updateConfig.RepositoryOwner}/{_updateConfig.RepositoryName}/releases/latest");
        var response = await _httpClient.GetAsync(apiUrl, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
          var errorMessage = $"Failed to check for updates: {response.StatusCode}";
          OnUpdateCheckCompleted(false, null, errorMessage);
          return false;
        }

        var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var release = JsonSerializer.Deserialize<GitHubRelease>(jsonContent, new JsonSerializerOptions
        {
          PropertyNameCaseInsensitive = true
        });

        if (release == null || string.IsNullOrEmpty(release.TagName))
        {
          OnUpdateCheckCompleted(false, null, "Invalid release information");
          return false;
        }

        // Parse version from tag (e.g., "v1.2.3" -> Version(1, 2, 3))
        var versionString = release.TagName.TrimStart('v', 'V');
        if (Version.TryParse(versionString, out var latestVersion))
        {
          _latestVersion = latestVersion;
          var updateAvailable = latestVersion > CurrentVersion;
          OnUpdateCheckCompleted(updateAvailable, latestVersion, null);
          return updateAvailable;
        }
        OnUpdateCheckCompleted(false, null, $"Invalid version format: {release.TagName}");

        return false;
      }
      catch (HttpRequestException ex)
      {
        var errorMessage = $"Network error while checking for updates: {ex.Message}";
        OnUpdateCheckCompleted(false, null, errorMessage);
        return false;
      }
      catch (Exception ex)
      {
        var errorMessage = $"Error checking for updates: {ex.Message}";
        OnUpdateCheckCompleted(false, null, errorMessage);
        return false;
      }
      finally
      {
        _isCheckingForUpdates = false;
      }
    }

    public async Task<string> DownloadUpdateAsync(Action<double>? progressCallback = null)
    {
      if (!IsUpdateAvailable || _latestVersion == null)
      {
        throw new InvalidOperationException("No update available to download");
      }

      if (_isDownloadingUpdate)
      {
        throw new InvalidOperationException("Update download already in progress");
      }

      _isDownloadingUpdate = true;
      _downloadProgress = 0.0;

      try
      {
        if (!IsConfigValid())
        {
          throw new InvalidOperationException("Update repository not configured.");
        }

        var release = await GetLatestReleaseAsync(CancellationToken.None).ConfigureAwait(false);
        if (release?.Assets == null || release.Assets.Count == 0)
        {
          throw new Exception("No update assets found in release");
        }

        var installerAsset = SelectUpdateAsset(release.Assets);
        if (installerAsset == null)
        {
          throw new Exception("No installer asset found in release");
        }

        var downloadPath = await DownloadAssetAsync(installerAsset, CancellationToken.None, progressCallback).ConfigureAwait(false);

        OnDownloadCompleted(true, downloadPath, null);
        return downloadPath;
      }
      catch (Exception ex)
      {
        var errorMessage = $"Failed to download update: {ex.Message}";
        OnDownloadCompleted(false, null, errorMessage);
        throw new Exception(errorMessage, ex);
      }
      finally
      {
        _isDownloadingUpdate = false;
      }
    }

    public async Task<bool> VerifyUpdateAsync(string updatePath, string expectedChecksum)
    {
      if (!File.Exists(updatePath))
      {
        return false;
      }

      try
      {
        using (var sha256 = SHA256.Create())
        await using (var fileStream = File.OpenRead(updatePath))
        {
          var hashBytes = await sha256.ComputeHashAsync(fileStream);
          var actualChecksum = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
          var expectedChecksumLower = expectedChecksum.Replace("-", "").Replace(" ", "").ToLowerInvariant();

          return actualChecksum == expectedChecksumLower;
        }
      }
      catch
      {
        return false;
      }
    }

    public async Task<bool> InstallUpdateAsync(string updatePath, bool restartAfterInstall = true)
    {
      if (!File.Exists(updatePath))
      {
        throw new FileNotFoundException("Update file not found", updatePath);
      }

      try
      {
        // Launch installer
        var processStartInfo = new ProcessStartInfo
        {
          FileName = updatePath,
          UseShellExecute = true,
          Verb = "runas" // Run as administrator
        };

        var process = Process.Start(processStartInfo);
        if (process == null)
        {
          return false;
        }

        // Wait a moment to ensure installer started
        await Task.Delay(1000);

        // If restart requested, close application after installer starts
        if (restartAfterInstall)
        {
          // Give installer time to start, then close application
          await Task.Delay(2000);
          Application.Current.Exit();
        }

        return true;
      }
      catch (Exception ex)
      {
        throw new Exception($"Failed to install update: {ex.Message}", ex);
      }
    }

    public async Task<string> GetReleaseNotesAsync(Version version)
    {
      try
      {
        var apiUrl = $"{_updateConfig.UpdateServerUrl}/repos/{_updateConfig.RepositoryOwner}/{_updateConfig.RepositoryName}/releases/tags/v{version}";
        var response = await _httpClient.GetAsync(apiUrl, CancellationToken.None).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
          return $"Release notes not available for version {version}";
        }

        var jsonContent = await response.Content.ReadAsStringAsync(CancellationToken.None).ConfigureAwait(false);
        var release = JsonSerializer.Deserialize<GitHubRelease>(jsonContent, new JsonSerializerOptions
        {
          PropertyNameCaseInsensitive = true
        });

        return release?.Body ?? $"Release notes not available for version {version}";
      }
      catch
      {
        return $"Release notes not available for version {version}";
      }
    }

    private void OnUpdateCheckCompleted(bool updateAvailable, Version? latestVersion, string? errorMessage)
    {
      UpdateCheckCompleted?.Invoke(this, new UpdateCheckCompletedEventArgs
      {
        UpdateAvailable = updateAvailable,
        LatestVersion = latestVersion,
        ErrorMessage = errorMessage
      });
    }

    private void OnDownloadProgressChanged(double progress, long bytesDownloaded, long totalBytes, double speedMbps)
    {
      DownloadProgressChanged?.Invoke(this, new DownloadProgressEventArgs
      {
        Progress = progress,
        BytesDownloaded = bytesDownloaded,
        TotalBytes = totalBytes,
        DownloadSpeedMbps = speedMbps
      });
    }

    private void OnDownloadCompleted(bool success, string? filePath, string? errorMessage)
    {
      DownloadCompleted?.Invoke(this, new DownloadCompletedEventArgs
      {
        Success = success,
        FilePath = filePath,
        ErrorMessage = errorMessage
      });
    }

    public void Dispose()
    {
      // HttpClient is owned by DI; do not dispose
    }
  }

  /// <summary>
  /// GitHub Release API response model.
  /// </summary>
  internal class GitHubRelease
  {
    public string? TagName { get; set; }
    public string? Name { get; set; }
    public string? Body { get; set; }
    public bool Prerelease { get; set; }
    public List<GitHubAsset>? Assets { get; set; }
  }

  /// <summary>
  /// GitHub Release Asset model.
  /// </summary>
  internal class GitHubAsset
  {
    public string? Name { get; set; }
    public string? BrowserDownloadUrl { get; set; }
    public long Size { get; set; }
  }

  /// <summary>
  /// Configuration for the update service.
  /// </summary>
  public class UpdateServiceConfig
  {
    /// <summary>
    /// The base URL for the update server (GitHub API).
    /// </summary>
    public string UpdateServerUrl { get; set; } = "https://api.github.com";

    /// <summary>
    /// The GitHub repository owner.
    /// </summary>
    public string RepositoryOwner { get; set; } = "VoiceStudio";

    /// <summary>
    /// The GitHub repository name.
    /// </summary>
    public string RepositoryName { get; set; } = "VoiceStudio";

    /// <summary>
    /// Indicates whether the configuration is valid for checking updates.
    /// </summary>
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(UpdateServerUrl) &&
        !string.IsNullOrWhiteSpace(RepositoryOwner) &&
        !string.IsNullOrWhiteSpace(RepositoryName);

    /// <summary>
    /// Merges another config into this one, overwriting non-empty values.
    /// </summary>
    public UpdateServiceConfig Merge(UpdateServiceConfig other)
    {
      if (other == null) return this;

      if (!string.IsNullOrWhiteSpace(other.UpdateServerUrl))
        UpdateServerUrl = other.UpdateServerUrl;
      if (!string.IsNullOrWhiteSpace(other.RepositoryOwner))
        RepositoryOwner = other.RepositoryOwner;
      if (!string.IsNullOrWhiteSpace(other.RepositoryName))
        RepositoryName = other.RepositoryName;

      return this;
    }
  }

  /// <summary>
  /// Tracks update history and rollback information.
  /// </summary>
  public class UpdateHistory
  {
    /// <summary>
    /// The version that was installed before the last update.
    /// </summary>
    public string? PreviousVersion { get; set; }

    /// <summary>
    /// The version that was installed by the last update.
    /// </summary>
    public string? InstalledVersion { get; set; }

    /// <summary>
    /// When the last update was installed.
    /// </summary>
    public DateTime? InstalledAt { get; set; }

    /// <summary>
    /// Path to the backup of the previous installation (for rollback).
    /// </summary>
    public string? BackupPath { get; set; }

    /// <summary>
    /// Whether the last update was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if the update failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
  }
}