using System.Threading.Tasks;

namespace VoiceStudio.App.Services;

/// <summary>
/// Orchestrates global transport play/stop routing by source (Library, Timeline, Synthesis, etc.).
/// Extracted from MainWindow to reduce bloat and centralize ownership rules.
/// </summary>
public interface IGlobalTransportOrchestrator
{
    /// <summary>
    /// Toggles playback based on current transport context (Timeline vs Library/Synthesis/Recording/Analyzer).
    /// </summary>
    Task TogglePlaybackAsync();

    /// <summary>
    /// Stops playback based on current transport context.
    /// </summary>
    void StopPlayback();
}
