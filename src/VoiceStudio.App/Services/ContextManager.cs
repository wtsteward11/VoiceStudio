// VoiceStudio - Panel Architecture Phase 2: Context Manager
// Implementation of centralized state management for cross-panel coordination
// Phase 5: Now uses AppStateStore for undo/redo support

using System;
using System.Collections.Generic;
using System.Linq;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Services;
using VoiceStudio.Core.State;
using VoiceStudio.Core.State.Commands;

namespace VoiceStudio.App.Services;

/// <summary>
/// Centralized manager for active/selected state across panels.
/// Single source of truth that publishes events on state changes.
/// Now backed by AppStateStore for undo/redo support (Phase 5).
/// </summary>
public class ContextManager : IContextManager
{
    private readonly IEventAggregator _eventAggregator;
    private readonly AppStateStore? _store;
    private readonly object _lock = new();
    private IDisposable? _storeSubscription;

    // Backing fields for state (used when store is not available)
    private string? _activeProfileId;
    private string? _activeProfileName;
    private string? _activeProjectId;
    private string? _activeProjectName;
    private string? _activeAssetId;
    private string? _activeAssetType;
    private string? _activeEngineId;
    private string? _activeJobId;
    private string? _currentPlayableAudioId;
    private TransportSource? _currentPlayableSource;
    private string? _currentPlayableTitle;
    private string? _activeTimelinePrimaryClipId;
    private string? _activeTimelinePrimaryTrackId;
    private string? _activeEffectChainId;

    /// <summary>
    /// Source panel ID used when publishing events.
    /// </summary>
    private const string SourcePanelId = "ContextManager";

    /// <summary>
    /// Creates a ContextManager with event aggregator only (legacy compatibility).
    /// </summary>
    public ContextManager(IEventAggregator eventAggregator)
        : this(eventAggregator, null)
    {
    }

    /// <summary>
    /// Creates a ContextManager with AppStateStore for undo/redo support.
    /// </summary>
    public ContextManager(IEventAggregator eventAggregator, AppStateStore? store)
    {
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _store = store;

        // Subscribe to store changes if available
        if (_store != null)
        {
            _storeSubscription = _store.Subscribe(
                state => (state.Profile.SelectedProfileId, state.Engines.ActiveEngineId, state.Project.CurrentProjectId),
                tuple => { /* State changes handled via StateChanged event */ });
            
            _store.StateChanged += OnStoreStateChanged;
        }
    }

    private void OnStoreStateChanged(object? sender, StateChangedEventArgs e)
    {
        // Detect profile changes
        if (e.PreviousState.Profile.SelectedProfileId != e.NewState.Profile.SelectedProfileId)
        {
            OnContextChanged(new ContextChangedEventArgs(
                nameof(ActiveProfileId),
                InteractionIntent.Navigation,
                e.PreviousState.Profile.SelectedProfileId,
                e.NewState.Profile.SelectedProfileId));

            if (e.NewState.Profile.SelectedProfileId != null)
            {
                _eventAggregator.Publish(new ProfileSelectedEvent(
                    SourcePanelId,
                    e.NewState.Profile.SelectedProfileId,
                    e.NewState.Profile.SelectedProfileName));
            }
        }

        // Detect engine changes
        if (e.PreviousState.Engines.ActiveEngineId != e.NewState.Engines.ActiveEngineId)
        {
            OnContextChanged(new ContextChangedEventArgs(
                nameof(ActiveEngineId),
                InteractionIntent.Navigation,
                e.PreviousState.Engines.ActiveEngineId,
                e.NewState.Engines.ActiveEngineId));

            if (e.NewState.Engines.ActiveEngineId != null)
            {
                _eventAggregator.Publish(new EngineChangedEvent(
                    SourcePanelId,
                    e.NewState.Engines.ActiveEngineId,
                    e.NewState.Engines.ActiveEngineName));
            }
        }

        // Detect project changes
        if (e.PreviousState.Project.CurrentProjectId != e.NewState.Project.CurrentProjectId)
        {
            string? oldChain;
            lock (_lock)
            {
                oldChain = _activeEffectChainId;
                _activeEffectChainId = null;
            }
            if (oldChain != null)
            {
                OnContextChanged(new ContextChangedEventArgs(
                    nameof(ActiveEffectChainId),
                    InteractionIntent.Navigation,
                    oldChain,
                    null));
            }

            OnContextChanged(new ContextChangedEventArgs(
                nameof(ActiveProjectId),
                InteractionIntent.Navigation,
                e.PreviousState.Project.CurrentProjectId,
                e.NewState.Project.CurrentProjectId));

            _eventAggregator.Publish(new ProjectChangedEvent(
                SourcePanelId,
                e.NewState.Project.CurrentProjectId,
                e.NewState.Project.CurrentProjectName));
        }
    }

    #region IContextManager Properties

    public string? ActiveProfileId
    {
        get
        {
            if (_store != null)
                return _store.State.Profile.SelectedProfileId;
            lock (_lock) return _activeProfileId;
        }
    }

    public string? ActiveProfileName
    {
        get
        {
            if (_store != null)
                return _store.State.Profile.SelectedProfileName;
            lock (_lock) return _activeProfileName;
        }
    }

    public string? ActiveProjectId
    {
        get
        {
            if (_store != null)
                return _store.State.Project.CurrentProjectId;
            lock (_lock) return _activeProjectId;
        }
    }

    public string? ActiveProjectName
    {
        get
        {
            if (_store != null)
                return _store.State.Project.CurrentProjectName;
            lock (_lock) return _activeProjectName;
        }
    }

    public string? ActiveAssetId
    {
        get { lock (_lock) return _activeAssetId; }
    }

    public string? ActiveAssetType
    {
        get { lock (_lock) return _activeAssetType; }
    }

    public string? ActiveEngineId
    {
        get
        {
            if (_store != null)
                return _store.State.Engines.ActiveEngineId;
            lock (_lock) return _activeEngineId;
        }
    }

    public string? ActiveJobId
    {
        get
        {
            if (_store != null)
                return _store.State.Jobs.CurrentJobId;
            lock (_lock) return _activeJobId;
        }
    }

    public string? CurrentPlayableAudioId
    {
        get { lock (_lock) return _currentPlayableAudioId; }
    }

    public TransportSource? CurrentPlayableSource
    {
        get { lock (_lock) return _currentPlayableSource; }
    }

    public string? CurrentPlayableTitle
    {
        get { lock (_lock) return _currentPlayableTitle; }
    }

    public string? ActiveTimelinePrimaryClipId
    {
        get { lock (_lock) return _activeTimelinePrimaryClipId; }
    }

    public string? ActiveTimelinePrimaryTrackId
    {
        get { lock (_lock) return _activeTimelinePrimaryTrackId; }
    }

    public string? ActiveEffectChainId
    {
        get { lock (_lock) return _activeEffectChainId; }
    }

    #endregion

    #region IContextManager Setters

    public void SetActiveProfile(string? profileId, string? profileName = null, InteractionIntent intent = InteractionIntent.Navigation)
    {
        // Use store if available (enables undo/redo)
        if (_store != null)
        {
            if (_store.State.Profile.SelectedProfileId == profileId)
                return; // No change

            var cmd = new SelectProfileCommand(
                profileId,
                profileName,
                _store.State.Profile.SelectedProfileId,
                _store.State.Profile.SelectedProfileName);
            _store.Dispatch(cmd);
            return;
        }

        // Fallback to local state
        string? oldValue;
        lock (_lock)
        {
            if (_activeProfileId == profileId)
                return; // No change

            oldValue = _activeProfileId;
            _activeProfileId = profileId;
            _activeProfileName = profileName;
        }

        // Raise context changed event
        OnContextChanged(new ContextChangedEventArgs(
            nameof(ActiveProfileId),
            intent,
            oldValue,
            profileId));

        // Publish panel event for backward compatibility
        if (profileId != null)
        {
            _eventAggregator.Publish(new ProfileSelectedEvent(
                SourcePanelId,
                profileId,
                profileName,
                intent));
        }
    }

    public void SetActiveProject(string? projectId, string? projectName = null, InteractionIntent intent = InteractionIntent.Navigation)
    {
        // Use store if available (enables undo/redo)
        if (_store != null)
        {
            if (_store.State.Project.CurrentProjectId == projectId)
                return; // No change

            var cmd = new UpdateProjectCommand(projectId, projectName);
            _store.Dispatch(cmd);
            return;
        }

        // Fallback to local state
        string? oldValue;
        string? clearedChain = null;
        lock (_lock)
        {
            if (_activeProjectId == projectId)
                return;

            oldValue = _activeProjectId;
            _activeProjectId = projectId;
            _activeProjectName = projectName;
            clearedChain = _activeEffectChainId;
            _activeEffectChainId = null;
        }

        if (clearedChain != null)
        {
            OnContextChanged(new ContextChangedEventArgs(
                nameof(ActiveEffectChainId),
                intent,
                clearedChain,
                null));
        }

        OnContextChanged(new ContextChangedEventArgs(
            nameof(ActiveProjectId),
            intent,
            oldValue,
            projectId));

        _eventAggregator.Publish(new ProjectChangedEvent(
            SourcePanelId,
            projectId,
            projectName));
    }

    public void SetActiveEffectChain(
        string? chainId,
        string? projectIdForScope = null,
        InteractionIntent intent = InteractionIntent.Navigation)
    {
        _ = projectIdForScope;

        string? oldValue;
        lock (_lock)
        {
            if (_activeEffectChainId == chainId)
                return;

            oldValue = _activeEffectChainId;
            _activeEffectChainId = chainId;
        }

        OnContextChanged(new ContextChangedEventArgs(
            nameof(ActiveEffectChainId),
            intent,
            oldValue,
            chainId));
    }

    public void SetActiveAsset(string? assetId, string? assetType = null, string? assetName = null, InteractionIntent intent = InteractionIntent.Navigation)
    {
        string? oldValue;
        lock (_lock)
        {
            if (_activeAssetId == assetId)
                return;

            oldValue = _activeAssetId;
            _activeAssetId = assetId;
            _activeAssetType = assetType;
        }

        OnContextChanged(new ContextChangedEventArgs(
            nameof(ActiveAssetId),
            intent,
            oldValue,
            assetId));

        if (assetId != null && assetType != null)
        {
            _eventAggregator.Publish(new AssetSelectedEvent(
                SourcePanelId,
                assetId,
                assetType,
                assetName,
                intent));
        }
    }

    public void SetCurrentPlayable(string? audioId, TransportSource? source, string? title)
    {
        lock (_lock)
        {
            if (_currentPlayableAudioId == audioId && _currentPlayableSource == source && _currentPlayableTitle == title)
                return;

            var oldAudioId = _currentPlayableAudioId;
            _currentPlayableAudioId = audioId;
            _currentPlayableSource = source;
            _currentPlayableTitle = title;

            // Use composite sentinel so listeners know "any transport field changed"
            OnContextChanged(new ContextChangedEventArgs(
                "CurrentPlayable",
                InteractionIntent.ImmediateUse,
                oldAudioId,
                audioId));
            TransportContextChanged?.Invoke(this, new TransportContextChangedEventArgs(audioId, source, title));
        }
    }

    public void SetActiveTimelineSelection(string? primaryClipId, string? primaryTrackId, InteractionIntent intent = InteractionIntent.Navigation)
    {
        string? oldClip;
        string? oldTrack;
        lock (_lock)
        {
            if (_activeTimelinePrimaryClipId == primaryClipId && _activeTimelinePrimaryTrackId == primaryTrackId)
                return;

            oldClip = _activeTimelinePrimaryClipId;
            oldTrack = _activeTimelinePrimaryTrackId;
            _activeTimelinePrimaryClipId = primaryClipId;
            _activeTimelinePrimaryTrackId = primaryTrackId;
        }

        if (oldClip != primaryClipId)
        {
            OnContextChanged(new ContextChangedEventArgs(
                nameof(ActiveTimelinePrimaryClipId),
                intent,
                oldClip,
                primaryClipId));
        }

        if (oldTrack != primaryTrackId)
        {
            OnContextChanged(new ContextChangedEventArgs(
                nameof(ActiveTimelinePrimaryTrackId),
                intent,
                oldTrack,
                primaryTrackId));
        }
    }

    public void SetActiveEngine(string? engineId, string? engineName = null, InteractionIntent intent = InteractionIntent.Navigation)
    {
        // Use store if available (enables undo/redo)
        if (_store != null)
        {
            if (_store.State.Engines.ActiveEngineId == engineId)
                return; // No change

            var cmd = new SelectEngineCommand(engineId, engineName);
            _store.Dispatch(cmd);
            return;
        }

        // Fallback to local state
        string? oldValue;
        lock (_lock)
        {
            if (_activeEngineId == engineId)
                return;

            oldValue = _activeEngineId;
            _activeEngineId = engineId;
        }

        OnContextChanged(new ContextChangedEventArgs(
            nameof(ActiveEngineId),
            intent,
            oldValue,
            engineId));

        if (engineId != null)
        {
            _eventAggregator.Publish(new EngineChangedEvent(
                SourcePanelId,
                engineId,
                engineName));
        }
    }

    public void SetActiveJob(string? jobId, InteractionIntent intent = InteractionIntent.BackgroundProcess)
    {
        // Use store if available
        if (_store != null)
        {
            if (_store.State.Jobs.CurrentJobId == jobId)
                return; // No change

            var cmd = new UpdateJobStateCommand(currentJobId: jobId);
            _store.Dispatch(cmd);

            // Raise event for compatibility (store doesn't auto-publish this)
            OnContextChanged(new ContextChangedEventArgs(
                nameof(ActiveJobId),
                intent,
                null,
                jobId));
            return;
        }

        // Fallback to local state
        string? oldValue;
        lock (_lock)
        {
            if (_activeJobId == jobId)
                return;

            oldValue = _activeJobId;
            _activeJobId = jobId;
        }

        OnContextChanged(new ContextChangedEventArgs(
            nameof(ActiveJobId),
            intent,
            oldValue,
            jobId));
    }

    #endregion

    #region IContextManager Selectors

    public bool IsVoiceCloningReady()
    {
        if (_store != null)
        {
            // Voice cloning requires an active audio asset and engine
            return !string.IsNullOrEmpty(_activeAssetId) // Asset not in store yet
                   && _activeAssetType == "audio"
                   && !string.IsNullOrEmpty(_store.State.Engines.ActiveEngineId);
        }

        lock (_lock)
        {
            // Voice cloning requires an active audio asset and engine
            return !string.IsNullOrEmpty(_activeAssetId)
                   && _activeAssetType == "audio"
                   && !string.IsNullOrEmpty(_activeEngineId);
        }
    }

    public bool IsSynthesisReady()
    {
        if (_store != null)
        {
            // Synthesis requires an active profile and engine
            return !string.IsNullOrEmpty(_store.State.Profile.SelectedProfileId)
                   && !string.IsNullOrEmpty(_store.State.Engines.ActiveEngineId);
        }

        lock (_lock)
        {
            // Synthesis requires an active profile and engine
            return !string.IsNullOrEmpty(_activeProfileId)
                   && !string.IsNullOrEmpty(_activeEngineId);
        }
    }

    public bool HasActiveJob()
    {
        if (_store != null)
        {
            return !string.IsNullOrEmpty(_store.State.Jobs.CurrentJobId);
        }

        lock (_lock)
        {
            return !string.IsNullOrEmpty(_activeJobId);
        }
    }

    #endregion

    #region Events

    public event EventHandler<ContextChangedEventArgs>? ContextChanged;
    public event EventHandler<TransportContextChangedEventArgs>? TransportContextChanged;
    public event EventHandler<PanelViewStateChangedEventArgs>? ViewStateChanged;

    private void OnContextChanged(ContextChangedEventArgs args)
    {
        ContextChanged?.Invoke(this, args);
    }

    #endregion

    #region Panel Linking (Phase 6)

    // Panel linking graph - stores bidirectional links
    private readonly Dictionary<string, HashSet<string>> _linkedPanels = new();

    public void LinkPanels(string panelId1, string panelId2)
    {
        if (string.IsNullOrEmpty(panelId1) || string.IsNullOrEmpty(panelId2))
            return;

        if (panelId1 == panelId2)
            return; // Cannot link to self

        lock (_lock)
        {
            // Add bidirectional link
            if (!_linkedPanels.ContainsKey(panelId1))
                _linkedPanels[panelId1] = new HashSet<string>();
            _linkedPanels[panelId1].Add(panelId2);

            if (!_linkedPanels.ContainsKey(panelId2))
                _linkedPanels[panelId2] = new HashSet<string>();
            _linkedPanels[panelId2].Add(panelId1);
        }
    }

    public void UnlinkPanels(string panelId1, string panelId2)
    {
        if (string.IsNullOrEmpty(panelId1) || string.IsNullOrEmpty(panelId2))
            return;

        lock (_lock)
        {
            if (_linkedPanels.TryGetValue(panelId1, out var links1))
                links1.Remove(panelId2);

            if (_linkedPanels.TryGetValue(panelId2, out var links2))
                links2.Remove(panelId1);
        }
    }

    public IReadOnlyCollection<string> GetLinkedPanels(string panelId)
    {
        if (string.IsNullOrEmpty(panelId))
            return Array.Empty<string>();

        lock (_lock)
        {
            if (_linkedPanels.TryGetValue(panelId, out var links))
                return links.ToArray();
            return Array.Empty<string>();
        }
    }

    public bool ArePanelsLinked(string panelId1, string panelId2)
    {
        if (string.IsNullOrEmpty(panelId1) || string.IsNullOrEmpty(panelId2))
            return false;

        lock (_lock)
        {
            if (_linkedPanels.TryGetValue(panelId1, out var links))
                return links.Contains(panelId2);
            return false;
        }
    }

    public void PublishViewState(string sourcePanelId, PanelViewState viewState)
    {
        if (string.IsNullOrEmpty(sourcePanelId) || viewState == null)
            return;

        IReadOnlyCollection<string> linkedPanels;
        lock (_lock)
        {
            linkedPanels = GetLinkedPanels(sourcePanelId);
        }

        if (linkedPanels.Count == 0)
            return;

        var args = new PanelViewStateChangedEventArgs(sourcePanelId, viewState, linkedPanels);
        ViewStateChanged?.Invoke(this, args);
    }

    #endregion
}
