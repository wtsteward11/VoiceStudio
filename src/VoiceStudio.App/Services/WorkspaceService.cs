// VoiceStudio - Panel Architecture Phase 3: Workspace System
// WorkspaceService implements workspace management with JSON persistence

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// Manages workspaces (panel layout configurations) with JSON persistence.
/// </summary>
public class WorkspaceService : IWorkspaceService
{
    private readonly ILayoutService? _layoutService;
    private readonly IEventAggregator? _eventAggregator;
    private readonly ILogger<WorkspaceService>? _logger;
    private readonly string _configPath;
    private readonly object _lock = new();
    
    private WorkspaceConfiguration _config = new();
    private bool _isLoaded = false;
    
    /// <summary>
    /// Source panel ID used when publishing workspace events.
    /// </summary>
    private const string WorkspaceServicePanelId = "workspace-service";

    /// <summary>
    /// Default presets that cannot be deleted.
    /// </summary>
    private static readonly List<WorkspaceDefinition> DefaultPresets = new()
    {
        new WorkspaceDefinition
        {
            Id = "default",
            Name = "Default",
            Description = "Standard layout for voice synthesis",
            IconGlyph = "\uE8A1",
            IsPreset = true,
            KeyboardShortcut = "Ctrl+1",
            Panels = new List<PanelPlacement>
            {
                new() { PanelId = PanelIds.Library, Region = PanelRegion.Left, Order = 0, IsVisible = true },
                new() { PanelId = PanelIds.Profiles, Region = PanelRegion.Left, Order = 1, IsVisible = true },
                new() { PanelId = PanelIds.VoiceSynthesis, Region = PanelRegion.Center, Order = 0, IsVisible = true },
                new() { PanelId = PanelIds.Timeline, Region = PanelRegion.Bottom, Order = 0, IsVisible = true },
                new() { PanelId = PanelIds.EffectsMixer, Region = PanelRegion.Right, Order = 0, IsVisible = true }
            }
        },
        new WorkspaceDefinition
        {
            Id = "cloning",
            Name = "Voice Cloning",
            Description = "Optimized layout for voice cloning workflows",
            IconGlyph = "\uE8D4",
            IsPreset = true,
            KeyboardShortcut = "Ctrl+2",
            Panels = new List<PanelPlacement>
            {
                new() { PanelId = PanelIds.Library, Region = PanelRegion.Left, Order = 0, IsVisible = true, RelativeWidth = 0.25 },
                new() { PanelId = PanelIds.VoiceCloningWizard, Region = PanelRegion.Center, Order = 0, IsVisible = true },
                new() { PanelId = PanelIds.Profiles, Region = PanelRegion.Right, Order = 0, IsVisible = true, RelativeWidth = 0.25 },
                new() { PanelId = PanelIds.Training, Region = PanelRegion.Bottom, Order = 0, IsVisible = true }
            }
        },
        new WorkspaceDefinition
        {
            Id = "editing",
            Name = "Audio Editing",
            Description = "Focused layout for timeline editing",
            IconGlyph = "\uE78C",
            IsPreset = true,
            KeyboardShortcut = "Ctrl+3",
            Panels = new List<PanelPlacement>
            {
                new() { PanelId = PanelIds.Library, Region = PanelRegion.Left, Order = 0, IsVisible = true, RelativeWidth = 0.2 },
                new() { PanelId = PanelIds.Timeline, Region = PanelRegion.Center, Order = 0, IsVisible = true },
                new() { PanelId = PanelIds.AudioAnalysis, Region = PanelRegion.Center, Order = 1, IsVisible = true },
                new() { PanelId = PanelIds.EffectsMixer, Region = PanelRegion.Right, Order = 0, IsVisible = true, RelativeWidth = 0.2 }
            }
        },
        new WorkspaceDefinition
        {
            Id = "minimal",
            Name = "Minimal",
            Description = "Clean workspace with essential panels only",
            IconGlyph = "\uE739",
            IsPreset = true,
            KeyboardShortcut = "Ctrl+4",
            Panels = new List<PanelPlacement>
            {
                new() { PanelId = PanelIds.VoiceSynthesis, Region = PanelRegion.Center, Order = 0, IsVisible = true },
                new() { PanelId = PanelIds.EffectsMixer, Region = PanelRegion.Right, Order = 0, IsVisible = true, RelativeWidth = 0.3 }
            }
        }
    };

    public WorkspaceService(
        ILayoutService? layoutService = null, 
        IEventAggregator? eventAggregator = null,
        ILogger<WorkspaceService>? logger = null)
    {
        _layoutService = layoutService;
        _eventAggregator = eventAggregator;
        _logger = logger;

        // Store workspaces in the app data folder
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var voiceStudioPath = Path.Combine(appDataPath, "VoiceStudio");
        Directory.CreateDirectory(voiceStudioPath);
        _configPath = Path.Combine(voiceStudioPath, "workspaces.json");
    }

    #region IWorkspaceService Properties

    public WorkspaceDefinition? ActiveWorkspace
    {
        get
        {
            lock (_lock)
            {
                if (string.IsNullOrEmpty(_config.ActiveWorkspaceId))
                    return _config.Workspaces.FirstOrDefault();
                return _config.Workspaces.FirstOrDefault(w => w.Id == _config.ActiveWorkspaceId);
            }
        }
    }

    public IReadOnlyList<WorkspaceDefinition> Workspaces
    {
        get
        {
            lock (_lock)
            {
                return _config.Workspaces.ToList();
            }
        }
    }

    #endregion

    #region Workspace Management

    public async Task<bool> SwitchWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        WorkspaceDefinition? previous;
        WorkspaceDefinition? target;

        lock (_lock)
        {
            target = _config.Workspaces.FirstOrDefault(w => w.Id == workspaceId);
            if (target == null)
            {
                _logger?.LogWarning("Workspace not found: {WorkspaceId}", workspaceId);
                return false;
            }

            previous = ActiveWorkspace;
            _config.ActiveWorkspaceId = workspaceId;
        }

        // Apply the layout
        if (_layoutService != null && target.Panels.Any())
        {
            await _layoutService.ApplyLayoutAsync(target.Panels);
        }

        // Raise event
        OnWorkspaceChanged(new WorkspaceChangedEventArgs(previous, target, wasSwitch: true));

        // Persist the change
        await SaveAsync(cancellationToken);

        _logger?.LogInformation("Switched to workspace: {WorkspaceName}", target.Name);
        return true;
    }

    public Task<WorkspaceDefinition> CreateWorkspaceAsync(
        string name,
        string? description = null,
        string? iconGlyph = null,
        CancellationToken cancellationToken = default)
    {
        // Capture current layout
        var panels = _layoutService?.CaptureCurrentLayout() ?? new List<PanelPlacement>();

        var workspace = new WorkspaceDefinition
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Description = description,
            IconGlyph = iconGlyph ?? "\uE8A1",
            IsPreset = false,
            Panels = panels.ToList()
        };

        lock (_lock)
        {
            _config.Workspaces.Add(workspace);
        }

        OnWorkspaceModified(workspace);
        _ = SaveAsync(cancellationToken);

        _logger?.LogInformation("Created workspace: {WorkspaceName}", name);
        return Task.FromResult(workspace);
    }

    public Task<WorkspaceDefinition> DuplicateWorkspaceAsync(
        string sourceWorkspaceId,
        string newName,
        CancellationToken cancellationToken = default)
    {
        WorkspaceDefinition? source;
        lock (_lock)
        {
            source = _config.Workspaces.FirstOrDefault(w => w.Id == sourceWorkspaceId);
        }

        if (source == null)
        {
            throw new ArgumentException($"Source workspace not found: {sourceWorkspaceId}");
        }

        var workspace = source with
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = newName,
            IsPreset = false,
            IsActive = false,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
            KeyboardShortcut = null
        };

        lock (_lock)
        {
            _config.Workspaces.Add(workspace);
        }

        OnWorkspaceModified(workspace);
        _ = SaveAsync(cancellationToken);

        _logger?.LogInformation("Duplicated workspace {Source} as {New}", source.Name, newName);
        return Task.FromResult(workspace);
    }

    public Task<bool> UpdateWorkspaceAsync(
        WorkspaceDefinition workspace,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var index = _config.Workspaces.FindIndex(w => w.Id == workspace.Id);
            if (index < 0)
            {
                _logger?.LogWarning("Cannot update - workspace not found: {WorkspaceId}", workspace.Id);
                return Task.FromResult(false);
            }

            _config.Workspaces[index] = workspace.WithModified();
        }

        OnWorkspaceModified(workspace);
        _ = SaveAsync(cancellationToken);

        _logger?.LogInformation("Updated workspace: {WorkspaceName}", workspace.Name);
        return Task.FromResult(true);
    }

    public Task<bool> DeleteWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var workspace = _config.Workspaces.FirstOrDefault(w => w.Id == workspaceId);
            if (workspace == null)
            {
                _logger?.LogWarning("Cannot delete - workspace not found: {WorkspaceId}", workspaceId);
                return Task.FromResult(false);
            }

            if (workspace.IsPreset)
            {
                _logger?.LogWarning("Cannot delete preset workspace: {WorkspaceName}", workspace.Name);
                return Task.FromResult(false);
            }

            _config.Workspaces.Remove(workspace);

            // If we deleted the active workspace, switch to default
            if (_config.ActiveWorkspaceId == workspaceId)
            {
                _config.ActiveWorkspaceId = "default";
            }
        }

        _ = SaveAsync(cancellationToken);

        _logger?.LogInformation("Deleted workspace: {WorkspaceId}", workspaceId);
        return Task.FromResult(true);
    }

    public async Task SaveCurrentLayoutAsync(CancellationToken cancellationToken = default)
    {
        if (_layoutService == null) return;

        var currentLayout = _layoutService.CaptureCurrentLayout();
        var activeId = _config.ActiveWorkspaceId;

        lock (_lock)
        {
            var index = _config.Workspaces.FindIndex(w => w.Id == activeId);
            if (index >= 0)
            {
                var updated = _config.Workspaces[index] with
                {
                    Panels = currentLayout.ToList(),
                    ModifiedAt = DateTimeOffset.UtcNow
                };
                _config.Workspaces[index] = updated;
                OnWorkspaceModified(updated);
            }
        }

        await SaveAsync(cancellationToken);
        _logger?.LogInformation("Saved current layout to active workspace");
    }

    public async Task ResetActiveWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        var activeId = _config.ActiveWorkspaceId;
        var preset = DefaultPresets.FirstOrDefault(p => p.Id == activeId);

        if (preset != null)
        {
            lock (_lock)
            {
                var index = _config.Workspaces.FindIndex(w => w.Id == activeId);
                if (index >= 0)
                {
                    _config.Workspaces[index] = preset with { ModifiedAt = DateTimeOffset.UtcNow };
                }
            }

            if (_layoutService != null)
            {
                await _layoutService.ApplyLayoutAsync(preset.Panels);
            }

            await SaveAsync(cancellationToken);
            _logger?.LogInformation("Reset workspace {Id} to default", activeId);
        }
    }

    #endregion

    #region Persistence

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_isLoaded) return;

        try
        {
            if (File.Exists(_configPath))
            {
                var json = await File.ReadAllTextAsync(_configPath, cancellationToken);
                var loaded = JsonSerializer.Deserialize<WorkspaceConfiguration>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (loaded != null)
                {
                    lock (_lock)
                    {
                        _config = loaded;
                        
                        // Ensure all presets exist
                        foreach (var preset in DefaultPresets)
                        {
                            if (!_config.Workspaces.Any(w => w.Id == preset.Id))
                            {
                                _config.Workspaces.Insert(0, preset);
                            }
                        }
                    }

                    _logger?.LogInformation("Loaded workspace configuration with {Count} workspaces", _config.Workspaces.Count);
                }
            }
            else
            {
                // Initialize with default presets
                lock (_lock)
                {
                    _config.Workspaces.AddRange(DefaultPresets);
                    _config.ActiveWorkspaceId = "default";
                }

                await SaveAsync(cancellationToken);
                _logger?.LogInformation("Initialized workspace configuration with default presets");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load workspace configuration");
            
            // Fallback to defaults
            lock (_lock)
            {
                _config = new WorkspaceConfiguration
                {
                    Workspaces = DefaultPresets.ToList(),
                    ActiveWorkspaceId = "default"
                };
            }
        }

        _isLoaded = true;
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            string json;
            lock (_lock)
            {
                _config.LastSaved = DateTimeOffset.UtcNow;
                json = JsonSerializer.Serialize(_config, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
            }

            await File.WriteAllTextAsync(_configPath, json, cancellationToken);
            _logger?.LogDebug("Saved workspace configuration");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save workspace configuration");
        }
    }

    public Task<string> ExportWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        WorkspaceDefinition? workspace;
        lock (_lock)
        {
            workspace = _config.Workspaces.FirstOrDefault(w => w.Id == workspaceId);
        }

        if (workspace == null)
        {
            throw new ArgumentException($"Workspace not found: {workspaceId}");
        }

        var json = JsonSerializer.Serialize(workspace, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        return Task.FromResult(json);
    }

    public Task<WorkspaceDefinition?> ImportWorkspaceAsync(string json, CancellationToken cancellationToken = default)
    {
        try
        {
            var workspace = JsonSerializer.Deserialize<WorkspaceDefinition>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (workspace == null)
            {
                return Task.FromResult<WorkspaceDefinition?>(null);
            }

            // Assign new ID and mark as non-preset
            var imported = workspace with
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = workspace.Name + " (Imported)",
                IsPreset = false,
                CreatedAt = DateTimeOffset.UtcNow,
                ModifiedAt = DateTimeOffset.UtcNow
            };

            lock (_lock)
            {
                _config.Workspaces.Add(imported);
            }

            _ = SaveAsync(cancellationToken);
            _logger?.LogInformation("Imported workspace: {WorkspaceName}", imported.Name);

            return Task.FromResult<WorkspaceDefinition?>(imported);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to import workspace");
            return Task.FromResult<WorkspaceDefinition?>(null);
        }
    }

    #endregion

    #region Events

    public event EventHandler<WorkspaceChangedEventArgs>? WorkspaceChanged;
    public event EventHandler<WorkspaceDefinition>? WorkspaceModified;

    private void OnWorkspaceChanged(WorkspaceChangedEventArgs args)
    {
        // Keep traditional .NET event for backward compatibility
        WorkspaceChanged?.Invoke(this, args);
        
        // Also publish via EventAggregator for cross-panel coordination (Phase 3)
        if (_eventAggregator != null && args.Current != null)
        {
            var intent = args.WasSwitch 
                ? InteractionIntent.Navigation 
                : InteractionIntent.SystemRestore;
            
            _eventAggregator.Publish(new WorkspaceChangedEvent(
                WorkspaceServicePanelId,
                args.Current.Id,
                args.Current.Name,
                args.Previous?.Id,
                wasUserInitiated: args.WasSwitch,
                intent));
        }
    }

    private void OnWorkspaceModified(WorkspaceDefinition workspace)
    {
        WorkspaceModified?.Invoke(this, workspace);
    }

    #endregion
}
