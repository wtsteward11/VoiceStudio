// VoiceStudio - GAP-008 Slice 24: panel quick-switch visual indicator (IDEA 1) shell bridge.
// Extracted from MainWindow.xaml.cs per VOICESTUDIO_BOUNDED_GAP008_SLICE24_MAINWINDOW_PANEL_QUICK_SWITCH_INDICATOR_SHELL.md

using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Animation;
using VoiceStudio.App.Controls;
using VoiceStudio.Core.Panels;

namespace VoiceStudio.App.Services;

/// <summary>
/// Owns the panel quick-switch popup, <see cref="PanelQuickSwitchIndicator"/>, and hide <see cref="DispatcherTimer"/> for IDEA 1 visual feedback.
/// </summary>
public sealed class MainWindowPanelQuickSwitchShellBridge
{
    private Popup? _panelQuickSwitchPopup;
    private PanelQuickSwitchIndicator? _panelQuickSwitchIndicator;
    private DispatcherTimer? _quickSwitchHideTimer;

    /// <summary>
    /// Shows a short-lived label over the target <see cref="PanelHost"/> when the user switches or focuses a panel region.
    /// </summary>
    public void ShowPanelQuickSwitchIndicator(string panelName, PanelRegion region, PanelHost targetHost)
    {
        if (_panelQuickSwitchPopup == null)
        {
            _panelQuickSwitchIndicator = new PanelQuickSwitchIndicator();
            _panelQuickSwitchPopup = new Popup
            {
                Child = _panelQuickSwitchIndicator,
                IsLightDismissEnabled = false
            };
        }

        _panelQuickSwitchIndicator?.SetPanelInfo(panelName, region);

        var rootElement = targetHost.XamlRoot?.Content as FrameworkElement;
        if (rootElement != null)
        {
            var transform = targetHost.TransformToVisual(rootElement);
            var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

            _panelQuickSwitchPopup.HorizontalOffset = point.X + (targetHost.ActualWidth / 2) - ((_panelQuickSwitchIndicator?.ActualWidth ?? 0) / 2);
            _panelQuickSwitchPopup.VerticalOffset = point.Y + (targetHost.ActualHeight / 2) - ((_panelQuickSwitchIndicator?.ActualHeight ?? 0) / 2);
        }

        _panelQuickSwitchPopup.XamlRoot = targetHost.XamlRoot;
        _panelQuickSwitchPopup.IsOpen = true;

        if (_panelQuickSwitchIndicator != null)
        {
            var fadeIn = new FadeInThemeAnimation
            {
                Duration = TimeSpan.FromMilliseconds(200)
            };
            Storyboard.SetTarget(fadeIn, _panelQuickSwitchIndicator);
            var storyboard = new Storyboard();
            storyboard.Children.Add(fadeIn);
            storyboard.Begin();
        }

        _quickSwitchHideTimer?.Stop();

        _quickSwitchHideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1500)
        };
        _quickSwitchHideTimer.Tick += (_, _) =>
        {
            _quickSwitchHideTimer?.Stop();
            HidePanelQuickSwitchIndicator();
        };
        _quickSwitchHideTimer.Start();
    }

    /// <summary>
    /// Stops the auto-hide timer (idempotent). Invoked from <see cref="MainWindowLifetimeCleanupShellBridge.RunCleanupCore"/>.
    /// </summary>
    public void DisposeQuickSwitchHideTimer()
    {
        if (_quickSwitchHideTimer != null)
        {
            _quickSwitchHideTimer.Stop();
            _quickSwitchHideTimer = null;
        }
    }

    private void HidePanelQuickSwitchIndicator()
    {
        if (_panelQuickSwitchPopup?.IsOpen != true || _panelQuickSwitchIndicator == null)
        {
            return;
        }

        var fadeOut = new FadeOutThemeAnimation
        {
            Duration = TimeSpan.FromMilliseconds(200)
        };
        Storyboard.SetTarget(fadeOut, _panelQuickSwitchIndicator);
        var storyboard = new Storyboard();
        storyboard.Children.Add(fadeOut);
        storyboard.Begin();

        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_panelQuickSwitchPopup != null)
            {
                _panelQuickSwitchPopup.IsOpen = false;
            }
        };
        timer.Start();
    }
}
