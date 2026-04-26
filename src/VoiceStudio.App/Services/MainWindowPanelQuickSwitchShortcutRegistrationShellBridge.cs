using System;
using System.Threading.Tasks;
using VoiceStudio.Core.Panels;
using Windows.System;

namespace VoiceStudio.App.Services;

/// <summary>
/// GAP-008 Slice 37: registers Ctrl+1–9 <c>nav.panel.{n}</c> shortcuts (IDEA 1 quick-switch table).
/// Visual indicator remains the Slice 24 panel quick-switch indicator bridge (not this type).
/// </summary>
public sealed class MainWindowPanelQuickSwitchShortcutRegistrationShellBridge
{
    public void RegisterAll(
        KeyboardShortcutService keyboardShortcutService,
        Func<string, string> getPanelTitle,
        Func<string, PanelRegion?, Task<bool>> openPanelByIdAsync)
    {
        ArgumentNullException.ThrowIfNull(keyboardShortcutService);
        ArgumentNullException.ThrowIfNull(getPanelTitle);
        ArgumentNullException.ThrowIfNull(openPanelByIdAsync);

        RegisterInternal(keyboardShortcutService, getPanelTitle, openPanelByIdAsync, 1, PanelRegion.Left, "Profiles");
        RegisterInternal(keyboardShortcutService, getPanelTitle, openPanelByIdAsync, 2, PanelRegion.Left, "Library");
        RegisterInternal(keyboardShortcutService, getPanelTitle, openPanelByIdAsync, 3, PanelRegion.Left, "Training");
        RegisterInternal(keyboardShortcutService, getPanelTitle, openPanelByIdAsync, 4, PanelRegion.Center, "Timeline");
        RegisterInternal(keyboardShortcutService, getPanelTitle, openPanelByIdAsync, 5, PanelRegion.Center, "VoiceSynthesis");
        RegisterInternal(keyboardShortcutService, getPanelTitle, openPanelByIdAsync, 6, PanelRegion.Center, "TextSpeechEditor");
        RegisterInternal(keyboardShortcutService, getPanelTitle, openPanelByIdAsync, 7, PanelRegion.Right, "EffectsMixer");
        RegisterInternal(keyboardShortcutService, getPanelTitle, openPanelByIdAsync, 8, PanelRegion.Right, "Analyzer");
        RegisterInternal(keyboardShortcutService, getPanelTitle, openPanelByIdAsync, 9, PanelRegion.Right, "QualityControl");
    }

    private static void RegisterInternal(
        KeyboardShortcutService keyboardShortcutService,
        Func<string, string> getPanelTitle,
        Func<string, PanelRegion?, Task<bool>> openPanelByIdAsync,
        int number,
        PanelRegion region,
        string panelId)
    {
        VirtualKey key = number switch
        {
            1 => VirtualKey.Number1,
            2 => VirtualKey.Number2,
            3 => VirtualKey.Number3,
            4 => VirtualKey.Number4,
            5 => VirtualKey.Number5,
            6 => VirtualKey.Number6,
            7 => VirtualKey.Number7,
            8 => VirtualKey.Number8,
            9 => VirtualKey.Number9,
            _ => VirtualKey.Number1
        };

        var title = getPanelTitle(panelId);
        keyboardShortcutService.RegisterShortcut(
            $"nav.panel.{number}",
            key,
            VirtualKeyModifiers.Control,
            () => _ = openPanelByIdAsync(panelId, region),
            $"Switch to {title}");
    }
}
