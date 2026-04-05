using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Services
{
    /// <summary>
    /// Crash recovery metadata under %LocalAppData%/VoiceStudio/Recovery/.
    /// Session snapshot is written after unified saves; recovery from crash is user-confirmed (no silent restore).
    /// </summary>
    public class CrashRecoveryService : IDisposable
    {
        private readonly string _recoveryDirectory;
        private readonly string _sessionFilePath;
        private readonly string _crashMarkerPath;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly object _saveLock = new();
        
        private SessionState? _currentState;
        private SessionState? _pendingRecoveryState;
        private bool _disposed;
        
        public event EventHandler<SessionRecoveredEventArgs>? SessionRecovered;
        public event EventHandler<RecoveryFailedEventArgs>? RecoveryFailed;
        
        /// <summary>Fired after <see cref="InitializeAsync"/> determines whether a user recovery prompt may be needed.</summary>
        public event EventHandler? PendingRecoveryDetermined;
        
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
        }
        
        /// <summary>
        /// Checks if a previous session crashed and recovery is available.
        /// </summary>
        public bool HasRecoverableSession()
        {
            return File.Exists(_crashMarkerPath) && File.Exists(_sessionFilePath);
        }

        /// <summary>True when a prior session left a snapshot the user should explicitly restore or discard.</summary>
        public bool HasPendingUserRecoveryPrompt => _pendingRecoveryState != null;
        
        /// <summary>
        /// Initializes the crash recovery system. Call on application startup (deferred).
        /// Loads optional pending recovery into memory without applying it; creates a crash marker for this run.
        /// </summary>
        public async Task InitializeAsync()
        {
            _pendingRecoveryState = null;
            if (HasRecoverableSession())
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_sessionFilePath).ConfigureAwait(false);
                    var recovered = JsonSerializer.Deserialize<SessionState>(json, _jsonOptions);
                    if (recovered != null)
                        _pendingRecoveryState = recovered;
                }
                catch (Exception ex)
                {
                    RecoveryFailed?.Invoke(this, new RecoveryFailedEventArgs(ex.Message));
                    ErrorLogger.LogWarning($"Crash recovery: could not read session snapshot: {ex.Message}", CrashRecoveryLogCategory);
                }
            }
            
            try
            {
                await File.WriteAllTextAsync(_crashMarkerPath, DateTime.UtcNow.ToString("O")).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ErrorLogger.LogWarning($"Crash recovery: failed to write crash marker: {ex.Message}", CrashRecoveryLogCategory);
            }
            
            _currentState = new SessionState
            {
                SessionId = Guid.NewGuid().ToString(),
                StartTime = DateTime.UtcNow
            };

            try
            {
                PendingRecoveryDetermined?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                ErrorLogger.LogWarning($"Crash recovery: PendingRecoveryDetermined handler failed: {ex.Message}", CrashRecoveryLogCategory);
            }
        }

        private const string CrashRecoveryLogCategory = "CrashRecovery";
        
        /// <summary>
        /// Marks clean shutdown. Call on normal application exit.
        /// Removes the crash marker so next launch knows we exited cleanly.
        /// </summary>
        public void MarkCleanShutdown()
        {
            try
            {
                if (File.Exists(_crashMarkerPath))
                    File.Delete(_crashMarkerPath);
                
                if (File.Exists(_sessionFilePath))
                    File.Delete(_sessionFilePath);
            }
            catch (Exception ex)
            {
                ErrorLogger.LogWarning($"Crash recovery: clean shutdown cleanup failed: {ex.Message}", CrashRecoveryLogCategory);
            }
        }

        /// <summary>User declined restore — remove recovery snapshot only.</summary>
        public void DiscardPendingRecovery()
        {
            _pendingRecoveryState = null;
            try
            {
                if (File.Exists(_sessionFilePath))
                    File.Delete(_sessionFilePath);
            }
            catch (Exception ex)
            {
                ErrorLogger.LogWarning($"Crash recovery: discard session file failed: {ex.Message}", CrashRecoveryLogCategory);
            }
        }

        /// <summary>Optional hook after user successfully restores; clears pending and raises <see cref="SessionRecovered"/>.</summary>
        public void NotifyRecoveryAccepted(SessionState state)
        {
            _pendingRecoveryState = null;
            try
            {
                if (File.Exists(_sessionFilePath))
                    File.Delete(_sessionFilePath);
            }
            catch (Exception ex)
            {
                ErrorLogger.LogWarning($"Crash recovery: remove session file after accept failed: {ex.Message}", CrashRecoveryLogCategory);
            }

            SessionRecovered?.Invoke(this, new SessionRecoveredEventArgs(state));
        }

        /// <summary>Snapshot state for UI/tests when prompting.</summary>
        public SessionState? PeekPendingRecovery() => _pendingRecoveryState;
        
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
        /// Sets the currently open project for recovery metadata (after unified save).
        /// </summary>
        public void SetActiveProject(string? projectId, string? projectPath, string? projectName)
        {
            UpdateState(state =>
            {
                state.ActiveProjectId = projectId;
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
        
        public void Dispose()
        {
            if (_disposed)
                return;
                
            _disposed = true;
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
        public string? ActiveProjectId { get; set; }
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
                ActiveProjectId = ActiveProjectId,
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
