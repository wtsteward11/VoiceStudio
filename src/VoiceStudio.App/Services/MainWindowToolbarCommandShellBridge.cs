using System;

namespace VoiceStudio.App.Services;

/// <summary>
/// GAP-008 Slice 9: MainWindow shell wiring for toolbar import command only — not toolbar customization (Slice 7);
/// not command palette (Slice 8); not search overlay; not tool catalog.
/// </summary>
public sealed class MainWindowToolbarCommandShellBridge : IToolbarShellImportFromToolbar
{
    private Action? _importAudio;

    /// <summary>
    /// Wires the import handler from <c>MainWindow</c> composition (call once when shell is ready).
    /// </summary>
    public void WireImportAudioHandler(Action importAudio)
    {
        _importAudio = importAudio ?? throw new ArgumentNullException(nameof(importAudio));
    }

    /// <inheritdoc />
    public void RequestImportAudio()
    {
        var handler = _importAudio
            ?? throw new InvalidOperationException(
                "Toolbar import shell is not wired: MainWindow must call WireImportAudioHandler before toolbar import is used.");
        handler();
    }
}
