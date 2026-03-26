using System;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// Holds the delegate to resolve the Timeline transport controller for orchestration.
/// MainWindow sets this in Loaded. Decouples orchestrator from UI-tree lookup.
/// </summary>
public sealed class TransportOrchestrationBootstrap
{
    private Func<ITimelineTransportController?>? _getTimelineController;

    /// <summary>
    /// Sets the delegate that returns the Timeline transport controller. Called by MainWindow in Loaded.
    /// </summary>
    public void SetGetTimelineController(Func<ITimelineTransportController?> getter)
    {
        _getTimelineController = getter ?? throw new ArgumentNullException(nameof(getter));
    }

    /// <summary>
    /// Returns the Timeline transport controller, or null if not set or not available.
    /// </summary>
    public ITimelineTransportController? GetTimelineController() => _getTimelineController?.Invoke();
}
