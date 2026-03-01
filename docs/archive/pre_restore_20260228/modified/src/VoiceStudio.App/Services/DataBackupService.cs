using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Services
{
    /// <summary>
    /// Provides automatic and manual data backup capabilities.
    /// Backs up user data including projects, settings, and presets.
    /// </summary>
    public class DataBackupService : IDisposable
    {
        private readonly string _backupDirectory;
        private readonly string _userDataDirectory;
        private readonly string _settingsFilePath;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly Timer? _autoBackupTimer;
        private readonly object _backupLock = new();
        
        private bool _disposed;
        private bool _backupInProgress;
        private BackupSettings _settings;
        
        // Default values
        private const int DefaultBackupIntervalHours = 24;
        private const int DefaultMaxBackups = 10;
        
        public event EventHandler<BackupCompletedEventArgs>? BackupCompleted;
        public event EventHandler<BackupFailedEventArgs>? BackupFailed;
        
        public DataBackupService()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _backupDirectory = Path.Combine(appDataPath, "VoiceStudio", "Backups");
            _userDataDirectory = Path.Combine(appDataPath, "VoiceStudio");
            _settingsFilePath = Path.Combine(appDataPath, "VoiceStudio", "backup_settings.json");
            
            Directory.CreateDirectory(_backupDirectory);
            
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            
            _settings = LoadSettings();
            
            // Start auto-backup timer if enabled
            if (IsAutoBackupEnabled)
            {
                var intervalMs = GetBackupIntervalHours() * 60 * 60 * 1000;
                _autoBackupTimer = new Timer(
                    OnAutoBackupTimer,
                    null,
                    intervalMs,
                    intervalMs);
            }
        }
        
        /// <summary>
        /// Gets whether automatic backups are enabled.
        /// </summary>
        public bool IsAutoBackupEnabled => _settings.AutoBackupEnabled;
        
        /// <summary>
        /// Gets the backup interval in hours.
        /// </summary>
        public int GetBackupIntervalHours() => 
            _settings.BackupIntervalHours > 0 ? _settings.BackupIntervalHours : DefaultBackupIntervalHours;
        
        /// <summary>
        /// Gets the maximum number of backups to retain.
        /// </summary>
        public int GetMaxBackups() => 
            _settings.MaxBackups > 0 ? _settings.MaxBackups : DefaultMaxBackups;
        
        /// <summary>
        /// Gets the last backup time.
        /// </summary>
        public DateTime? GetLastBackupTime() => _settings.LastBackupTime;
        
        /// <summary>
        /// Enables or disables automatic backups.
        /// </summary>
        public void SetAutoBackupEnabled(bool enabled)
        {
            _settings.AutoBackupEnabled = enabled;
            SaveSettings();
        }
        
        /// <summary>
        /// Creates a backup of user data.
        /// </summary>
        public async Task<BackupResult> CreateBackupAsync(
            string? description = null,
            CancellationToken cancellationToken = default)
        {
            if (_backupInProgress)
            {
                return new BackupResult
                {
                    Success = false,
                    ErrorMessage = "Backup already in progress"
                };
            }
            
            lock (_backupLock)
            {
                if (_backupInProgress)
                {
                    return new BackupResult
                    {
                        Success = false,
                        ErrorMessage = "Backup already in progress"
                    };
                }
                _backupInProgress = true;
            }
            
            try
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backupName = $"backup_{timestamp}.zip";
                var backupPath = Path.Combine(_backupDirectory, backupName);
                
                // Create manifest
                var manifest = new BackupManifest
                {
                    CreatedAt = DateTime.UtcNow,
                    Description = description ?? "Manual backup",
                    AppVersion = GetAppVersion(),
                    Items = new List<BackupItem>()
                };
                
                // Create zip archive
                await Task.Run(() =>
                {
                    using var zipFile = ZipFile.Open(backupPath, ZipArchiveMode.Create);
                    
                    // Backup settings
                    var settingsPath = Path.Combine(_userDataDirectory, "settings");
                    if (Directory.Exists(settingsPath))
                    {
                        AddDirectoryToZip(zipFile, settingsPath, "settings", manifest);
                    }
                    
                    // Backup presets
                    var presetsPath = Path.Combine(_userDataDirectory, "presets");
                    if (Directory.Exists(presetsPath))
                    {
                        AddDirectoryToZip(zipFile, presetsPath, "presets", manifest);
                    }
                    
                    // Backup voices
                    var voicesPath = Path.Combine(_userDataDirectory, "voices");
                    if (Directory.Exists(voicesPath))
                    {
                        AddDirectoryToZip(zipFile, voicesPath, "voices", manifest);
                    }
                    
                    // Add manifest
                    var manifestJson = System.Text.Json.JsonSerializer.Serialize(manifest);
                    var manifestEntry = zipFile.CreateEntry("manifest.json");
                    using var writer = new StreamWriter(manifestEntry.Open());
                    writer.Write(manifestJson);
                    
                }, cancellationToken);
                
                // Update last backup time
                SaveSettings();
                
                // Clean up old backups
                await CleanupOldBackupsAsync();
                
                var result = new BackupResult
                {
                    Success = true,
                    BackupPath = backupPath,
                    BackupName = backupName,
                    SizeBytes = new FileInfo(backupPath).Length,
                    ItemCount = manifest.Items.Count
                };
                
                BackupCompleted?.Invoke(this, new BackupCompletedEventArgs(result));
                ErrorLogger.LogInfo($"[DataBackup] Backup completed: {backupName}", "DataBackupService");
                
                return result;
            }
            catch (Exception ex)
            {
                var result = new BackupResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
                
                BackupFailed?.Invoke(this, new BackupFailedEventArgs(ex.Message));
                ErrorLogger.LogError($"Backup failed: {ex.Message}", "DataBackup");
                
                return result;
            }
            finally
            {
                _backupInProgress = false;
            }
        }
        
        /// <summary>
        /// Restores data from a backup.
        /// </summary>
        public async Task<RestoreResult> RestoreBackupAsync(
            string backupPath,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (!File.Exists(backupPath))
                {
                    return new RestoreResult
                    {
                        Success = false,
                        ErrorMessage = "Backup file not found"
                    };
                }
                
                var itemsRestored = 0;
                
                await Task.Run(() =>
                {
                    using var zipFile = ZipFile.OpenRead(backupPath);
                    
                    foreach (var entry in zipFile.Entries)
                    {
                        if (entry.Name == "manifest.json")
                            continue;
                            
                        var targetPath = Path.Combine(_userDataDirectory, entry.FullName);
                        var targetDir = Path.GetDirectoryName(targetPath);
                        
                        if (!string.IsNullOrEmpty(targetDir))
                        {
                            Directory.CreateDirectory(targetDir);
                        }
                        
                        entry.ExtractToFile(targetPath, overwrite: true);
                        itemsRestored++;
                    }
                }, cancellationToken);
                
                ErrorLogger.LogInfo($"[DataBackup] Restore completed: {itemsRestored} items", "DataBackupService");
                
                return new RestoreResult
                {
                    Success = true,
                    ItemsRestored = itemsRestored
                };
            }
            catch (Exception ex)
            {
                ErrorLogger.LogError($"Restore failed: {ex.Message}", "DataBackup");
                
                return new RestoreResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }
        
        /// <summary>
        /// Gets a list of available backups.
        /// </summary>
        public IEnumerable<LocalBackupInfo> GetAvailableBackups()
        {
            var backups = new List<LocalBackupInfo>();
            
            if (!Directory.Exists(_backupDirectory))
                return backups;
            
            foreach (var file in Directory.GetFiles(_backupDirectory, "backup_*.zip")
                .OrderByDescending(f => new FileInfo(f).CreationTime))
            {
                var fileInfo = new FileInfo(file);
                backups.Add(new LocalBackupInfo
                {
                    Path = file,
                    Name = fileInfo.Name,
                    CreatedAt = fileInfo.CreationTime,
                    SizeBytes = fileInfo.Length
                });
            }
            
            return backups;
        }
        
        /// <summary>
        /// Deletes a backup file.
        /// </summary>
        public bool DeleteBackup(string backupPath)
        {
            try
            {
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
        
        private void AddDirectoryToZip(
            ZipArchive zip, 
            string sourceDir, 
            string entryPrefix,
            BackupManifest manifest)
        {
            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceDir, file);
                var entryName = Path.Combine(entryPrefix, relativePath).Replace('\\', '/');
                
                zip.CreateEntryFromFile(file, entryName);
                
                manifest.Items.Add(new BackupItem
                {
                    Path = entryName,
                    SizeBytes = new FileInfo(file).Length
                });
            }
        }
        
        private async Task CleanupOldBackupsAsync()
        {
            var maxBackups = GetMaxBackups();
            var backups = GetAvailableBackups().ToList();
            
            if (backups.Count <= maxBackups)
                return;
            
            var toDelete = backups.Skip(maxBackups);
            
            await Task.Run(() =>
            {
                foreach (var backup in toDelete)
                {
                    try
                    {
                        File.Delete(backup.Path);
                        ErrorLogger.LogDebug($"[DataBackup] Deleted old backup: {backup.Name}", "DataBackupService");
                    }
                    // ALLOWED: empty catch - Best effort cleanup, failure is acceptable
                    catch
                    {
                    }
                }
            });
        }
        
        private void OnAutoBackupTimer(object? state)
        {
            _ = CreateBackupAsync("Automatic backup");
        }
        
        private string GetAppVersion()
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            return assembly.GetName().Version?.ToString() ?? "Unknown";
        }
        
        private BackupSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    return JsonSerializer.Deserialize<BackupSettings>(json, _jsonOptions) 
                        ?? new BackupSettings();
                }
            }
            catch (Exception ex)
            {
                ErrorLogger.LogWarning($"Failed to load backup settings: {ex.Message}", "DataBackup");
            }
            return new BackupSettings();
        }
        
        private void SaveSettings()
        {
            try
            {
                _settings.LastBackupTime = DateTime.UtcNow;
                var json = JsonSerializer.Serialize(_settings, _jsonOptions);
                File.WriteAllText(_settingsFilePath, json);
            }
            catch (Exception ex)
            {
                ErrorLogger.LogWarning($"Failed to save backup settings: {ex.Message}", "DataBackup");
            }
        }
        
        public void Dispose()
        {
            if (_disposed)
                return;
            
            _disposed = true;
            _autoBackupTimer?.Dispose();
        }
    }
    
    public class BackupSettings
    {
        public bool AutoBackupEnabled { get; set; }
        public int BackupIntervalHours { get; set; } = 24;
        public int MaxBackups { get; set; } = 10;
        public DateTime? LastBackupTime { get; set; }
    }
    
    public class BackupManifest
    {
        public DateTime CreatedAt { get; set; }
        public string Description { get; set; } = string.Empty;
        public string AppVersion { get; set; } = string.Empty;
        public List<BackupItem> Items { get; set; } = new();
    }
    
    public class BackupItem
    {
        public string Path { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
    }
    
    public class BackupResult
    {
        public bool Success { get; set; }
        public string? BackupPath { get; set; }
        public string? BackupName { get; set; }
        public long SizeBytes { get; set; }
        public int ItemCount { get; set; }
        public string? ErrorMessage { get; set; }
    }
    
    public class RestoreResult
    {
        public bool Success { get; set; }
        public int ItemsRestored { get; set; }
        public string? ErrorMessage { get; set; }
    }
    
    /// <summary>
    /// Local backup file metadata (distinct from VoiceStudio.Core.Models.BackupInfo used for backend sync).
    /// </summary>
    public class LocalBackupInfo
    {
        public string Path { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public long SizeBytes { get; set; }
    }
    
    public class BackupCompletedEventArgs : EventArgs
    {
        public BackupResult Result { get; }
        
        public BackupCompletedEventArgs(BackupResult result)
        {
            Result = result;
        }
    }
    
    public class BackupFailedEventArgs : EventArgs
    {
        public string ErrorMessage { get; }
        
        public BackupFailedEventArgs(string message)
        {
            ErrorMessage = message;
        }
    }
}
