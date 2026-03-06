// Phase 5.0: Service Unification
// Task 5.0.1: Unified Workspace Service Interface
// This interface unifies WorkspaceManager and PanelStateService

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;

namespace VoiceStudio.App.Services;

/// <summary>
/// Unified workspace service interface that combines workspace management
/// with panel state persistence.
/// </summary>
public interface IUnifiedWorkspaceService
{
    #region Workspace Management

    /// <summary>
    /// Gets the current workspace profile name.
    /// </summary>
    string CurrentWorkspaceProfile { get; }

    /// <summary>
    /// Gets the current workspace layout.
    /// </summary>
    WorkspaceLayout GetCurrentLayout();

    /// <summary>
    /// Lists all available workspace profiles.
    /// </summary>
    Task<List<WorkspaceProfile>> ListWorkspaceProfilesAsync();

    /// <summary>
    /// Switches to a different workspace profile.
    /// </summary>
    Task<bool> SwitchWorkspaceProfileAsync(string profileName);

    /// <summary>
    /// Saves a workspace profile.
    /// </summary>
    Task SaveWorkspaceProfileAsync(WorkspaceProfile profile);

    /// <summary>
    /// Loads a workspace profile by name.
    /// </summary>
    Task<WorkspaceProfile?> LoadWorkspaceProfileAsync(string profileName);

    /// <summary>
    /// Deletes a workspace profile.
    /// </summary>
    Task<bool> DeleteWorkspaceProfileAsync(string profileName);

    /// <summary>
    /// Creates a new workspace profile from the current layout.
    /// </summary>
    Task<WorkspaceProfile> CreateWorkspaceProfileAsync(string name, string? description = null);

    /// <summary>
    /// Duplicates an existing workspace profile.
    /// </summary>
    Task<WorkspaceProfile?> DuplicateWorkspaceProfileAsync(string sourceName, string newName);

    #endregion

    #region Panel State Management

    /// <summary>
    /// Saves panel state for a specific region.
    /// </summary>
    void SaveRegionState(PanelRegion region, string activePanelId, List<string> openedPanels);

    /// <summary>
    /// Saves panel state for a specific region including splitter ratios.
    /// </summary>
    void SaveRegionState(PanelRegion region, string activePanelId, List<string> openedPanels, double? widthRatio, double? heightRatio);

    /// <summary>
    /// Saves the collapsed state for a region.
    /// </summary>
    void SaveRegionCollapsedState(PanelRegion region, bool isCollapsed);

    /// <summary>
    /// Saves panel-specific state.
    /// </summary>
    void SavePanelState(PanelRegion region, string panelId, PanelState state);

    /// <summary>
    /// Gets panel state for a specific panel.
    /// </summary>
    PanelState? GetPanelState(PanelRegion region, string panelId);

    /// <summary>
    /// Gets region state for a specific region.
    /// </summary>
    RegionState? GetRegionState(PanelRegion region);

    #endregion

    #region Project State

    /// <summary>
    /// Saves panel state for a specific project.
    /// </summary>
    Task SaveProjectStateAsync(string projectId, WorkspaceLayout layout);

    /// <summary>
    /// Loads panel state for a specific project.
    /// </summary>
    Task<WorkspaceLayout?> LoadProjectStateAsync(string projectId);

    #endregion

    #region Workspace Templates

    /// <summary>
    /// Gets built-in workspace templates.
    /// </summary>
    IReadOnlyList<WorkspaceTemplate> GetBuiltInTemplates();

    /// <summary>
    /// Applies a workspace template.
    /// </summary>
    Task ApplyTemplateAsync(string templateId);

    #endregion

    #region Export/Import

    /// <summary>
    /// Exports a workspace profile to JSON.
    /// </summary>
    Task<string> ExportWorkspaceAsync(string profileName);

    /// <summary>
    /// Imports a workspace profile from JSON.
    /// </summary>
    Task<WorkspaceProfile?> ImportWorkspaceAsync(string json);

    #endregion

    #region Events

    /// <summary>
    /// Event raised when workspace profile changes.
    /// </summary>
    event EventHandler<WorkspaceProfileChangedEventArgs>? WorkspaceProfileChanged;

    #endregion
}

/// <summary>
/// Built-in workspace template definition.
/// </summary>
public class WorkspaceTemplate
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public WorkspaceLayout Layout { get; set; } = new();
}
