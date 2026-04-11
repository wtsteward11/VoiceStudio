// VoiceStudio - Transport Keyboard Shortcut Orchestration (Transport Coherence Wave 4 Phase 1)
// Extracted from MainWindow.xaml.cs per TRANSPORT_WAVE_4_SHELL_DECOMPOSITION_PLAN.md

using System;
using Windows.System;

namespace VoiceStudio.App.Services;

/// <summary>
/// Owns transport keyboard shortcut registration (Space, S, Ctrl+R).
/// Delegates play/stop to <see cref="IGlobalTransportOrchestrator"/>; Ctrl+R invokes the same recording navigation policy as the timeline Record button (caller-supplied action).
/// </summary>
public sealed class TransportShortcutCoordinator
{
    private readonly IGlobalTransportOrchestrator? _orchestrator;
    private KeyboardShortcutService? _shortcutService;
    private Action? _openRecordingPanel;
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
    /// <param name="openRecordingPanel">Opens the Recording panel (same policy as timeline Record): typically <c>NavigateToEvent</c> to <see cref="VoiceStudio.Core.Panels.PanelIds.Recording"/>.</param>
    public void Attach(KeyboardShortcutService shortcutService, Action? openRecordingPanel = null)
    {
        if (_attached)
            return;

        _shortcutService = shortcutService ?? throw new ArgumentNullException(nameof(shortcutService));
        _openRecordingPanel = openRecordingPanel;

        // Play: Space — delegate to orchestrator only
        _shortcutService.TryRegisterShortcut(
            "playback.play",
            VirtualKey.Space,
            VirtualKeyModifiers.None,
            () => _ = _orchestrator?.TogglePlaybackAsync(),
            "Play/Pause");

        // Stop: S — overrides default Escape binding for playback.stop (intentional; GAP-065)
        _shortcutService.TryRegisterShortcut(
            "playback.stop",
            VirtualKey.S,
            VirtualKeyModifiers.None,
            () => _orchestrator?.StopPlayback(),
            "Stop",
            ShortcutContext.Global,
            allowOverwrite: true);

        // Record: Ctrl+R — same navigation policy as timeline Record (no shell-only record toggle)
        _shortcutService.TryRegisterShortcut(
            "playback.record",
            VirtualKey.R,
            VirtualKeyModifiers.Control,
            () => _openRecordingPanel?.Invoke(),
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
        _openRecordingPanel = null;
        _attached = false;
    }
}
