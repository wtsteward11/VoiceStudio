// VoiceStudio - Transport Keyboard Shortcut Orchestration (Transport Coherence Wave 4 Phase 1)
// Extracted from MainWindow.xaml.cs per TRANSPORT_WAVE_4_SHELL_DECOMPOSITION_PLAN.md

using System;
using Windows.System;

namespace VoiceStudio.App.Services;

/// <summary>
/// Owns transport keyboard shortcut registration (Space, S, Ctrl+R).
/// Delegates play/stop to IGlobalTransportOrchestrator; record via callback until unified.
/// </summary>
public sealed class TransportShortcutCoordinator
{
    private readonly IGlobalTransportOrchestrator? _orchestrator;
    private KeyboardShortcutService? _shortcutService;
    private Action? _toggleRecord;
    private bool _attached;

    /// <summary>
    /// Creates a coordinator that delegates play/stop to the orchestrator.
    /// </summary>
    public TransportShortcutCoordinator(IGlobalTransportOrchestrator? orchestrator)
    {
        _orchestrator = orchestrator;
    }

    /// <summary>
    /// Registers playback shortcuts. Call from MainWindow Loaded.
    /// </summary>
    /// <param name="shortcutService">Keyboard shortcut service.</param>
    /// <param name="toggleRecord">Record callback until recording transport is unified.</param>
    public void Attach(KeyboardShortcutService shortcutService, Action? toggleRecord = null)
    {
        if (_attached)
            return;

        _shortcutService = shortcutService ?? throw new ArgumentNullException(nameof(shortcutService));
        _toggleRecord = toggleRecord;

        // Play: Space — delegate to orchestrator only
        _shortcutService.RegisterShortcut(
            "playback.play",
            VirtualKey.Space,
            VirtualKeyModifiers.None,
            () => _ = _orchestrator?.TogglePlaybackAsync(),
            "Play/Pause");

        // Stop: S — delegate to orchestrator only
        _shortcutService.RegisterShortcut(
            "playback.stop",
            VirtualKey.S,
            VirtualKeyModifiers.None,
            () => _orchestrator?.StopPlayback(),
            "Stop");

        // Record: Ctrl+R — callback until recording is unified
        _shortcutService.RegisterShortcut(
            "playback.record",
            VirtualKey.R,
            VirtualKeyModifiers.Control,
            () => _toggleRecord?.Invoke(),
            "Record");

        _attached = true;
    }

    /// <summary>
    /// Unregisters shortcuts. Call from MainWindow Cleanup.
    /// </summary>
    public void Detach()
    {
        if (!_attached || _shortcutService == null)
            return;

        _shortcutService.UnregisterShortcut("playback.play");
        _shortcutService.UnregisterShortcut("playback.stop");
        _shortcutService.UnregisterShortcut("playback.record");

        _shortcutService = null;
        _toggleRecord = null;
        _attached = false;
    }
}
