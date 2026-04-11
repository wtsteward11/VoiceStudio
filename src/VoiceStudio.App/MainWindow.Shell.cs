using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using VoiceStudio.App.Helpers;
using VoiceStudio.App.Services;
using Windows.UI;

namespace VoiceStudio.App;

public sealed partial class MainWindow
{
    private EventHandler<ThemeChangedEventArgs>? _shellThemeChangedHandler;

    /// <summary>
    /// Applies Mica or Desktop Acrylic when supported; otherwise leaves <see cref="RootGrid"/> on VSQ gradient.
    /// Must run only from <see cref="FrameworkElement.Loaded"/> (ADR-047).
    /// </summary>
    private void ApplyMicaBackdrop()
    {
        try
        {
            var best = MaterialsHelper.GetBestAvailableMaterial();
            if (best == MaterialsHelper.MaterialType.None)
            {
                System.Diagnostics.Debug.WriteLine("[MainWindow] Backdrop: no system material (gradient fallback).");
                return;
            }

            var applied = MaterialsHelper.ApplyMaterial(this, best);
            if (applied)
            {
                RootGrid.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                System.Diagnostics.Debug.WriteLine($"[MainWindow] Backdrop applied: {best}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] Backdrop apply returned false (best={best}).");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] ApplyMicaBackdrop: {ex.Message}");
        }
    }

    /// <summary>
    /// Custom title bar: extends client into caption, drag region, and caption button colors. Loaded-only (ADR-047).
    /// </summary>
    private void InitializeCustomTitleBar()
    {
        try
        {
            ExtendsContentIntoTitleBar = true;
            if (AppTitleBar != null)
            {
                SetTitleBar(AppTitleBar);
            }

            var themeSvc = AppServices.TryGetThemeService();
            var isDark = themeSvc?.IsDarkMode ?? true;
            ApplyShellTitleBarColors(isDark);

            if (themeSvc != null)
            {
                _shellThemeChangedHandler = OnShellThemeChanged;
                themeSvc.ThemeChanged -= _shellThemeChangedHandler;
                themeSvc.ThemeChanged += _shellThemeChangedHandler;
            }

            MaterialsHelper.RefreshSystemBackdropTheme();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] InitializeCustomTitleBar: {ex.Message}");
        }
    }

    private void OnShellThemeChanged(object? sender, ThemeChangedEventArgs e)
    {
        MaterialsHelper.RefreshSystemBackdropTheme();
        var isDark = e.EffectiveTheme == ElementTheme.Dark;
        if (e.EffectiveTheme == ElementTheme.Default && sender is IUnifiedThemeService ut)
        {
            isDark = ut.IsDarkMode;
        }

        ApplyShellTitleBarColors(isDark);
    }

    private void ApplyShellTitleBarColors(bool isDark)
    {
        try
        {
            var titleBar = AppWindow.TitleBar;
            if (isDark)
            {
                titleBar.ForegroundColor = Color.FromArgb(255, 240, 240, 240);
                titleBar.BackgroundColor = Color.FromArgb(255, 32, 32, 32);
                titleBar.ButtonForegroundColor = Color.FromArgb(255, 255, 255, 255);
                titleBar.ButtonBackgroundColor = Color.FromArgb(0, 0, 0, 0);
                titleBar.ButtonHoverForegroundColor = Color.FromArgb(255, 255, 255, 255);
                titleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 60, 60, 60);
                titleBar.ButtonPressedForegroundColor = Color.FromArgb(255, 255, 255, 255);
                titleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 80, 80, 80);
                titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 160, 160, 160);
                titleBar.InactiveForegroundColor = Color.FromArgb(255, 160, 160, 160);
                titleBar.InactiveBackgroundColor = Color.FromArgb(255, 32, 32, 32);
            }
            else
            {
                titleBar.ForegroundColor = Color.FromArgb(255, 20, 20, 20);
                titleBar.BackgroundColor = Color.FromArgb(255, 243, 243, 243);
                titleBar.ButtonForegroundColor = Color.FromArgb(255, 0, 0, 0);
                titleBar.ButtonBackgroundColor = Color.FromArgb(0, 0, 0, 0);
                titleBar.ButtonHoverForegroundColor = Color.FromArgb(255, 0, 0, 0);
                titleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 220, 220, 220);
                titleBar.ButtonPressedForegroundColor = Color.FromArgb(255, 0, 0, 0);
                titleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 200, 200, 200);
                titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 100, 100, 100);
                titleBar.InactiveForegroundColor = Color.FromArgb(255, 100, 100, 100);
                titleBar.InactiveBackgroundColor = Color.FromArgb(255, 243, 243, 243);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] ApplyShellTitleBarColors: {ex.Message}");
        }
    }

    private void UnsubscribeShellChromeEvents()
    {
        if (_shellThemeChangedHandler == null)
        {
            return;
        }

        var themeSvc = AppServices.TryGetThemeService();
        if (themeSvc != null)
        {
            themeSvc.ThemeChanged -= _shellThemeChangedHandler;
        }

        _shellThemeChangedHandler = null;
    }
}
