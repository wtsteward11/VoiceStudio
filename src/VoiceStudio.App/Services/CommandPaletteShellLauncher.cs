using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// Default launcher: constructs <see cref="CommandPaletteService"/> and shows the palette window.
/// </summary>
public sealed class CommandPaletteShellLauncher : ICommandPaletteShellLauncher
{
    public void Show(IPanelRegistry panelRegistry, ThemeManager themeManager)
    {
        var service = new CommandPaletteService(panelRegistry, themeManager);
        service.Show();
    }
}
