// Phase 5.2: Power User Features
// Task 5.2.3: Macro Recording - Record command sequences, save/load macros

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace VoiceStudio.App.Services;

/// <summary>
/// Represents a recorded macro step.
/// </summary>
public class MacroStep
{
    public string CommandId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public Dictionary<string, object?> Parameters { get; set; } = new();
    public TimeSpan? DelayBefore { get; set; }
}

/// <summary>
/// Represents a recorded macro (local recording).
/// Named RecordedMacro to avoid conflict with VoiceStudio.Core.Models.Macro.
/// </summary>
public class RecordedMacro
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "Custom";
    public List<MacroStep> Steps { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public string? KeyboardShortcut { get; set; }
    public bool IncludeTimingDelays { get; set; }
}

/// <summary>
/// Event args for macro recording events.
/// </summary>
public class MacroRecordingEventArgs : EventArgs
{
    public bool IsRecording { get; set; }
    public int StepCount { get; set; }
    public MacroStep? LatestStep { get; set; }
}

/// <summary>
/// Event args for macro playback events.
/// </summary>
public class MacroPlaybackEventArgs : EventArgs
{
    public RecordedMacro Macro { get; set; } = null!;
    public int CurrentStep { get; set; }
    public int TotalSteps { get; set; }
    public bool IsComplete { get; set; }
    public bool IsCancelled { get; set; }
}

/// <summary>
/// Service for recording, saving, and playing back macros.
/// </summary>
public class MacroRecorderService
{
    private readonly string _macrosDirectory;
    private readonly List<RecordedMacro> _macros = new();
    private readonly List<MacroStep> _recordingSteps = new();
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _isRecording;
    private bool _isPlaying;
    private DateTime? _lastStepTime;
    private System.Threading.CancellationTokenSource? _playbackCts;

    public event EventHandler<MacroRecordingEventArgs>? RecordingStateChanged;
    public event EventHandler<MacroPlaybackEventArgs>? PlaybackProgress;

    public MacroRecorderService()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _macrosDirectory = Path.Combine(appDataPath, "VoiceStudio", "Macros");
        Directory.CreateDirectory(_macrosDirectory);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    /// <summary>
    /// Gets whether macro recording is active.
    /// </summary>
    public bool IsRecording => _isRecording;

    /// <summary>
    /// Gets whether a macro is currently playing.
    /// </summary>
    public bool IsPlaying => _isPlaying;

    /// <summary>
    /// Gets all loaded macros.
    /// </summary>
    public IReadOnlyList<RecordedMacro> Macros => _macros;

    /// <summary>
    /// Gets the current recording step count.
    /// </summary>
    public int RecordingStepCount => _recordingSteps.Count;

    /// <summary>
    /// Loads all macros from disk.
    /// </summary>
    public async Task LoadMacrosAsync()
    {
        _macros.Clear();

        try
        {
            foreach (var file in Directory.GetFiles(_macrosDirectory, "*.json"))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var macro = JsonSerializer.Deserialize<RecordedMacro>(json, _jsonOptions);
                    if (macro != null)
                    {
                        _macros.Add(macro);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load macro {file}: {ex.Message}");
                }
            }

            _macros.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load macros: {ex.Message}");
        }
    }

    /// <summary>
    /// Starts recording a new macro.
    /// </summary>
    public void StartRecording()
    {
        if (_isPlaying)
            return;

        _recordingSteps.Clear();
        _isRecording = true;
        _lastStepTime = null;

        RaiseRecordingStateChanged();
    }

    /// <summary>
    /// Records a command execution as a macro step.
    /// </summary>
    public void RecordStep(string commandId, Dictionary<string, object?>? parameters = null)
    {
        if (!_isRecording)
            return;

        var now = DateTime.UtcNow;
        var step = new MacroStep
        {
            CommandId = commandId,
            Timestamp = now,
            Parameters = parameters ?? new(),
            DelayBefore = _lastStepTime.HasValue ? now - _lastStepTime.Value : null
        };

        _recordingSteps.Add(step);
        _lastStepTime = now;

        RaiseRecordingStateChanged(step);
    }

    /// <summary>
    /// Stops recording and returns the recorded steps.
    /// </summary>
    public List<MacroStep> StopRecording()
    {
        _isRecording = false;
        var steps = new List<MacroStep>(_recordingSteps);
        RaiseRecordingStateChanged();
        return steps;
    }

    /// <summary>
    /// Cancels recording without saving.
    /// </summary>
    public void CancelRecording()
    {
        _isRecording = false;
        _recordingSteps.Clear();
        RaiseRecordingStateChanged();
    }

    /// <summary>
    /// Saves the recorded steps as a new macro.
    /// </summary>
    public async Task<RecordedMacro> SaveMacroAsync(string name, string? description = null, bool includeDelays = false)
    {
        var steps = StopRecording();

        var macro = new RecordedMacro
        {
            Name = name,
            Description = description ?? string.Empty,
            Steps = steps,
            IncludeTimingDelays = includeDelays,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        };

        // Remove timing delays if not included
        if (!includeDelays)
        {
            foreach (var step in macro.Steps)
            {
                step.DelayBefore = null;
            }
        }

        await SaveMacroToFileAsync(macro);
        _macros.Add(macro);
        _macros.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        return macro;
    }

    /// <summary>
    /// Saves a macro to disk.
    /// </summary>
    public async Task SaveMacroToFileAsync(RecordedMacro macro)
    {
        macro.ModifiedAt = DateTime.UtcNow;
        var filePath = Path.Combine(_macrosDirectory, $"{macro.Id}.json");
        var json = JsonSerializer.Serialize(macro, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>
    /// Deletes a macro.
    /// </summary>
    public async Task DeleteMacroAsync(string macroId)
    {
        var macro = _macros.FirstOrDefault(m => m.Id == macroId);
        if (macro != null)
        {
            _macros.Remove(macro);

            var filePath = Path.Combine(_macrosDirectory, $"{macroId}.json");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Plays a macro.
    /// </summary>
    public async Task PlayMacroAsync(RecordedMacro macro, Func<MacroStep, Task> executeStep)
    {
        if (_isRecording || _isPlaying)
            return;

        _isPlaying = true;
        _playbackCts = new System.Threading.CancellationTokenSource();

        try
        {
            for (int i = 0; i < macro.Steps.Count; i++)
            {
                if (_playbackCts.Token.IsCancellationRequested)
                {
                    PlaybackProgress?.Invoke(this, new MacroPlaybackEventArgs
                    {
                        Macro = macro,
                        CurrentStep = i,
                        TotalSteps = macro.Steps.Count,
                        IsCancelled = true
                    });
                    break;
                }

                var step = macro.Steps[i];

                // Apply delay if included
                if (macro.IncludeTimingDelays && step.DelayBefore.HasValue)
                {
                    await Task.Delay(step.DelayBefore.Value, _playbackCts.Token);
                }

                PlaybackProgress?.Invoke(this, new MacroPlaybackEventArgs
                {
                    Macro = macro,
                    CurrentStep = i + 1,
                    TotalSteps = macro.Steps.Count
                });

                await executeStep(step);
            }

            PlaybackProgress?.Invoke(this, new MacroPlaybackEventArgs
            {
                Macro = macro,
                CurrentStep = macro.Steps.Count,
                TotalSteps = macro.Steps.Count,
                IsComplete = true
            });
        }
        // ALLOWED: empty catch - playback cancellation is expected and handled
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _isPlaying = false;
            _playbackCts?.Dispose();
            _playbackCts = null;
        }
    }

    /// <summary>
    /// Stops macro playback.
    /// </summary>
    public void StopPlayback()
    {
        _playbackCts?.Cancel();
    }

    /// <summary>
    /// Gets macros by category.
    /// </summary>
    public IEnumerable<RecordedMacro> GetMacrosByCategory(string category)
    {
        return _macros.Where(m => m.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Searches macros by name or description.
    /// </summary>
    public IEnumerable<RecordedMacro> SearchMacros(string query)
    {
        var lowerQuery = query.ToLowerInvariant();
        return _macros.Where(m =>
            m.Name.ToLowerInvariant().Contains(lowerQuery) ||
            m.Description.ToLowerInvariant().Contains(lowerQuery));
    }

    /// <summary>
    /// Exports a macro to JSON.
    /// </summary>
    public string ExportMacro(RecordedMacro macro)
    {
        return JsonSerializer.Serialize(macro, _jsonOptions);
    }

    /// <summary>
    /// Imports a macro from JSON.
    /// </summary>
    public async Task<RecordedMacro?> ImportMacroAsync(string json)
    {
        try
        {
            var macro = JsonSerializer.Deserialize<RecordedMacro>(json, _jsonOptions);
            if (macro != null)
            {
                // Generate new ID to avoid conflicts
                macro.Id = Guid.NewGuid().ToString();
                macro.ModifiedAt = DateTime.UtcNow;

                await SaveMacroToFileAsync(macro);
                _macros.Add(macro);
                _macros.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

                return macro;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to import macro: {ex.Message}");
        }

        return null;
    }

    private void RaiseRecordingStateChanged(MacroStep? latestStep = null)
    {
        RecordingStateChanged?.Invoke(this, new MacroRecordingEventArgs
        {
            IsRecording = _isRecording,
            StepCount = _recordingSteps.Count,
            LatestStep = latestStep
        });
    }
}
