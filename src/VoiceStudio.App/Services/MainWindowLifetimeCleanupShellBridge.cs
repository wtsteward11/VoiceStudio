using System;
using System.Diagnostics;
using System.IO;

namespace VoiceStudio.App.Services;

/// <summary>
/// GAP-008 Slice 13: <see cref="Microsoft.UI.Xaml.Window.Closed"/> prelude + idempotent teardown for <see cref="MainWindow"/>.
/// Not pending jump-list/file activation (later slices); not notification center Loaded wire.
/// </summary>
public sealed class MainWindowClosedPreludeChannels
{
    public required Action StopStatusBarTimer { get; init; }
    public required Action CancelLayoutSaveDebouncer { get; init; }
    public required Action SaveWorkspaceLayout { get; init; }
    public required Action TryMarkCleanShutdown { get; init; }
}

/// <summary>
/// Channel surface for <see cref="MainWindowLifetimeCleanupShellBridge.RunCleanupCore"/> — one delegate per teardown step.
/// </summary>
public sealed class MainWindowLifetimeCleanupCoreChannels
{
    public required Func<bool> GetDisposed { get; init; }
    public required Action SetDisposed { get; init; }
    public required Action DisposeClockTimer { get; init; }
    public required Action DisposePreviewHideTimer { get; init; }
    public required Action DisposeQuickSwitchHideTimer { get; init; }
    public required Action CancelDebouncerAndSaveWorkspace { get; init; }
    public required Action UnsubscribeContentKeyDown { get; init; }
    public required Action UnsubscribeWindowActivated { get; init; }
    public required Action UnsubscribeWindowClosed { get; init; }
    public required Action UnsubscribeWorkspaceProfileChanged { get; init; }
    public required Action DetachNavigationService { get; init; }
    public required Action UnsubscribeStartupOverlay { get; init; }
    public required Action DisposeSessionLifecycle { get; init; }
    public required Action DetachTransportShortcutsAndClear { get; init; }
    public required Action UnsubscribeStatusBarCoordinator { get; init; }
    public required Action DisposeJumpListServiceBestEffort { get; init; }
    public required Action DisposeTaskbarProgressServiceBestEffort { get; init; }
    public required Action CleanupNotificationCenterViewModel { get; init; }
    public required Action CleanupGlobalTransportEvents { get; init; }
    public required Action UnsubscribeShellChromeEvents { get; init; }
}

public sealed class MainWindowLifetimeCleanupShellBridge
{
    private readonly MainWindowClosedPreludeChannels _prelude;
    private readonly MainWindowLifetimeCleanupCoreChannels _core;

    public MainWindowLifetimeCleanupShellBridge(
        MainWindowClosedPreludeChannels prelude,
        MainWindowLifetimeCleanupCoreChannels core)
    {
        _prelude = prelude ?? throw new ArgumentNullException(nameof(prelude));
        _core = core ?? throw new ArgumentNullException(nameof(core));
        ArgumentNullException.ThrowIfNull(prelude.StopStatusBarTimer);
        ArgumentNullException.ThrowIfNull(prelude.CancelLayoutSaveDebouncer);
        ArgumentNullException.ThrowIfNull(prelude.SaveWorkspaceLayout);
        ArgumentNullException.ThrowIfNull(prelude.TryMarkCleanShutdown);
        ArgumentNullException.ThrowIfNull(core.GetDisposed);
        ArgumentNullException.ThrowIfNull(core.SetDisposed);
        ArgumentNullException.ThrowIfNull(core.DisposeClockTimer);
        ArgumentNullException.ThrowIfNull(core.DisposePreviewHideTimer);
        ArgumentNullException.ThrowIfNull(core.DisposeQuickSwitchHideTimer);
        ArgumentNullException.ThrowIfNull(core.CancelDebouncerAndSaveWorkspace);
        ArgumentNullException.ThrowIfNull(core.UnsubscribeContentKeyDown);
        ArgumentNullException.ThrowIfNull(core.UnsubscribeWindowActivated);
        ArgumentNullException.ThrowIfNull(core.UnsubscribeWindowClosed);
        ArgumentNullException.ThrowIfNull(core.UnsubscribeWorkspaceProfileChanged);
        ArgumentNullException.ThrowIfNull(core.DetachNavigationService);
        ArgumentNullException.ThrowIfNull(core.UnsubscribeStartupOverlay);
        ArgumentNullException.ThrowIfNull(core.DisposeSessionLifecycle);
        ArgumentNullException.ThrowIfNull(core.DetachTransportShortcutsAndClear);
        ArgumentNullException.ThrowIfNull(core.UnsubscribeStatusBarCoordinator);
        ArgumentNullException.ThrowIfNull(core.DisposeJumpListServiceBestEffort);
        ArgumentNullException.ThrowIfNull(core.DisposeTaskbarProgressServiceBestEffort);
        ArgumentNullException.ThrowIfNull(core.CleanupNotificationCenterViewModel);
        ArgumentNullException.ThrowIfNull(core.CleanupGlobalTransportEvents);
        ArgumentNullException.ThrowIfNull(core.UnsubscribeShellChromeEvents);
    }

    public void OnClosedPrelude()
    {
        _prelude.StopStatusBarTimer();
        _prelude.CancelLayoutSaveDebouncer();
        _prelude.SaveWorkspaceLayout();
        _prelude.TryMarkCleanShutdown();
    }

    public void RunCleanupCore()
    {
        if (_core.GetDisposed())
        {
            return;
        }

        CleanupTemporaryAudioFiles();
        _core.DisposeClockTimer();
        _core.DisposePreviewHideTimer();
        _core.DisposeQuickSwitchHideTimer();
        _core.CancelDebouncerAndSaveWorkspace();
        _core.UnsubscribeContentKeyDown();
        _core.UnsubscribeWindowActivated();
        _core.UnsubscribeWindowClosed();
        _core.UnsubscribeWorkspaceProfileChanged();
        _core.DetachNavigationService();
        _core.UnsubscribeStartupOverlay();
        _core.DisposeSessionLifecycle();
        _core.DetachTransportShortcutsAndClear();
        _core.UnsubscribeStatusBarCoordinator();
        _core.DisposeJumpListServiceBestEffort();
        _core.DisposeTaskbarProgressServiceBestEffort();
        _core.CleanupNotificationCenterViewModel();
        _core.CleanupGlobalTransportEvents();
        _core.UnsubscribeShellChromeEvents();
        _core.SetDisposed();
    }

    /// <summary>
    /// Audit L-2: temp synthesis/recording WAV files under %TEMP%.
    /// </summary>
    internal static void CleanupTemporaryAudioFiles()
    {
        try
        {
            var tempDir = Path.GetTempPath();
            var patterns = new[] { "voicestudio_*.wav", "voicestudio_recording_*.wav" };

            var cleaned = 0;
            foreach (var pattern in patterns)
            {
                foreach (var file in Directory.GetFiles(tempDir, pattern))
                {
                    try
                    {
                        File.Delete(file);
                        cleaned++;
                    }
                    catch (IOException)
                    {
                        Debug.WriteLine("[MainWindow] Temp file in use, skipped: " + file);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Debug.WriteLine("[MainWindow] Temp file access denied, skipped: " + file);
                    }
                }
            }

            if (cleaned > 0)
            {
                Debug.WriteLine($"[MainWindow] Cleaned up {cleaned} temporary audio file(s)");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainWindow] Temp cleanup failed (non-critical): {ex.Message}");
        }
    }
}
