using System;
using System.Diagnostics;

namespace VoiceStudio.App.Services;

/// <summary>
/// GAP-008 Slice 12: Loaded-bootstrap shell wiring for Windows jump list schedule and taskbar progress HWND only.
/// Not pending jump-list/file activation dispatch (separate bounded slices); not notification center.
/// </summary>
public sealed class MainWindowJumpListTaskbarProgressShellBridge
{
    private readonly Func<IntPtr> _getWindowHandle;

    public MainWindowJumpListTaskbarProgressShellBridge(Func<IntPtr> getWindowHandle)
    {
        _getWindowHandle = getWindowHandle ?? throw new ArgumentNullException(nameof(getWindowHandle));
    }

    public void WireJumpList()
    {
        try
        {
            AppServices.TryGetJumpListService()?.ScheduleInitialRebuildAfterDelay(200);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainWindow] Jump list shell wire failed: {ex.Message}");
        }
    }

    public void WireTaskbarProgress()
    {
        try
        {
            var hwnd = _getWindowHandle();
            AppServices.TryGetTaskbarProgressService()?.SetWindowHandle(hwnd);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainWindow] Taskbar progress shell wire failed: {ex.Message}");
        }
    }
}
