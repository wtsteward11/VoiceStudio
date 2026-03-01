// Copyright (c) VoiceStudio. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root.

using System.Collections.Generic;
using System.Linq;

namespace VoiceStudio.App.Core.Commands;

/// <summary>
/// Strongly-typed command IDs for the VoiceStudio command system.
/// </summary>
/// <remarks>
/// GAP-B19: This file provides compile-time safety for command routing.
/// All command IDs should be defined here rather than as magic strings.
/// 
/// Naming convention: category.action (e.g., "playback.play", "file.save")
/// </remarks>
public static class CommandIds
{
    #region File Operations

    /// <summary>Create a new project.</summary>
    public const string FileNew = "file.new";

    /// <summary>Open an existing project.</summary>
    public const string FileOpen = "file.open";

    /// <summary>Save the current project.</summary>
    public const string FileSave = "file.save";

    /// <summary>Save the current project with a new name.</summary>
    public const string FileSaveAs = "file.saveAs";

    /// <summary>Import audio file.</summary>
    public const string FileImport = "file.import";

    /// <summary>Export audio file.</summary>
    public const string FileExport = "file.export";

    /// <summary>Close the current project.</summary>
    public const string FileClose = "file.close";

    #endregion

    #region Edit Operations

    /// <summary>Undo the last action.</summary>
    public const string EditUndo = "edit.undo";

    /// <summary>Redo the last undone action.</summary>
    public const string EditRedo = "edit.redo";

    /// <summary>Cut selected content.</summary>
    public const string EditCut = "edit.cut";

    /// <summary>Copy selected content.</summary>
    public const string EditCopy = "edit.copy";

    /// <summary>Paste from clipboard.</summary>
    public const string EditPaste = "edit.paste";

    /// <summary>Select all content.</summary>
    public const string EditSelectAll = "edit.selectAll";

    /// <summary>Delete selected content.</summary>
    public const string EditDelete = "edit.delete";

    #endregion

    #region Playback Operations

    /// <summary>Start playback.</summary>
    public const string PlaybackPlay = "playback.play";

    /// <summary>Pause playback.</summary>
    public const string PlaybackPause = "playback.pause";

    /// <summary>Toggle play/pause.</summary>
    public const string PlaybackToggle = "playback.toggle";

    /// <summary>Stop playback.</summary>
    public const string PlaybackStop = "playback.stop";

    /// <summary>Start recording.</summary>
    public const string PlaybackRecord = "playback.record";

    /// <summary>Toggle loop mode.</summary>
    public const string PlaybackLoop = "playback.loop";

    /// <summary>Go to start (rewind).</summary>
    public const string PlaybackRewind = "playback.rewind";

    /// <summary>Go to end (fast forward).</summary>
    public const string PlaybackForward = "playback.forward";

    /// <summary>Step backward one frame.</summary>
    public const string PlaybackStepBack = "playback.stepBack";

    /// <summary>Step forward one frame.</summary>
    public const string PlaybackStepForward = "playback.stepForward";

    /// <summary>Seek to a specific position.</summary>
    public const string PlaybackSeek = "playback.seek";

    #endregion

    #region Synthesis Operations

    /// <summary>Generate speech audio.</summary>
    public const string SynthesisGenerate = "synthesis.generate";

    /// <summary>Preview voice without saving.</summary>
    public const string SynthesisPreview = "synthesis.preview";

    /// <summary>Regenerate the last synthesis.</summary>
    public const string SynthesisRegenerate = "synthesis.regenerate";

    /// <summary>Stop synthesis in progress.</summary>
    public const string SynthesisStop = "synthesis.stop";

    /// <summary>Add synthesized audio to timeline.</summary>
    public const string SynthesisAddToTimeline = "synthesis.addToTimeline";

    #endregion

    #region Timeline Operations

    /// <summary>Add a new track to the timeline.</summary>
    public const string TimelineAddTrack = "timeline.addTrack";

    /// <summary>Delete selected clip(s).</summary>
    public const string TimelineDeleteClip = "timeline.deleteClip";

    /// <summary>Move selected clip(s).</summary>
    public const string TimelineMoveClip = "timeline.moveClip";

    /// <summary>Split clip at playhead.</summary>
    public const string TimelineSplitClip = "timeline.splitClip";

    /// <summary>Merge selected clips.</summary>
    public const string TimelineMergeClips = "timeline.mergeClips";

    /// <summary>Select all clips in track.</summary>
    public const string TimelineSelectAllInTrack = "timeline.selectAllInTrack";

    #endregion

    #region View Operations

    /// <summary>Zoom in.</summary>
    public const string ViewZoomIn = "view.zoomIn";

    /// <summary>Zoom out.</summary>
    public const string ViewZoomOut = "view.zoomOut";

    /// <summary>Zoom to fit content.</summary>
    public const string ViewZoomFit = "view.zoomFit";

    /// <summary>Toggle fullscreen mode.</summary>
    public const string ViewFullscreen = "view.fullscreen";

    /// <summary>Toggle fullscreen mode (alias).</summary>
    public const string ViewToggleFullscreen = "view.toggleFullscreen";

    /// <summary>Open command palette.</summary>
    public const string ViewCommandPalette = "view.commandPalette";

    #endregion

    #region Panel Navigation

    /// <summary>Switch to synthesis panel.</summary>
    public const string PanelSynthesis = "panel.synthesis";

    /// <summary>Switch to library panel.</summary>
    public const string PanelLibrary = "panel.library";

    /// <summary>Switch to profiles panel.</summary>
    public const string PanelProfiles = "panel.profiles";

    /// <summary>Switch to effects panel.</summary>
    public const string PanelEffects = "panel.effects";

    /// <summary>Open settings panel.</summary>
    public const string PanelSettings = "panel.settings";

    /// <summary>Cycle to next panel (Tab navigation).</summary>
    public const string PanelCycleNext = "panel.cycleNext";

    /// <summary>Cycle to previous panel (Shift+Tab navigation).</summary>
    public const string PanelCyclePrevious = "panel.cyclePrevious";

    /// <summary>Focus the left panel region.</summary>
    public const string PanelFocusLeft = "panel.focusLeft";

    /// <summary>Focus the center panel region.</summary>
    public const string PanelFocusCenter = "panel.focusCenter";

    /// <summary>Focus the right panel region.</summary>
    public const string PanelFocusRight = "panel.focusRight";

    /// <summary>Focus the bottom panel region.</summary>
    public const string PanelFocusBottom = "panel.focusBottom";

    #endregion

    #region Tools

    /// <summary>Open command palette.</summary>
    public const string ToolsCommandPalette = "tools.commandPalette";

    /// <summary>Open search.</summary>
    public const string ToolsSearch = "tools.search";

    /// <summary>Open help.</summary>
    public const string ToolsHelp = "tools.help";

    #endregion

    #region Dialog Operations

    /// <summary>Close the current dialog (GAP-B08/B09).</summary>
    public const string DialogClose = "dialog.close";

    #endregion

    #region Validation

    /// <summary>
    /// All known command IDs for validation purposes.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        // File
        FileNew, FileOpen, FileSave, FileSaveAs, FileImport, FileExport, FileClose,
        
        // Edit
        EditUndo, EditRedo, EditCut, EditCopy, EditPaste, EditSelectAll, EditDelete,
        
        // Playback
        PlaybackPlay, PlaybackPause, PlaybackToggle, PlaybackStop, PlaybackRecord,
        PlaybackLoop, PlaybackRewind, PlaybackForward, PlaybackStepBack, PlaybackStepForward, PlaybackSeek,
        
        // Synthesis
        SynthesisGenerate, SynthesisPreview, SynthesisRegenerate, SynthesisStop, SynthesisAddToTimeline,
        
        // Timeline
        TimelineAddTrack, TimelineDeleteClip, TimelineMoveClip, TimelineSplitClip, TimelineMergeClips, TimelineSelectAllInTrack,
        
        // View
        ViewZoomIn, ViewZoomOut, ViewZoomFit, ViewFullscreen, ViewToggleFullscreen, ViewCommandPalette,
        
        // Panel
        PanelSynthesis, PanelLibrary, PanelProfiles, PanelEffects, PanelSettings,
        PanelCycleNext, PanelCyclePrevious, PanelFocusLeft, PanelFocusCenter, PanelFocusRight, PanelFocusBottom,
        
        // Tools
        ToolsCommandPalette, ToolsSearch, ToolsHelp,
        
        // Dialog
        DialogClose
    };

    /// <summary>
    /// Command IDs by category for grouped display.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ByCategory { get; } = new Dictionary<string, IReadOnlyList<string>>
    {
        ["file"] = new[] { FileNew, FileOpen, FileSave, FileSaveAs, FileImport, FileExport, FileClose },
        ["edit"] = new[] { EditUndo, EditRedo, EditCut, EditCopy, EditPaste, EditSelectAll, EditDelete },
        ["playback"] = new[] { PlaybackPlay, PlaybackPause, PlaybackToggle, PlaybackStop, PlaybackRecord, PlaybackLoop, PlaybackRewind, PlaybackForward, PlaybackStepBack, PlaybackStepForward, PlaybackSeek },
        ["synthesis"] = new[] { SynthesisGenerate, SynthesisPreview, SynthesisRegenerate, SynthesisStop, SynthesisAddToTimeline },
        ["timeline"] = new[] { TimelineAddTrack, TimelineDeleteClip, TimelineMoveClip, TimelineSplitClip, TimelineMergeClips, TimelineSelectAllInTrack },
        ["view"] = new[] { ViewZoomIn, ViewZoomOut, ViewZoomFit, ViewFullscreen, ViewToggleFullscreen, ViewCommandPalette },
        ["panel"] = new[] { PanelSynthesis, PanelLibrary, PanelProfiles, PanelEffects, PanelSettings, PanelCycleNext, PanelCyclePrevious, PanelFocusLeft, PanelFocusCenter, PanelFocusRight, PanelFocusBottom },
        ["tools"] = new[] { ToolsCommandPalette, ToolsSearch, ToolsHelp },
        ["dialog"] = new[] { DialogClose }
    };

    /// <summary>
    /// Checks if a command ID is known/registered.
    /// </summary>
    /// <param name="commandId">The command ID to validate.</param>
    /// <returns>True if the command ID is known; otherwise, false.</returns>
    public static bool IsKnown(string commandId)
    {
        return !string.IsNullOrEmpty(commandId) && All.Contains(commandId);
    }

    /// <summary>
    /// Gets the category prefix from a command ID.
    /// </summary>
    /// <param name="commandId">The command ID (e.g., "playback.play").</param>
    /// <returns>The category (e.g., "playback"), or null if invalid.</returns>
    public static string? GetCategory(string commandId)
    {
        if (string.IsNullOrEmpty(commandId))
            return null;

        var dotIndex = commandId.IndexOf('.');
        return dotIndex > 0 ? commandId[..dotIndex] : null;
    }

    /// <summary>
    /// Gets the action part from a command ID.
    /// </summary>
    /// <param name="commandId">The command ID (e.g., "playback.play").</param>
    /// <returns>The action (e.g., "play"), or null if invalid.</returns>
    public static string? GetAction(string commandId)
    {
        if (string.IsNullOrEmpty(commandId))
            return null;

        var dotIndex = commandId.IndexOf('.');
        return dotIndex > 0 && dotIndex < commandId.Length - 1
            ? commandId[(dotIndex + 1)..]
            : null;
    }

    /// <summary>
    /// Validates a command ID format and existence.
    /// </summary>
    /// <param name="commandId">The command ID to validate.</param>
    /// <param name="error">Error message if validation fails.</param>
    /// <returns>True if valid; otherwise, false.</returns>
    public static bool TryValidate(string commandId, out string? error)
    {
        if (string.IsNullOrWhiteSpace(commandId))
        {
            error = "Command ID cannot be null or empty.";
            return false;
        }

        if (!commandId.Contains('.'))
        {
            error = $"Command ID '{commandId}' must follow 'category.action' format.";
            return false;
        }

        var category = GetCategory(commandId);
        var action = GetAction(commandId);

        if (string.IsNullOrEmpty(category) || string.IsNullOrEmpty(action))
        {
            error = $"Command ID '{commandId}' has invalid format. Expected 'category.action'.";
            return false;
        }

        if (!IsKnown(commandId))
        {
            error = $"Command ID '{commandId}' is not registered in CommandIds. Add it to the appropriate region.";
            return false;
        }

        error = null;
        return true;
    }

    #endregion
}
