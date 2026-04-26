namespace VoiceStudio.App.Services;

/// <summary>
/// GAP-008 Slice 9: Shell port for toolbar-initiated import audio — implemented by
/// <see cref="MainWindowToolbarCommandShellBridge"/> and wired from <c>MainWindow</c> (no <c>App.MainWindowInstance</c>).
/// </summary>
public interface IToolbarShellImportFromToolbar
{
    /// <summary>
    /// Invokes the same import path as File menu / other shell entry points (e.g. <see cref="VoiceStudio.App.MainWindow.ImportAudioFile"/>).
    /// </summary>
    void RequestImportAudio();
}
