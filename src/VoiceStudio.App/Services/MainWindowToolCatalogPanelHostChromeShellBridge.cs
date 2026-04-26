// VoiceStudio — GAP-008 Slice 35: tool catalog completion path — PanelHost title/icon only.

using System;
using VoiceStudio.App.Controls;
using VoiceStudio.Core.Panels;

namespace VoiceStudio.App.Services;

/// <summary>
/// Applies tool catalog display metadata to the <see cref="PanelHost"/> for a <see cref="PanelRegion"/>
/// after <see cref="MainWindowToolCatalogShellBridge"/> opens the panel. Does not own the catalog dialog (Slice 10).
/// </summary>
public sealed class MainWindowToolCatalogPanelHostChromeShellBridge
{
    public void Apply(PanelRegion region, string title, string? icon, Func<string, object?> findNameOnContent)
    {
        ArgumentNullException.ThrowIfNull(findNameOnContent);
        var host = region switch
        {
            PanelRegion.Left => findNameOnContent("LeftPanelHost") as PanelHost,
            PanelRegion.Center => findNameOnContent("CenterPanelHost") as PanelHost,
            PanelRegion.Right => findNameOnContent("RightPanelHost") as PanelHost,
            PanelRegion.Bottom => findNameOnContent("BottomPanelHost") as PanelHost,
            _ => null
        };
        if (host == null)
        {
            return;
        }

        host.PanelTitle = title;
        if (!string.IsNullOrEmpty(icon))
        {
            host.PanelIcon = icon;
        }
    }
}
