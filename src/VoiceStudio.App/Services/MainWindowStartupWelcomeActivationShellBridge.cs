using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using VoiceStudio.App.Helpers;
using VoiceStudio.App.Logging;
using VoiceStudio.App.Views;

namespace VoiceStudio.App.Services;

/// <summary>
/// GAP-008 Slice 11: first-window activation — KeyDown attach on content and one-shot welcome dialog.
/// MainWindow forwards <see cref="Window.Activated"/> here; keyboard handler remains <see cref="MainWindow"/> instance method.
/// </summary>
public sealed class MainWindowStartupWelcomeActivationShellBridge
{
    private const string ShowWelcomeKey = "ShowWelcomeDialog";
    private bool _welcomeDialogShown;
    private readonly Func<bool> _isGateCSmokeMode;
    private readonly Func<bool> _isSafeStartupMode;
    private readonly KeyEventHandler _windowKeyDown;

    public MainWindowStartupWelcomeActivationShellBridge(
        Func<bool> isGateCSmokeMode,
        Func<bool> isSafeStartupMode,
        KeyEventHandler windowKeyDown)
    {
        _isGateCSmokeMode = isGateCSmokeMode ?? throw new ArgumentNullException(nameof(isGateCSmokeMode));
        _isSafeStartupMode = isSafeStartupMode ?? throw new ArgumentNullException(nameof(isSafeStartupMode));
        _windowKeyDown = windowKeyDown ?? throw new ArgumentNullException(nameof(windowKeyDown));
    }

    /// <summary>
    /// Handles window activation: keyboard attach, smoke/safe gates, optional welcome dialog.
    /// </summary>
    public async Task HandleActivatedAsync(Window window, WindowActivatedEventArgs e)
    {
        try
        {
            if (_isGateCSmokeMode())
            {
                return;
            }

            if (window.Content is UIElement root)
            {
                root.KeyDown -= _windowKeyDown;
                root.KeyDown += _windowKeyDown;
            }
        }
        catch (Exception ex)
        {
            ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "StartupWelcomeActivationShellBridge.HandleActivatedAsync.KeyDown");
        }

        if (e.WindowActivationState != WindowActivationState.CodeActivated)
        {
            return;
        }

        if (_isSafeStartupMode())
        {
            _welcomeDialogShown = true;
            return;
        }

        if (_welcomeDialogShown)
        {
            return;
        }

        var showWelcome = UnpackagedSettingsHelper.GetValue<bool>(ShowWelcomeKey, true);

        try
        {
            if (showWelcome && window.Content?.XamlRoot is not null)
            {
                _welcomeDialogShown = true;
                var welcomeDialog = new WelcomeView();
                welcomeDialog.XamlRoot = window.Content.XamlRoot;
                try
                {
                    var showTask = welcomeDialog.ShowAsync();
                    var timeoutTask = Task.Delay(5000);
                    var completed = await Task.WhenAny(showTask.AsTask(), timeoutTask).ConfigureAwait(true);
                    if (completed == timeoutTask)
                    {
                        Debug.WriteLine("[Startup] Welcome dialog ShowAsync timed out after 5s; continuing");
                        welcomeDialog.Hide();
                    }
                    else
                    {
                        await showTask.AsTask().ConfigureAwait(true);
                        UnpackagedSettingsHelper.SetValue(ShowWelcomeKey, welcomeDialog.ShowOnStartup);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Startup] Welcome dialog failed: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            ErrorLogger.LogWarning($"Activated handler failed: {ex.Message}", "StartupWelcomeActivationShellBridge.HandleActivatedAsync.Welcome");
        }
    }
}
