// VoiceStudio - GAP-008 Slice 27: panel region focus + cycling (GAP-E02) shell bridge.
// Extracted from MainWindow.xaml.cs per VOICESTUDIO_BOUNDED_GAP008_SLICE27_MAINWINDOW_PANEL_REGION_FOCUS_SHELL.md

using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.Controls;
using VoiceStudio.Core.Panels;

namespace VoiceStudio.App.Services;

/// <summary>
/// Owns keyboard-driven panel region focus, Ctrl+Tab / Ctrl+Shift+Tab cycling order, and shows the quick-switch visual
/// indicator via a host-supplied callback (MainWindow injects the Slice 24 indicator path; this file does not name that bridge type).
/// </summary>
public sealed class MainWindowPanelRegionFocusShellBridge
{
    private static readonly PanelRegion[] PanelCycleOrder =
    {
        PanelRegion.Left,
        PanelRegion.Center,
        PanelRegion.Right,
        PanelRegion.Bottom
    };

    private int _currentPanelIndex;

    private readonly Func<PanelRegion, PanelHost?> _resolveHost;
    private readonly Func<bool> _isGateCSmokeMode;
    private readonly Action<string, PanelRegion, PanelHost> _showQuickSwitchIndicator;

    public MainWindowPanelRegionFocusShellBridge(
        Func<PanelRegion, PanelHost?> resolveHost,
        Func<bool> isGateCSmokeMode,
        Action<string, PanelRegion, PanelHost> showQuickSwitchIndicator)
    {
        _resolveHost = resolveHost;
        _isGateCSmokeMode = isGateCSmokeMode;
        _showQuickSwitchIndicator = showQuickSwitchIndicator;
    }

    public void CyclePanelNext()
    {
        _currentPanelIndex = (_currentPanelIndex + 1) % PanelCycleOrder.Length;
        FocusPanelRegion(PanelCycleOrder[_currentPanelIndex]);
    }

    public void CyclePanelPrevious()
    {
        _currentPanelIndex = (_currentPanelIndex - 1 + PanelCycleOrder.Length) % PanelCycleOrder.Length;
        FocusPanelRegion(PanelCycleOrder[_currentPanelIndex]);
    }

    public void FocusPanelRegion(PanelRegion region)
    {
        var targetHost = _resolveHost(region);
        if (targetHost is null)
        {
            return;
        }

        _currentPanelIndex = Array.IndexOf(PanelCycleOrder, region);

        if (targetHost.HostedPanel is FrameworkElement content)
        {
            _ = content.Focus(FocusState.Keyboard);
        }
        else
        {
            _ = targetHost.Focus(FocusState.Keyboard);
        }

        var panelName = GetPanelDisplayName(region);

        if (!_isGateCSmokeMode())
        {
            _showQuickSwitchIndicator(panelName, region, targetHost);
        }
    }

    private static string GetPanelDisplayName(PanelRegion region) =>
        region switch
        {
            PanelRegion.Left => "Left Panel",
            PanelRegion.Center => "Center Panel",
            PanelRegion.Right => "Right Panel",
            PanelRegion.Bottom => "Bottom Panel",
            _ => "Panel"
        };
}
