// VoiceStudio — GAP-008 Slice 25: panel region dock / swap (IDEA 14) shell bridge.
// Extracted from MainWindow.xaml.cs per VOICESTUDIO_BOUNDED_GAP008_SLICE25_MAINWINDOW_PANEL_DOCKING_SHELL.md

using System;
using VoiceStudio.App.Controls;
using VoiceStudio.Core.Panels;

namespace VoiceStudio.App.Services;

/// <summary>
/// Cross-region <see cref="PanelHost"/> dock/swap: fade animation, migrate panel state, reopen panels, layout save, toasts.
/// </summary>
public sealed class MainWindowPanelDockShellBridge
{
    private readonly Func<PanelRegion, PanelHost?> _getPanelHostByRegion;
    private readonly Func<string, PanelRegion?, Task<bool>> _openPanelByIdAsync;
    private readonly PanelStateService? _panelState;
    private readonly Action? _invokeLayoutSave;
    private readonly Func<IToastNotificationService?> _getToast;

    public MainWindowPanelDockShellBridge(
        Func<PanelRegion, PanelHost?> getPanelHostByRegion,
        Func<string, PanelRegion?, Task<bool>> openPanelByIdAsync,
        PanelStateService? panelState,
        Action? invokeLayoutSave,
        Func<IToastNotificationService?> getToast)
    {
        _getPanelHostByRegion = getPanelHostByRegion ?? throw new ArgumentNullException(nameof(getPanelHostByRegion));
        _openPanelByIdAsync = openPanelByIdAsync ?? throw new ArgumentNullException(nameof(openPanelByIdAsync));
        _panelState = panelState;
        _invokeLayoutSave = invokeLayoutSave;
        _getToast = getToast ?? throw new ArgumentNullException(nameof(getToast));
    }

    /// <summary>Handles <see cref="PanelHost.OnPanelDockRequested"/> from all region hosts.</summary>
    public void OnPanelDockRequested(object? sender, PanelDockEventArgs e)
    {
        if (e.SourcePanelHost == null)
        {
            return;
        }

        var targetHost = e.TargetRegion switch
        {
            PanelRegion.Left => _getPanelHostByRegion(PanelRegion.Left),
            PanelRegion.Center => _getPanelHostByRegion(PanelRegion.Center),
            PanelRegion.Right => _getPanelHostByRegion(PanelRegion.Right),
            PanelRegion.Bottom => _getPanelHostByRegion(PanelRegion.Bottom),
            _ => null
        };

        if (targetHost == null || targetHost == e.SourcePanelHost)
        {
            return;
        }

        var sourceContent = e.SourcePanelHost.HostedPanel;
        var targetContent = targetHost.HostedPanel;
        AnimatePanelDock(e.SourcePanelHost, targetHost, sourceContent, targetContent);
    }

    private void AnimatePanelDock(PanelHost sourceHost, PanelHost targetHost, UIElement? sourceContent, UIElement? targetContent)
    {
        var sourceRegion = sourceHost.PanelRegion;
        var targetRegion = targetHost.PanelRegion;
        var sourcePanelId = PanelHost.TryGetPanelIdFromContent(sourceContent, out var s) ? s : null;
        var targetPanelId = PanelHost.TryGetPanelIdFromContent(targetContent, out var t) ? t : null;

        var sourceFadeOut = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(200)
        };
        Storyboard.SetTarget(sourceFadeOut, sourceHost);
        Storyboard.SetTargetProperty(sourceFadeOut, "Opacity");

        var targetFadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(200),
            BeginTime = TimeSpan.FromMilliseconds(200)
        };
        Storyboard.SetTarget(targetFadeIn, targetHost);
        Storyboard.SetTargetProperty(targetFadeIn, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(sourceFadeOut);
        storyboard.Children.Add(targetFadeIn);

        storyboard.Completed += (_, _) =>
        {
            sourceHost.Opacity = 1;
            targetHost.Opacity = 1;
            _ = CompletePanelDockAsync(sourceHost, targetHost, sourceRegion, targetRegion, sourcePanelId, targetPanelId);
        };

        storyboard.Begin();
    }

    private async Task CompletePanelDockAsync(
        PanelHost sourceHost,
        PanelHost targetHost,
        PanelRegion sourceRegion,
        PanelRegion targetRegion,
        string? sourcePanelId,
        string? targetPanelId)
    {
        if (!string.IsNullOrEmpty(sourcePanelId))
        {
            await sourceHost.UnloadPanelAsync(sourcePanelId).ConfigureAwait(true);
        }

        if (!string.IsNullOrEmpty(targetPanelId))
        {
            await targetHost.UnloadPanelAsync(targetPanelId).ConfigureAwait(true);
        }

        if (!string.IsNullOrEmpty(sourcePanelId))
        {
            _panelState?.MigratePanelState(sourcePanelId, sourceRegion, targetRegion);
        }

        if (!string.IsNullOrEmpty(targetPanelId))
        {
            _panelState?.MigratePanelState(targetPanelId, targetRegion, sourceRegion);
        }

        if (!string.IsNullOrEmpty(targetPanelId))
        {
            await _openPanelByIdAsync(targetPanelId, sourceRegion).ConfigureAwait(true);
        }

        if (!string.IsNullOrEmpty(sourcePanelId))
        {
            await _openPanelByIdAsync(sourcePanelId, targetRegion).ConfigureAwait(true);
        }

        _invokeLayoutSave?.Invoke();

        var toastService = _getToast();
        if (!string.IsNullOrEmpty(sourcePanelId) && !string.IsNullOrEmpty(targetPanelId))
        {
            toastService?.ShowSuccess("Panel Swapped",
                $"Swapped {targetPanelId} ({sourceRegion}) \u2194 {sourcePanelId} ({targetRegion})");
        }
        else
        {
            var movedName = sourcePanelId ?? targetPanelId ?? "Panel";
            var destRegion = !string.IsNullOrEmpty(sourcePanelId) ? targetRegion : sourceRegion;
            toastService?.ShowSuccess("Panel Moved", $"Moved {movedName} -> {destRegion}");
        }
    }
}
