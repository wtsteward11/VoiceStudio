using System;
using System.Collections.Generic;
using VoiceStudio.Core.Panels;

namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// Workspace layout configuration storing panel states and arrangement.
  /// Implements IDEA 3: Panel State Persistence with Workspace Profiles.
  /// </summary>
  public class WorkspaceLayout
  {
    /// <summary>
    /// Workspace profile name (e.g., "Recording", "Mixing", "Analysis", "Default").
    /// </summary>
    public string ProfileName { get; set; } = "Default";

    /// <summary>
    /// Version of the layout schema.
    /// </summary>
    public string Version { get; set; } = "1.0";

    /// <summary>
    /// Panel states for each region.
    /// </summary>
    public List<RegionState> Regions { get; set; } = new List<RegionState>();

    /// <summary>
    /// Timestamp when layout was created/modified.
    /// </summary>
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this is the default workspace profile.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Panel IDs pinned/favorited in the Tool Catalog (shown at top of list).
    /// </summary>
    public HashSet<string> PinnedPanelIds { get; set; } = new HashSet<string>();
  }

  /// <summary>
  /// Panel state for a specific region.
  /// </summary>
  public class RegionState
  {
    /// <summary>
    /// Panel region (Left, Center, Right, Bottom).
    /// </summary>
    public PanelRegion Region { get; set; }

    /// <summary>
    /// ID of the currently active panel in this region.
    /// </summary>
    public string ActivePanelId { get; set; } = string.Empty;

    /// <summary>
    /// List of panel IDs that are open in this region (for tab system).
    /// </summary>
    public List<string> OpenedPanels { get; set; } = new List<string>();

    /// <summary>
    /// Panel-specific state data (e.g., scroll position, selected items, filters).
    /// </summary>
    public Dictionary<string, PanelState> PanelStates { get; set; } = new Dictionary<string, PanelState>();

    /// <summary>
    /// Region width/height ratios (if resizable).
    /// </summary>
    public double? WidthRatio { get; set; }

    public double? HeightRatio { get; set; }

    /// <summary>
    /// Whether the region is collapsed (minimized).
    /// </summary>
    public bool IsCollapsed { get; set; }
  }

  /// <summary>
  /// State information for a specific panel.
  /// </summary>
  public class PanelState
  {
    /// <summary>
    /// Panel ID.
    /// </summary>
    public string PanelId { get; set; } = string.Empty;

    /// <summary>
    /// Scroll position (for scrollable panels).
    /// </summary>
    public double? ScrollPosition { get; set; }

    /// <summary>
    /// Selected item ID (e.g., selected profile, audio file, timeline position).
    /// </summary>
    public string? SelectedItemId { get; set; }

    /// <summary>
    /// Timeline-specific state (zoom level, scroll position, selected region).
    /// </summary>
    public TimelinePanelState? TimelineState { get; set; }

    /// <summary>
    /// Filter states (for LibraryView, PresetLibraryView).
    /// </summary>
    public Dictionary<string, object>? FilterStates { get; set; }

    /// <summary>
    /// Expanded/collapsed sections (for panels with collapsible sections).
    /// </summary>
    public Dictionary<string, bool>? ExpandedSections { get; set; }

    /// <summary>
    /// Custom panel-specific state data.
    /// </summary>
    public Dictionary<string, object>? CustomState { get; set; }
  }

  /// <summary>
  /// Timeline-specific panel state.
  /// </summary>
  public class TimelinePanelState
  {
    /// <summary>
    /// Timeline zoom level.
    /// </summary>
    public double ZoomLevel { get; set; } = 1.0;

    /// <summary>
    /// Horizontal scroll position in pixels.
    /// </summary>
    public double ScrollPosition { get; set; }

    /// <summary>
    /// Selected timeline position in seconds.
    /// </summary>
    public double? SelectedPosition { get; set; }

    /// <summary>
    /// Selected track ID.
    /// </summary>
    public string? SelectedTrackId { get; set; }

    /// <summary>
    /// Selected clip ID.
    /// </summary>
    public string? SelectedClipId { get; set; }

    /// <summary>
    /// Selected time range (start and end in seconds).
    /// </summary>
    public TimeRange? SelectedRange { get; set; }

    /// <summary>
    /// Playhead position in seconds.
    /// </summary>
    public double? PlayheadPosition { get; set; }
  }

  /// <summary>
  /// Time range selection.
  /// </summary>
  public class TimeRange
  {
    public double Start { get; set; }
    public double End { get; set; }
  }

  /// <summary>
  /// Workspace profile with layout configuration.
  /// </summary>
  public class WorkspaceProfile
  {
    /// <summary>
    /// Profile name (e.g., "Recording", "Mixing", "Analysis").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Profile description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Workspace layout configuration.
    /// </summary>
    public WorkspaceLayout Layout { get; set; } = new WorkspaceLayout();

    /// <summary>
    /// Timestamp when profile was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when profile was last modified.
    /// </summary>
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
  }
}