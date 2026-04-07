using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Service for managing panel state persistence and workspace profiles.
  /// Implements IDEA 3: Panel State Persistence with Workspace Profiles.
  /// Phase 5.0: Now implements IUnifiedWorkspaceService for service unification.
  /// </summary>
  public class PanelStateService : IUnifiedWorkspaceService
  {
    // Built-in workspace templates
    private static readonly List<WorkspaceTemplate> _builtInTemplates = new()
    {
      new WorkspaceTemplate
      {
        Id = "recording",
        Name = "Recording Studio",
        Description = "Optimized for voice recording with waveform and input monitoring",
        Category = "Production",
        Layout = new WorkspaceLayout { ProfileName = "Recording", Version = "1.0" }
      },
      new WorkspaceTemplate
      {
        Id = "mixing",
        Name = "Mixing Console",
        Description = "Audio mixing with effects chain and analyzer panels",
        Category = "Production",
        Layout = new WorkspaceLayout { ProfileName = "Mixing", Version = "1.0" }
      },
      new WorkspaceTemplate
      {
        Id = "synthesis",
        Name = "Voice Synthesis",
        Description = "Text-to-speech synthesis with profile management",
        Category = "Synthesis",
        Layout = new WorkspaceLayout { ProfileName = "Synthesis", Version = "1.0" }
      },
      new WorkspaceTemplate
      {
        Id = "analysis",
        Name = "Audio Analysis",
        Description = "Detailed audio analysis with spectrogram and quality metrics",
        Category = "Analysis",
        Layout = new WorkspaceLayout { ProfileName = "Analysis", Version = "1.0" }
      },
      new WorkspaceTemplate
      {
        Id = "training",
        Name = "Model Training",
        Description = "Voice model training with dataset and progress panels",
        Category = "Training",
        Layout = new WorkspaceLayout { ProfileName = "Training", Version = "1.0" }
      }
    };

    private readonly ISettingsService _settingsService;
    private readonly string _workspaceProfilesDirectory;
    private readonly string _projectStatesDirectory;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly JsonSerializerOptions _embeddedLayoutOptions;
    private WorkspaceLayout? _currentLayout;
    private string _currentWorkspaceProfile = "studio";
    private bool _disposed;

    /// <summary>
    /// GAP-070: serialize load-merge-save for <see cref="SaveCurrentWorkspaceAsync"/> so concurrent workspace writes
    /// cannot interleave and drop fields from <see cref="SettingsData"/>.
    /// </summary>
    private readonly SemaphoreSlim _workspaceSettingsSaveGate = new(1, 1);

    /// <summary>Maps workspace profile ID (e.g. studio, batch_lab) to embedded resource file name (e.g. Studio.json, BatchLab.json).</summary>
    private static readonly Dictionary<string, string> ProfileIdToResourceName = new(StringComparer.OrdinalIgnoreCase)
    {
      ["studio"] = "Studio",
      ["default"] = "Studio",
      ["recording"] = "Recording",
      ["mixing"] = "Mixing",
      ["synthesis"] = "Synthesis",
      ["analysis"] = "Analysis",
      ["training"] = "Training",
      ["batch_lab"] = "BatchLab",
      ["pro_mix"] = "ProMix"
    };

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
      "CON", "PRN", "AUX", "NUL",
      "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
      "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private static bool IsValidProfileName(string? name, out string? reason)
    {
      reason = null;
      if (name == null) { reason = "null"; return false; }
      var trimmed = name.Trim();
      if (trimmed.Length == 0) { reason = "empty"; return false; }
      if (trimmed.Length > 64) { reason = "too long"; return false; }
      if (trimmed.Contains('/') || trimmed.Contains('\\')) { reason = "path separator"; return false; }
      if (trimmed.Contains("..")) { reason = "traversal"; return false; }
      foreach (var c in trimmed)
        if (c < 0x20) { reason = "control char"; return false; }
      if (trimmed.IndexOfAny(new[] { '<', '>', ':', '"', '|', '?', '*' }) >= 0) { reason = "illegal char"; return false; }
      if (trimmed[0] == '.' || trimmed[^1] == '.' || trimmed[0] == ' ' || trimmed[^1] == ' ') { reason = "leading/trailing"; return false; }
      var dotIdx = trimmed.IndexOf('.');
      var baseName = dotIdx >= 0 ? trimmed[..dotIdx] : trimmed;
      if (ReservedDeviceNames.Contains(baseName)) { reason = "reserved"; return false; }
      return true;
    }

    /// <summary>
    /// Initializes the panel state service.
    /// </summary>
    /// <param name="settingsService">Settings service for loading and saving workspace layout to app settings.</param>
    /// <param name="appDataOverride">Optional override for app data root (used by tests for isolation). When null, uses LocalApplicationData.</param>
    public PanelStateService(ISettingsService settingsService, string? appDataOverride = null)
    {
      _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

      // Set up directories
      var appDataPath = appDataOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
      _workspaceProfilesDirectory = Path.Combine(appDataPath, "VoiceStudio", "WorkspaceProfiles");
      _projectStatesDirectory = Path.Combine(appDataPath, "VoiceStudio", "ProjectStates");
      Directory.CreateDirectory(_workspaceProfilesDirectory);
      Directory.CreateDirectory(_projectStatesDirectory);

      _jsonOptions = new JsonSerializerOptions
      {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
      };

      _embeddedLayoutOptions = new JsonSerializerOptions
      {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
      };

      // Load current workspace layout from settings (fire-and-forget with error handling)
      _ = LoadCurrentWorkspaceAsync();
    }

    /// <summary>
    /// Gets the current workspace layout.
    /// </summary>
    /// <returns>The current layout; a new empty layout is created if none is loaded.</returns>
    public WorkspaceLayout GetCurrentLayout()
    {
      if (_currentLayout == null)
      {
        _currentLayout = new WorkspaceLayout
        {
          ProfileName = _currentWorkspaceProfile,
          Version = "1.0",
          IsDefault = string.Equals(_currentWorkspaceProfile, "studio", StringComparison.OrdinalIgnoreCase)
        };
      }
      return _currentLayout;
    }

    /// <summary>
    /// Gets the current workspace profile name.
    /// </summary>
    public string CurrentWorkspaceProfile => _currentWorkspaceProfile;

    /// <summary>
    /// Saves panel state for a specific region.
    /// </summary>
    /// <param name="region">The panel region (Left, Center, Right, Bottom).</param>
    /// <param name="activePanelId">The ID of the currently active panel in the region.</param>
    /// <param name="openedPanels">List of panel IDs open in the region (e.g. for tabs).</param>
    public void SaveRegionState(PanelRegion region, string activePanelId, List<string> openedPanels)
    {
      SaveRegionState(region, activePanelId, openedPanels, null, null);
    }

    public void SaveRegionState(PanelRegion region, string activePanelId, List<string> openedPanels, double? widthRatio, double? heightRatio)
    {
      var layout = GetCurrentLayout();
      var regionState = layout.Regions.FirstOrDefault(r => r.Region == region);

      if (regionState == null)
      {
        regionState = new RegionState
        {
          Region = region,
          ActivePanelId = activePanelId,
          OpenedPanels = openedPanels ?? new List<string>()
        };
        layout.Regions.Add(regionState);
      }
      else
      {
        regionState.ActivePanelId = activePanelId;
        regionState.OpenedPanels = openedPanels ?? new List<string>();
      }

      if (widthRatio.HasValue)
        regionState.WidthRatio = widthRatio.Value;
      if (heightRatio.HasValue)
        regionState.HeightRatio = heightRatio.Value;

      layout.ModifiedAt = DateTime.UtcNow;
      layout.Version = "1.1";
      SaveCurrentWorkspace();
    }

    /// <summary>
    /// Saves the collapsed state for a region.
    /// </summary>
    public void SaveRegionCollapsedState(PanelRegion region, bool isCollapsed)
    {
      var layout = GetCurrentLayout();
      var regionState = layout.Regions.FirstOrDefault(r => r.Region == region);

      if (regionState == null)
      {
        regionState = new RegionState
        {
          Region = region,
          ActivePanelId = string.Empty,
          OpenedPanels = new List<string>()
        };
        layout.Regions.Add(regionState);
      }

      regionState.IsCollapsed = isCollapsed;
      layout.ModifiedAt = DateTime.UtcNow;
      SaveCurrentWorkspace();
    }

    /// <summary>
    /// Saves panel-specific state (scroll position, selected items, etc.).
    /// </summary>
    /// <param name="region">The panel region.</param>
    /// <param name="panelId">The panel ID.</param>
    /// <param name="state">The panel state to save.</param>
    public void SavePanelState(PanelRegion region, string panelId, PanelState state)
    {
      var layout = GetCurrentLayout();
      var regionState = layout.Regions.FirstOrDefault(r => r.Region == region);

      if (regionState == null)
      {
        regionState = new RegionState
        {
          Region = region,
          ActivePanelId = panelId
        };
        layout.Regions.Add(regionState);
      }
      else
      {
        // GAP-070: PanelHost persist path must not leave ActivePanelId stale when the region already exists.
        regionState.ActivePanelId = panelId;
      }

      state.PanelId = panelId;
      regionState.PanelStates[panelId] = state;
      layout.ModifiedAt = DateTime.UtcNow;
      SaveCurrentWorkspace();
    }

    /// <summary>
    /// Gets panel state for a specific panel.
    /// </summary>
    /// <param name="region">The panel region.</param>
    /// <param name="panelId">The panel ID.</param>
    /// <returns>The saved panel state, or null if not found.</returns>
    public PanelState? GetPanelState(PanelRegion region, string panelId)
    {
      var layout = GetCurrentLayout();
      var regionState = layout.Regions.FirstOrDefault(r => r.Region == region);
      return regionState?.PanelStates.GetValueOrDefault(panelId);
    }

    /// <summary>
    /// Gets region state for a specific region.
    /// </summary>
    /// <param name="region">The panel region.</param>
    /// <returns>The region state, or null if not found.</returns>
    public RegionState? GetRegionState(PanelRegion region)
    {
      var layout = GetCurrentLayout();
      return layout.Regions.FirstOrDefault(r => r.Region == region);
    }

    /// <summary>
    /// Migrates panel state from one region to another when a panel is docked/moved.
    /// Ensures panel state follows the panelId across regions.
    /// </summary>
    public void MigratePanelState(string panelId, PanelRegion fromRegion, PanelRegion toRegion)
    {
      if (fromRegion == toRegion) return;
      var layout = GetCurrentLayout();
      var fromState = layout.Regions.FirstOrDefault(r => r.Region == fromRegion);
      if (fromState?.PanelStates.TryGetValue(panelId, out var state) == true)
      {
        var toState = layout.Regions.FirstOrDefault(r => r.Region == toRegion);
        if (toState != null)
        {
          toState.PanelStates[panelId] = state;
          fromState.PanelStates.Remove(panelId);
          layout.ModifiedAt = DateTime.UtcNow;
          SaveCurrentWorkspace();
        }
      }
    }

    /// <summary>
    /// Toggles whether a panel is pinned in the Tool Catalog (pinned panels appear at top).
    /// </summary>
    /// <param name="panelId">The panel ID to toggle.</param>
    /// <returns>True if the panel is now pinned, false if unpinned.</returns>
    public bool TogglePinnedPanel(string panelId)
    {
      var layout = GetCurrentLayout();
      layout.PinnedPanelIds ??= new HashSet<string>();
      if (layout.PinnedPanelIds.Contains(panelId))
      {
        layout.PinnedPanelIds.Remove(panelId);
        layout.ModifiedAt = DateTime.UtcNow;
        SaveCurrentWorkspace();
        return false;
      }
      layout.PinnedPanelIds.Add(panelId);
      layout.ModifiedAt = DateTime.UtcNow;
      SaveCurrentWorkspace();
      return true;
    }

    /// <summary>
    /// Returns whether a panel is pinned in the Tool Catalog.
    /// </summary>
    public bool IsPanelPinned(string panelId)
    {
      var layout = GetCurrentLayout();
      return layout.PinnedPanelIds?.Contains(panelId) ?? false;
    }

    /// <summary>
    /// Saves panel state for a specific project.
    /// </summary>
    /// <param name="projectId">The project identifier.</param>
    /// <param name="layout">The workspace layout to save.</param>
    public async Task SaveProjectStateAsync(string projectId, WorkspaceLayout layout)
    {
      try
      {
        var filePath = Path.Combine(_projectStatesDirectory, $"{projectId}.json");
        var json = JsonSerializer.Serialize(layout, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json);
      }
      catch (Exception ex)
      {
        // Log error but don't throw - state saving shouldn't break the app
        System.Diagnostics.Debug.WriteLine($"Failed to save project state: {ex.Message}");
      }
    }

    /// <summary>
    /// Loads panel state for a specific project.
    /// </summary>
    /// <param name="projectId">The project identifier.</param>
    /// <returns>The saved workspace layout, or null if not found or on error.</returns>
    public async Task<WorkspaceLayout?> LoadProjectStateAsync(string projectId)
    {
      try
      {
        var filePath = Path.Combine(_projectStatesDirectory, $"{projectId}.json");
        if (!File.Exists(filePath))
          return null;

        var json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize<WorkspaceLayout>(json, _jsonOptions);
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Failed to load project state: {ex.Message}");
        return null;
      }
    }

    /// <summary>
    /// Saves a workspace profile.
    /// </summary>
    /// <param name="profile">The workspace profile to save.</param>
    public async Task SaveWorkspaceProfileAsync(WorkspaceProfile profile)
    {
      if (!IsValidProfileName(profile?.Name, out var reason))
      {
        System.Diagnostics.Debug.WriteLine($"[PanelStateService] Cannot save profile with invalid name: {reason}");
        return;
      }

      try
      {
        var filePath = Path.Combine(_workspaceProfilesDirectory, $"{profile!.Name}.json");
        profile.ModifiedAt = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(profile, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json);
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Failed to save workspace profile: {ex.Message}");
      }
    }

    /// <summary>
    /// Tries to load a workspace layout from embedded Resources/Workspaces JSON (when profile is missing or has no regions).
    /// </summary>
    /// <param name="profileId">The workspace profile ID (e.g. studio, recording, training).</param>
    /// <returns>The loaded workspace profile, or null if not found or invalid.</returns>
    private async Task<WorkspaceProfile?> TryLoadEmbeddedLayoutAsync(string profileId)
    {
      if (string.IsNullOrWhiteSpace(profileId))
        return null;
      if (!ProfileIdToResourceName.TryGetValue(profileId.Trim(), out var resourceName))
        return null;

      try
      {
        // Unpackaged/self-contained: content is under BaseDirectory. Packaged MSIX may need ms-appx fallback (future).
        var baseDir = AppContext.BaseDirectory;
        var path = Path.Combine(baseDir, "Resources", "Workspaces", $"{resourceName}.json");
        if (!File.Exists(path))
        {
          System.Diagnostics.Debug.WriteLine($"[PanelStateService] Embedded workspace not found: {path}");
          return null;
        }

        var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
        var definition = JsonSerializer.Deserialize<EmbeddedWorkspaceDefinition>(json, _embeddedLayoutOptions);
        if (definition?.Layout == null || definition.Layout.Regions == null || definition.Layout.Regions.Count == 0)
          return null;

        return new WorkspaceProfile
        {
          Name = profileId,
          Description = definition.Name ?? profileId,
          Layout = definition.Layout,
          CreatedAt = DateTime.UtcNow,
          ModifiedAt = DateTime.UtcNow
        };
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[PanelStateService] Failed to load embedded workspace '{profileId}': {ex.Message}");
        return null;
      }
    }

    /// <summary>
    /// Loads a workspace profile by name.
    /// </summary>
    /// <param name="profileName">The profile name (e.g. studio, recording).</param>
    /// <returns>The workspace profile, or null if not found or on error.</returns>
    public async Task<WorkspaceProfile?> LoadWorkspaceProfileAsync(string profileName)
    {
      try
      {
        var filePath = Path.Combine(_workspaceProfilesDirectory, $"{profileName}.json");
        if (!File.Exists(filePath))
          return null;

        var json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize<WorkspaceProfile>(json, _jsonOptions);
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Failed to load workspace profile: {ex.Message}");
        return null;
      }
    }

    /// <summary>
    /// Lists all available workspace profiles.
    /// </summary>
    /// <returns>Ordered list of workspace profiles, including an ensured default if none exist.</returns>
    public async Task<List<WorkspaceProfile>> ListWorkspaceProfilesAsync()
    {
      var profiles = new List<WorkspaceProfile>();

      try
      {
        if (!Directory.Exists(_workspaceProfilesDirectory))
          return profiles;

        foreach (var file in Directory.GetFiles(_workspaceProfilesDirectory, "*.json"))
        {
          try
          {
            var json = await File.ReadAllTextAsync(file);
            var profile = JsonSerializer.Deserialize<WorkspaceProfile>(json, _jsonOptions);
            if (profile != null)
              profiles.Add(profile);
          }
          catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "PanelStateService.Task");
      }
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Failed to list workspace profiles: {ex.Message}");
      }

      // Ensure studio (canonical default) profile exists
      if (!profiles.Any(p => string.Equals(p.Name, "studio", StringComparison.OrdinalIgnoreCase)))
      {
        var studioProfile = await TryLoadEmbeddedLayoutAsync("studio").ConfigureAwait(false);
        if (studioProfile != null)
        {
          profiles.Insert(0, studioProfile);
          await SaveWorkspaceProfileAsync(studioProfile).ConfigureAwait(false);
        }
        else
        {
          var defaultProfile = new WorkspaceProfile
          {
            Name = "studio",
            Description = "Default workspace layout",
            Layout = new WorkspaceLayout { ProfileName = "studio", Version = "1.0", IsDefault = true },
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
          };
          profiles.Insert(0, defaultProfile);
          await SaveWorkspaceProfileAsync(defaultProfile).ConfigureAwait(false);
        }
      }

      return profiles.OrderBy(p => p.Name).ToList();
    }

    /// <summary>
    /// Deletes a workspace profile.
    /// </summary>
    /// <param name="profileName">The profile name to delete.</param>
    /// <returns>True if the profile was deleted; false if not found or if the profile is the protected default.</returns>
    public Task<bool> DeleteWorkspaceProfileAsync(string profileName)
    {
      if (string.Equals(profileName, "studio", StringComparison.OrdinalIgnoreCase))
        return Task.FromResult(false); // Cannot delete built-in default profile

      try
      {
        var filePath = Path.Combine(_workspaceProfilesDirectory, $"{profileName}.json");
        if (File.Exists(filePath))
        {
          File.Delete(filePath);
          return Task.FromResult(true);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Failed to delete workspace profile: {ex.Message}");
      }

      return Task.FromResult(false);
    }

    /// <summary>
    /// Renames a workspace profile.
    /// </summary>
    /// <param name="oldName">The current profile name.</param>
    /// <param name="newName">The new profile name.</param>
    /// <returns>True if renamed; false if source not found, studio protected, or new name conflicts.</returns>
    public async Task<bool> RenameWorkspaceProfileAsync(string oldName, string newName)
    {
      if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
        return false;
      if (!IsValidProfileName(newName, out var reason))
      {
        System.Diagnostics.Debug.WriteLine($"[PanelStateService] Invalid rename target: {reason}");
        return false;
      }
      if (string.Equals(oldName, "studio", StringComparison.OrdinalIgnoreCase))
        return false;

      // No-op: already renamed (exact match)
      if (string.Equals(oldName, newName, StringComparison.Ordinal))
        return true;

      var source = await LoadWorkspaceProfileAsync(oldName).ConfigureAwait(false);
      if (source == null)
        return false;

      var isCaseOnlyRename = oldName.Equals(newName, StringComparison.OrdinalIgnoreCase) && !oldName.Equals(newName, StringComparison.Ordinal);

      if (!isCaseOnlyRename)
      {
        var existing = await LoadWorkspaceProfileAsync(newName).ConfigureAwait(false);
        if (existing != null)
          return false;
      }

      var renamed = new WorkspaceProfile
      {
        Name = newName,
        Description = source.Description,
        Layout = source.Layout,
        CreatedAt = source.CreatedAt,
        ModifiedAt = DateTime.UtcNow
      };

      var oldPath = Path.Combine(_workspaceProfilesDirectory, $"{oldName}.json");
      var newPath = Path.Combine(_workspaceProfilesDirectory, $"{newName}.json");

      if (isCaseOnlyRename)
      {
        // Windows NTFS: oldPath and newPath resolve to same file. Use temp intermediate.
        var tempPath = Path.Combine(_workspaceProfilesDirectory, $"_rename_{Guid.NewGuid():N}.json");
        try
        {
          File.Move(oldPath, tempPath);
          File.Move(tempPath, newPath);
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine($"Failed case-only rename: {ex.Message}");
          if (File.Exists(tempPath))
          {
            // ALLOWED: empty catch - best effort restore; failure is acceptable, not actionable
            try { File.Move(tempPath, oldPath); } catch (Exception restoreEx) { ErrorLogger.LogWarning($"Best effort restore failed: {restoreEx.Message}", "PanelStateService.RenameWorkspaceProfileAsync"); }
          }
          return false;
        }
      }
      else
      {
        await SaveWorkspaceProfileAsync(renamed).ConfigureAwait(false);
        if (!TryDeleteWithRetry(oldPath))
          System.Diagnostics.Debug.WriteLine($"Failed to delete old profile file after rename: {oldPath}");
      }

      if (isCaseOnlyRename)
      {
        var json = JsonSerializer.Serialize(renamed, _jsonOptions);
        await File.WriteAllTextAsync(newPath, json).ConfigureAwait(false);
      }

      if (string.Equals(_currentWorkspaceProfile, oldName, StringComparison.OrdinalIgnoreCase))
        _currentWorkspaceProfile = newName;

      return true;
    }

    private static bool TryDeleteWithRetry(string path, int maxAttempts = 3)
    {
      for (var attempt = 0; attempt < maxAttempts; attempt++)
      {
        try
        {
          if (File.Exists(path))
            File.Delete(path);
          return true;
        }
        catch (IOException) when (attempt < maxAttempts - 1)
        {
          System.Threading.Thread.Sleep(100);
        }
      }
      return false;
    }

    /// <summary>
    /// Resets a workspace profile to its embedded template (if one exists).
    /// </summary>
    /// <param name="profileName">The profile name to reset.</param>
    /// <returns>True if reset; false if no embedded template exists for the profile.</returns>
    public async Task<bool> ResetWorkspaceProfileAsync(string profileName)
    {
      if (string.IsNullOrWhiteSpace(profileName))
        return false;

      var embedded = await TryLoadEmbeddedLayoutAsync(profileName).ConfigureAwait(false);
      if (embedded == null)
        return false;

      await SaveWorkspaceProfileAsync(embedded).ConfigureAwait(false);

      if (string.Equals(_currentWorkspaceProfile, profileName, StringComparison.OrdinalIgnoreCase))
      {
        _currentLayout = embedded.Layout;
        WorkspaceProfileChanged?.Invoke(this, new WorkspaceProfileChangedEventArgs
        {
          ProfileName = profileName,
          Layout = embedded.Layout
        });
      }

      return true;
    }

    /// <summary>
    /// Switches to a different workspace profile.
    /// </summary>
    /// <param name="profileName">The profile name or ID to switch to (e.g. studio, recording).</param>
    /// <returns>True if the switch succeeded; false on error.</returns>
    public async Task<bool> SwitchWorkspaceProfileAsync(string profileName)
    {
      try
      {
        // Save current profile to its disk file (not just settings) before switching.
        // This prevents state bleed between workspaces.
        if (!string.IsNullOrEmpty(_currentWorkspaceProfile))
        {
          var outgoing = new WorkspaceProfile
          {
            Name = _currentWorkspaceProfile,
            Layout = GetCurrentLayout(),
            ModifiedAt = DateTime.UtcNow
          };
          await SaveWorkspaceProfileAsync(outgoing).ConfigureAwait(false);
        }

        // Load new profile from disk first
        var profile = await LoadWorkspaceProfileAsync(profileName).ConfigureAwait(false);

        // If missing or layout has no regions, try embedded Resources/Workspaces JSON
        var usedEmbedded = false;
        if (profile == null || profile.Layout?.Regions == null || profile.Layout.Regions.Count == 0)
        {
          var embedded = await TryLoadEmbeddedLayoutAsync(profileName).ConfigureAwait(false);
          if (embedded != null)
          {
            profile = embedded;
            usedEmbedded = true;
          }
        }

        if (profile == null)
        {
          // Create new profile with empty layout if no embedded definition exists
          WorkspaceFallbackToEmpty?.Invoke(this, new WorkspaceFallbackToEmptyEventArgs(profileName));
          profile = new WorkspaceProfile
          {
            Name = profileName,
            Layout = new WorkspaceLayout
            {
              ProfileName = profileName,
              Version = "1.0"
            },
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
          };
          await SaveWorkspaceProfileAsync(profile).ConfigureAwait(false);
        }
        else if (usedEmbedded)
        {
          await SaveWorkspaceProfileAsync(profile).ConfigureAwait(false);
        }

        _currentWorkspaceProfile = profileName;
        _currentLayout = profile.Layout;

        // Save to settings
        await SaveCurrentWorkspaceAsync();

        if (profile.Layout != null)
        {
          WorkspaceProfileChanged?.Invoke(this, new WorkspaceProfileChangedEventArgs
          {
            ProfileName = profileName,
            Layout = profile.Layout
          });
        }

        return true;
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Failed to switch workspace profile: {ex.Message}");
        return false;
      }
    }

    /// <summary>
    /// Saves current workspace layout to settings.
    /// </summary>
    private void SaveCurrentWorkspace()
    {
      try
      {
        // IMPORTANT: Do NOT block the UI thread here.
        // This method is called from UI panel switching (PanelHost.OnContentChanged).
        // Sync-over-async can deadlock or stall the UI thread (especially when the backend is unavailable),
        // which breaks Gate C UI smoke and can freeze the app in production.
        _ = Task.Run(async () => await SaveCurrentWorkspaceAsync().ConfigureAwait(false));
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Failed to save workspace to settings: {ex.Message}");
      }
    }

    /// <summary>
    /// Saves current workspace layout to settings asynchronously.
    /// </summary>
    private async Task SaveCurrentWorkspaceAsync()
    {
      await _workspaceSettingsSaveGate.WaitAsync().ConfigureAwait(false);
      try
      {
        var layout = GetCurrentLayout();
        var settingsData = await _settingsService.LoadSettingsAsync().ConfigureAwait(false);
        settingsData.WorkspaceLayout = layout;
        await _settingsService.SaveSettingsAsync(settingsData).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Failed to save workspace to settings: {ex.Message}");
      }
      finally
      {
        _workspaceSettingsSaveGate.Release();
      }
    }

    /// <summary>
    /// Loads current workspace layout from settings. On first run or when missing, uses embedded Studio layout.
    /// </summary>
    private async Task LoadCurrentWorkspaceAsync()
    {
      try
      {
        var settingsData = await _settingsService.LoadSettingsAsync().ConfigureAwait(false);
        if (settingsData?.WorkspaceLayout != null && settingsData.WorkspaceLayout.Regions?.Count > 0)
        {
          _currentLayout = settingsData.WorkspaceLayout;
          // Migration: treat legacy "Default" as canonical "studio"
          _currentWorkspaceProfile = string.Equals(_currentLayout.ProfileName, "Default", StringComparison.OrdinalIgnoreCase)
            ? "studio"
            : _currentLayout.ProfileName;
          if (string.Equals(_currentLayout.ProfileName, "Default", StringComparison.OrdinalIgnoreCase))
            _currentLayout.ProfileName = "studio";
          return;
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[PanelStateService] Failed to load workspace: {ex.Message}");
      }

      _currentWorkspaceProfile = "studio";
      var embedded = await TryLoadEmbeddedLayoutAsync("studio").ConfigureAwait(false);
      var studioLayout = embedded?.Layout;
      if (studioLayout != null && studioLayout.Regions?.Count > 0)
        _currentLayout = studioLayout;
      else
        _currentLayout = new WorkspaceLayout { ProfileName = "studio", Version = "1.0", IsDefault = true };

      // Persist first-run studio layout so next launch loads from settings
      await SaveCurrentWorkspaceAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Event raised when workspace profile changes.
    /// </summary>
    public event EventHandler<WorkspaceProfileChangedEventArgs>? WorkspaceProfileChanged;

    /// <summary>
    /// Event raised when switching to a profile that has no saved layout and no embedded layout (fallback to empty).
    /// Subscribers (e.g. toolbar) may show a non-blocking warning toast.
    /// </summary>
    public event EventHandler<WorkspaceFallbackToEmptyEventArgs>? WorkspaceFallbackToEmpty;

    #region IUnifiedWorkspaceService Implementation

    /// <summary>
    /// Creates a new workspace profile from the current layout.
    /// </summary>
    /// <param name="name">The profile name.</param>
    /// <param name="description">Optional description.</param>
    /// <returns>The created workspace profile.</returns>
    public async Task<WorkspaceProfile> CreateWorkspaceProfileAsync(string name, string? description = null)
    {
      if (!IsValidProfileName(name, out var reason))
      {
        System.Diagnostics.Debug.WriteLine($"[PanelStateService] Invalid profile name: {reason}");
        throw new ArgumentException($"Invalid profile name: {reason}", nameof(name));
      }

      var profile = new WorkspaceProfile
      {
        Name = name,
        Description = description ?? $"Custom workspace: {name}",
        Layout = GetCurrentLayout(),
        CreatedAt = DateTime.UtcNow,
        ModifiedAt = DateTime.UtcNow
      };
      
      await SaveWorkspaceProfileAsync(profile);
      return profile;
    }

    /// <summary>
    /// Duplicates an existing workspace profile.
    /// </summary>
    /// <param name="sourceName">The name of the profile to copy.</param>
    /// <param name="newName">The name for the new profile.</param>
    /// <returns>The duplicated profile, or null if the source was not found.</returns>
    public async Task<WorkspaceProfile?> DuplicateWorkspaceProfileAsync(string sourceName, string newName)
    {
      if (!IsValidProfileName(newName, out var reason))
      {
        System.Diagnostics.Debug.WriteLine($"[PanelStateService] Invalid duplicate target name: {reason}");
        return null;
      }

      var source = await LoadWorkspaceProfileAsync(sourceName);
      if (source == null) return null;

      var duplicate = new WorkspaceProfile
      {
        Name = newName,
        Description = $"Copy of {sourceName}",
        Layout = source.Layout,
        CreatedAt = DateTime.UtcNow,
        ModifiedAt = DateTime.UtcNow
      };

      await SaveWorkspaceProfileAsync(duplicate);
      return duplicate;
    }

    /// <summary>
    /// Gets built-in workspace templates.
    /// </summary>
    public IReadOnlyList<WorkspaceTemplate> GetBuiltInTemplates() => _builtInTemplates;

    /// <summary>
    /// Applies a workspace template.
    /// </summary>
    /// <param name="templateId">The template ID (e.g. recording, mixing, synthesis).</param>
    public async Task ApplyTemplateAsync(string templateId)
    {
      var template = _builtInTemplates.FirstOrDefault(t => t.Id == templateId);
      if (template == null) return;

      // Create a profile from the template if it doesn't exist
      var existingProfile = await LoadWorkspaceProfileAsync(template.Name);
      if (existingProfile == null)
      {
        var profile = new WorkspaceProfile
        {
          Name = template.Name,
          Description = template.Description,
          Layout = template.Layout,
          CreatedAt = DateTime.UtcNow,
          ModifiedAt = DateTime.UtcNow
        };
        await SaveWorkspaceProfileAsync(profile);
      }

      await SwitchWorkspaceProfileAsync(template.Name);
    }

    /// <summary>
    /// Exports a workspace profile to JSON.
    /// </summary>
    /// <param name="profileName">The profile name to export.</param>
    /// <returns>JSON string of the profile, or "{}" if not found.</returns>
    public async Task<string> ExportWorkspaceAsync(string profileName)
    {
      var profile = await LoadWorkspaceProfileAsync(profileName);
      if (profile == null) return "{}";

      return JsonSerializer.Serialize(profile, _jsonOptions);
    }

    /// <summary>
    /// Imports a workspace profile from JSON.
    /// </summary>
    /// <param name="json">The JSON string of the workspace profile.</param>
    /// <returns>The imported profile, or null on parse error.</returns>
    public async Task<WorkspaceProfile?> ImportWorkspaceAsync(string json)
    {
      try
      {
        var profile = JsonSerializer.Deserialize<WorkspaceProfile>(json, _jsonOptions);
        if (profile == null) return null;
        if (!IsValidProfileName(profile.Name, out var reason))
        {
          System.Diagnostics.Debug.WriteLine($"[PanelStateService] Import rejected: invalid profile name: {reason}");
          return null;
        }

        // Ensure unique name
        var existing = await LoadWorkspaceProfileAsync(profile.Name);
        if (existing != null)
        {
          profile.Name = $"{profile.Name} (Imported)";
        }

        profile.ModifiedAt = DateTime.UtcNow;
        await SaveWorkspaceProfileAsync(profile);
        return profile;
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Failed to import workspace: {ex.Message}");
        return null;
      }
    }

    #endregion

    public void Dispose()
    {
      if (!_disposed)
      {
        // Save current workspace before disposing
        SaveCurrentWorkspace();
        _disposed = true;
      }
    }
  }

  /// <summary>
  /// Event args for workspace profile changes.
  /// </summary>
  public class WorkspaceProfileChangedEventArgs : EventArgs
  {
    public string ProfileName { get; set; } = string.Empty;
    public WorkspaceLayout Layout { get; set; } = null!;
  }

  /// <summary>
  /// Event args when workspace falls back to empty layout (no saved and no embedded profile).
  /// </summary>
  public class WorkspaceFallbackToEmptyEventArgs : EventArgs
  {
    public string ProfileName { get; set; } = string.Empty;
    public WorkspaceFallbackToEmptyEventArgs(string profileName) => ProfileName = profileName ?? string.Empty;
  }

  /// <summary>
  /// DTO for embedded Resources/Workspaces/*.json files.
  /// </summary>
  internal sealed class EmbeddedWorkspaceDefinition
  {
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? Version { get; set; }
    public WorkspaceLayout? Layout { get; set; }
  }
}