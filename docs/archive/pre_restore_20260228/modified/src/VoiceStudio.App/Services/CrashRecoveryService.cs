using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Services
{
    /// <summary>
    /// Provides automatic crash recovery with session state save/restore capabilities.
    /// Saves session state periodically and on critical events for recovery after crashes.
    /// </summary>
    public class CrashRecoveryService : IDisposable
    {
        private readonly string _recoveryDirectory;
        private readonly string _sessionFilePath;
        private readonly string _crashMarkerPath;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly Timer? _autoSaveTimer;
        private readonly object _saveLock = new();
        
        private SessionState? _currentState;
        private bool _disposed;
        
        // Auto-save interval (default: 60 seconds)
        private const int AutoSaveIntervalMs = 60000;
        
        public event EventHandler<SessionRecoveredEventArgs>? SessionRecovered;
        public event EventHandler<RecoveryFailedEventArgs>? RecoveryFailed;
        
        public CrashRecoveryService()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _recoveryDirectory = Path.Combine(appDataPath, "VoiceStudio", "Recovery");
            _sessionFilePath = Path.Combine(_recoveryDirectory, "session.json");
            _crashMarkerPath = Path.Combine(_recoveryDirectory, ".crash_marker");
            
            Directory.CreateDirectory(_recoveryDirectory);
            
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            
            // Start auto-save timer
            _autoSaveTimer = new Timer(
                OnAutoSaveTimer, 
                null, 
                AutoSaveIntervalMs, 
                AutoSaveIntervalMs);
        }
        
        /// <summary>
        /// Checks if a previous session crashed and recovery is available.
        /// </summary>
        public bool HasRecoverableSession()
        {
            return File.Exists(_crashMarkerPath) && File.Exists(_sessionFilePath);
        }
        
        /// <summary>
        /// Initializes the crash recovery system. Call on application startup.
        /// Creates a crash marker that is removed on clean shutdown.
        /// </summary>
        public async Task InitializeAsync()
        {
            // Check for crash marker from previous session
            if (HasRecoverableSession())
            {
                await TryRecoverSessionAsync();
            }
            
            // Create crash marker for this session
            await File.WriteAllTextAsync(_crashMarkerPath, DateTime.UtcNow.ToString("O"));
            
            // Initialize empty session state
            _currentState = new SessionState
            {
                SessionId = Guid.NewGuid().ToString(),
                StartTime = DateTime.UtcNow
            };
        }
        
        /// <summary>
        /// Marks clean shutdown. Call on normal application exit.
        /// Removes the crash marker so next launch knows we exited cleanly.
        /// </summary>
        public void MarkCleanShutdown()
        {
            try
            {
                if (File.Exists(_crashMarkerPath))
                {
                    File.Delete(_crashMarkerPath);
                }
                
                // Optionally clean up session file on clean exit
                if (File.Exists(_sessionFilePath))
                {
                    File.Delete(_sessionFilePath);
                }
            }
            // ALLOWED: empty catch - Best effort cleanup, failure is acceptable
            catch
            {
            }
        }
        
        /// <summary>
        /// Updates the current session state. Call when significant state changes occur.
        /// </summary>
        public void UpdateState(Action<SessionState> updateAction)
        {
            lock (_saveLock)
            {
                if (_currentState != null)
                {
                    updateAction(_currentState);
                    _currentState.LastModified = DateTime.UtcNow;
                }
            }
        }
        
        /// <summary>
        /// Sets the currently open project for recovery purposes.
        /// </summary>
        public void SetActiveProject(string? projectPath, string? projectName)
        {
            UpdateState(state =>
            {
                state.ActiveProjectPath = projectPath;
                state.ActiveProjectName = projectName;
            });
        }
        
        /// <summary>
        /// Adds an unsaved change marker for the specified file.
        /// </summary>
        public void MarkUnsavedChange(string filePath, string changeDescription)
        {
            UpdateState(state =>
            {
                state.UnsavedChanges[filePath] = new UnsavedChangeInfo
                {
                    FilePath = filePath,
                    Description = changeDescription,
                    Timestamp = DateTime.UtcNow
                };
            });
        }
        
        /// <summary>
        /// Clears unsaved change marker when file is saved.
        /// </summary>
        public void ClearUnsavedChange(string filePath)
        {
            UpdateState(state =>
            {
                state.UnsavedChanges.Remove(filePath);
            });
        }
        
        /// <summary>
        /// Saves the current session state immediately.
        /// Call before potentially risky operations.
        /// </summary>
        public async Task SaveSessionAsync()
        {
            SessionState? stateToSave;
            
            lock (_saveLock)
            {
                if (_currentState == null)
                    return;
                    
                stateToSave = _currentState.Clone();
            }
            
            try
            {
                var json = JsonSerializer.Serialize(stateToSave, _jsonOptions);
                
                // Write to temp file first, then rename for atomic save
                var tempPath = _sessionFilePath + ".tmp";
                await File.WriteAllTextAsync(tempPath, json);
                File.Move(tempPath, _sessionFilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                ErrorLogger.LogWarning($"Failed to save session state: {ex.Message}", "CrashRecovery");
            }
        }
        
        /// <summary>
        /// Attempts to recover the previous session.
        /// </summary>
        private async Task TryRecoverSessionAsync()
        {
            try
            {
                var json = await File.ReadAllTextAsync(_sessionFilePath);
                var recoveredState = JsonSerializer.Deserialize<SessionState>(json, _jsonOptions);
                
                if (recoveredState != null)
                {
                    SessionRecovered?.Invoke(this, new SessionRecoveredEventArgs(recoveredState));
                    ErrorLogger.LogDebug($"[CrashRecovery] Session recovered from crash. Session ID: {recoveredState.SessionId}", "CrashRecoveryService");
                }
            }
            catch (Exception ex)
            {
                RecoveryFailed?.Invoke(this, new RecoveryFailedEventArgs(ex.Message));
                ErrorLogger.LogWarning($"Failed to recover session: {ex.Message}", "CrashRecovery");
            }
        }
        
        private void OnAutoSaveTimer(object? state)
        {
            _ = SaveSessionAsync();
        }
        
        public void Dispose()
        {
            if (_disposed)
                return;
                
            _disposed = true;
            _autoSaveTimer?.Dispose();
        }
    }
    
    /// <summary>
    /// Represents the recoverable session state.
    /// </summary>
    public class SessionState
    {
        public string SessionId { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime LastModified { get; set; }
        public string? ActiveProjectPath { get; set; }
        public string? ActiveProjectName { get; set; }
        public Dictionary<string, UnsavedChangeInfo> UnsavedChanges { get; set; } = new();
        public List<string> OpenPanels { get; set; } = new();
        public Dictionary<string, object?> CustomState { get; set; } = new();
        
        public SessionState Clone()
        {
            return new SessionState
            {
                SessionId = SessionId,
                StartTime = StartTime,
                LastModified = LastModified,
                ActiveProjectPath = ActiveProjectPath,
                ActiveProjectName = ActiveProjectName,
                UnsavedChanges = new Dictionary<string, UnsavedChangeInfo>(UnsavedChanges),
                OpenPanels = new List<string>(OpenPanels),
                CustomState = new Dictionary<string, object?>(CustomState)
            };
        }
    }
    
    public class UnsavedChangeInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
    
    public class SessionRecoveredEventArgs : EventArgs
    {
        public SessionState RecoveredState { get; }
        
        public SessionRecoveredEventArgs(SessionState state)
        {
            RecoveredState = state;
        }
    }
    
    public class RecoveryFailedEventArgs : EventArgs
    {
        public string ErrorMessage { get; }
        
        public RecoveryFailedEventArgs(string message)
        {
            ErrorMessage = message;
        }
    }
}
