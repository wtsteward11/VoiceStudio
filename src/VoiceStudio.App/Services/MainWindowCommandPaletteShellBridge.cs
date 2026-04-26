using System;
using System.Collections.Generic;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// GAP-008 Slice 8: MainWindow shell wiring for command palette invocation only — not toolbar customization;
/// not search overlay; not tool catalog.
/// </summary>
public sealed class MainWindowCommandPaletteShellBridge
{
    private readonly Func<IPanelRegistry> _getPanelRegistry;
    private readonly Func<ThemeManager> _getThemeManager;
    private readonly ICommandPaletteShellLauncher _launcher;
    private readonly Func<IToastNotificationService?> _getToast;
    private readonly ICommandPaletteShellDiagnostics _diagnostics;

    public MainWindowCommandPaletteShellBridge(
        Func<IPanelRegistry> getPanelRegistry,
        Func<ThemeManager> getThemeManager,
        ICommandPaletteShellLauncher launcher,
        Func<IToastNotificationService?> getToast,
        ICommandPaletteShellDiagnostics? diagnostics = null)
    {
        _getPanelRegistry = getPanelRegistry ?? throw new ArgumentNullException(nameof(getPanelRegistry));
        _getThemeManager = getThemeManager ?? throw new ArgumentNullException(nameof(getThemeManager));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _getToast = getToast ?? throw new ArgumentNullException(nameof(getToast));
        _diagnostics = diagnostics ?? CommandPaletteShellErrorDiagnostics.Instance;
    }

    /// <summary>
    /// Opens the command palette from the shell (keyboard shortcut forwards here).
    /// </summary>
    public void Show()
    {
        try
        {
            _launcher.Show(_getPanelRegistry(), _getThemeManager());
        }
        catch (Exception ex)
        {
            var message = $"Command palette failed to open: {ex.Message}";
            var context = new Dictionary<string, object>
            {
                ["ExceptionType"] = ex.GetType().FullName ?? string.Empty
            };
            _diagnostics.LogCommandPaletteOpenFailure(
                message,
                "MainWindowCommandPaletteShellBridge.Show",
                ex,
                context);
            var toastService = _getToast();
            toastService?.ShowError(
                $"Could not open the command palette: {ex.Message}",
                "Command Palette");
        }
    }
}
