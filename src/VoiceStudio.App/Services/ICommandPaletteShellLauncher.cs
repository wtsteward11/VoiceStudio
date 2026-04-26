using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// Production/test seam for opening the command palette window from shell glue (<see cref="MainWindowCommandPaletteShellBridge"/>).
/// </summary>
public interface ICommandPaletteShellLauncher
{
    void Show(IPanelRegistry panelRegistry, ThemeManager themeManager);
}
