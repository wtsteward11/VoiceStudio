// GAP-008 Slice 31 — File menu / toolbar / shortcut import-audio path (bounded).

using System;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// <see cref="MainWindow"/> import path: gate on <see cref="IStartupStateService"/>, then
/// <see cref="IImportWorkflowService.ImportAudioFileAsync"/> with the window handle.
/// </summary>
public sealed class MainWindowImportWorkflowShellBridge
{
    public void ImportAudioFile(
        Func<IStartupStateService> getStartupState,
        Func<IImportWorkflowService?> getImportWorkflowService,
        Action<string, string> showInfoToast,
        Func<IntPtr> getWindowHandle)
    {
        ArgumentNullException.ThrowIfNull(getStartupState);
        ArgumentNullException.ThrowIfNull(getImportWorkflowService);
        ArgumentNullException.ThrowIfNull(showInfoToast);
        ArgumentNullException.ThrowIfNull(getWindowHandle);

        var startupState = getStartupState();
        if (!startupState.IsReady)
        {
            showInfoToast("Starting VoiceStudio services…", "Please wait");
            return;
        }

        var service = getImportWorkflowService();
        if (service is null)
        {
            return;
        }

        var hwnd = getWindowHandle();
        _ = service.ImportAudioFileAsync(hwnd);
    }
}
