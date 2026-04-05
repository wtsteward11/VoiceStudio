// VoiceStudio - Panel Architecture Phase 2: Context Manager
// Centralized shared read model for active state across panels

using System;
using VoiceStudio.Core.Events;

namespace VoiceStudio.Core.Services;

/// <summary>
/// Event raised when context changes.
/// </summary>
public class ContextChangedEventArgs : EventArgs
{
    /// <summary>
    /// Which context property changed.
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    /// The intent associated with the change.
    /// </summary>
    public InteractionIntent Intent { get; }

    /// <summary>
    /// Previous value (may be null).
    /// </summary>
    public string? OldValue { get; }

    /// <summary>
    /// New value (may be null).
    /// </summary>
    public string? NewValue { get; }

    public ContextChangedEventArgs(string propertyName, InteractionIntent intent, string? oldValue, string? newValue)
    {
        PropertyName = propertyName;
        Intent = intent;
        OldValue = oldValue;
        NewValue = newValue;
    }
}

/// <summary>
/// Event arguments for transport context changes (audioId, source, title).
/// Used when SetCurrentPlayable is called; allows listeners to react without polling.
/// </summary>
public sealed class TransportContextChangedEventArgs : EventArgs
{
    /// <summary>Backend audio ID for PlayBackendAudioIdAsync.</summary>
    public string? AudioId { get; }

    /// <summary>Typed source that owns the current playable.</summary>
    public TransportSource? Source { get; }

    /// <summary>Display title for the current playable.</summary>
    public string? Title { get; }

    public TransportContextChangedEventArgs(string? audioId, TransportSource? source, string? title)
    {
        AudioId = audioId;
        Source = source;
        Title = title;
    }
}

/// <summary>
/// Centralized manager for active/selected state across panels.
/// Provides a single source of truth for cross-panel coordination.
/// </summary>
public interface IContextManager
{
    #region Active State Properties

    /// <summary>
    /// Currently active voice profile ID.
    /// </summary>
    string? ActiveProfileId { get; }

    /// <summary>
    /// Currently active voice profile name.
    /// </summary>
    string? ActiveProfileName { get; }

    /// <summary>
    /// Currently active project ID.
    /// </summary>
    string? ActiveProjectId { get; }

    /// <summary>
    /// Currently active project name.
    /// </summary>
    string? ActiveProjectName { get; }

    /// <summary>
    /// Currently active/selected asset ID.
    /// </summary>
    string? ActiveAssetId { get; }

    /// <summary>
    /// Currently active/selected asset type.
    /// </summary>
    string? ActiveAssetType { get; }

    /// <summary>
    /// Currently active engine ID.
    /// </summary>
    string? ActiveEngineId { get; }

    /// <summary>
    /// Currently active job ID (for tracking in-progress operations).
    /// </summary>
    string? ActiveJobId { get; }

    /// <summary>
    /// Backend audio ID for the current playable (used by PlayBackendAudioIdAsync).
    /// Set when a panel publishes "I own current playable."
    /// </summary>
    string? CurrentPlayableAudioId { get; }

    /// <summary>
    /// Typed source that owns the current playable.
    /// </summary>
    TransportSource? CurrentPlayableSource { get; }

    /// <summary>
    /// Display title for the current playable.
    /// </summary>
    string? CurrentPlayableTitle { get; }

    /// <summary>
    /// Primary selected timeline clip ID (hero path), or null when no clip is selected.
    /// </summary>
    string? ActiveTimelinePrimaryClipId { get; }

    /// <summary>
    /// Track ID containing <see cref="ActiveTimelinePrimaryClipId"/>, or the timeline's focused track when no clip is selected.
    /// </summary>
    string? ActiveTimelinePrimaryTrackId { get; }

    /// <summary>
    /// Effect chain ID selected in the Effects Mixer for the current project context (export bake authority).
    /// </summary>
    string? ActiveEffectChainId { get; }

    #endregion

    #region State Setters

    /// <summary>
    /// Sets the active profile and publishes ProfileSelectedEvent.
    /// </summary>
    void SetActiveProfile(string? profileId, string? profileName = null, InteractionIntent intent = InteractionIntent.Navigation);

    /// <summary>
    /// Sets the active project and publishes ProjectChangedEvent.
    /// </summary>
    void SetActiveProject(string? projectId, string? projectName = null, InteractionIntent intent = InteractionIntent.Navigation);

    /// <summary>
    /// Sets the active asset and publishes AssetSelectedEvent.
    /// </summary>
    void SetActiveAsset(string? assetId, string? assetType = null, string? assetName = null, InteractionIntent intent = InteractionIntent.Navigation);

    /// <summary>
    /// Sets the active engine and publishes EngineChangedEvent.
    /// </summary>
    void SetActiveEngine(string? engineId, string? engineName = null, InteractionIntent intent = InteractionIntent.Navigation);

    /// <summary>
    /// Sets the active job ID (for tracking in-progress operations).
    /// </summary>
    void SetActiveJob(string? jobId, InteractionIntent intent = InteractionIntent.BackgroundProcess);

    /// <summary>
    /// Sets the current playable for global transport. Pass null for all args to clear.
    /// </summary>
    /// <param name="audioId">Backend audio ID for PlayBackendAudioIdAsync.</param>
    /// <param name="source">Typed transport source.</param>
    /// <param name="title">Display title.</param>
    void SetCurrentPlayable(string? audioId, TransportSource? source, string? title);

    /// <summary>
    /// Records the timeline panel's primary clip/track selection for cross-panel and transport-adjacent consumers.
    /// Does not publish panel events; use the <see cref="ContextChanged"/> event for reactions.
    /// </summary>
    void SetActiveTimelineSelection(string? primaryClipId, string? primaryTrackId, InteractionIntent intent = InteractionIntent.Navigation);

    /// <summary>
    /// Records the active effect chain for export and cross-panel coordination.
    /// </summary>
    void SetActiveEffectChain(string? chainId, string? projectIdForScope = null, InteractionIntent intent = InteractionIntent.Navigation);

    #endregion

    #region State Selectors

    /// <summary>
    /// Returns true if all requirements for voice cloning are met.
    /// </summary>
    bool IsVoiceCloningReady();

    /// <summary>
    /// Returns true if all requirements for synthesis are met.
    /// </summary>
    bool IsSynthesisReady();

    /// <summary>
    /// Returns true if there is an active job in progress.
    /// </summary>
    bool HasActiveJob();

    #endregion

    #region Events

    /// <summary>
    /// Raised when any context property changes.
    /// </summary>
    event EventHandler<ContextChangedEventArgs>? ContextChanged;

    /// <summary>
    /// Raised when transport context changes (SetCurrentPlayable). Carries full payload.
    /// </summary>
    event EventHandler<TransportContextChangedEventArgs>? TransportContextChanged;

    /// <summary>
    /// Raised when view state changes in a linked panel.
    /// </summary>
    event EventHandler<PanelViewStateChangedEventArgs>? ViewStateChanged;

    #endregion

    #region Panel Linking (Phase 6)

    /// <summary>
    /// Links two panels to synchronize their view state.
    /// </summary>
    /// <param name="panelId1">First panel ID.</param>
    /// <param name="panelId2">Second panel ID.</param>
    void LinkPanels(string panelId1, string panelId2);

    /// <summary>
    /// Unlinks two panels.
    /// </summary>
    /// <param name="panelId1">First panel ID.</param>
    /// <param name="panelId2">Second panel ID.</param>
    void UnlinkPanels(string panelId1, string panelId2);

    /// <summary>
    /// Gets all panels linked to the specified panel.
    /// </summary>
    /// <param name="panelId">The panel ID to query.</param>
    /// <returns>Collection of linked panel IDs.</returns>
    IReadOnlyCollection<string> GetLinkedPanels(string panelId);

    /// <summary>
    /// Checks if two panels are linked.
    /// </summary>
    bool ArePanelsLinked(string panelId1, string panelId2);

    /// <summary>
    /// Publishes a view state change to all linked panels.
    /// </summary>
    /// <param name="sourcePanelId">The panel that originated the change.</param>
    /// <param name="viewState">The view state to synchronize.</param>
    void PublishViewState(string sourcePanelId, PanelViewState viewState);

    #endregion
}

/// <summary>
/// View state for panel synchronization.
/// </summary>
public sealed class PanelViewState
{
    /// <summary>
    /// Horizontal scroll offset.
    /// </summary>
    public double ScrollX { get; init; }

    /// <summary>
    /// Vertical scroll offset.
    /// </summary>
    public double ScrollY { get; init; }

    /// <summary>
    /// Zoom level (1.0 = 100%).
    /// </summary>
    public double ZoomLevel { get; init; } = 1.0;

    /// <summary>
    /// Selected item IDs.
    /// </summary>
    public IReadOnlyList<string> SelectedIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Playhead/cursor position (for timeline panels).
    /// </summary>
    public double? CursorPosition { get; init; }

    /// <summary>
    /// Custom state data for panel-specific synchronization.
    /// </summary>
    public IReadOnlyDictionary<string, object>? CustomData { get; init; }
}

/// <summary>
/// Event arguments for view state changes.
/// </summary>
public sealed class PanelViewStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// Panel that originated the change.
    /// </summary>
    public string SourcePanelId { get; }

    /// <summary>
    /// The new view state.
    /// </summary>
    public PanelViewState ViewState { get; }

    /// <summary>
    /// IDs of linked panels that should synchronize.
    /// </summary>
    public IReadOnlyCollection<string> LinkedPanelIds { get; }

    public PanelViewStateChangedEventArgs(
        string sourcePanelId,
        PanelViewState viewState,
        IReadOnlyCollection<string> linkedPanelIds)
    {
        SourcePanelId = sourcePanelId;
        ViewState = viewState;
        LinkedPanelIds = linkedPanelIds;
    }
}
