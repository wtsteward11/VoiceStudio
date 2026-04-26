// VoiceStudio — GAP-008 Slice 26: startup backend overlay shell.
// Extracted from MainWindow.xaml.cs per VOICESTUDIO_BOUNDED_GAP008_SLICE26_MAINWINDOW_STARTUP_OVERLAY_SHELL.md

using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace VoiceStudio.App.Services;

/// <summary>
/// Cold-start backend overlay: visibility, messages, progress, retry; cold-start shell-interactive timing callback (owner provides idempotent action).
/// </summary>
public sealed class MainWindowStartupOverlayShellBridge
{
    private readonly Func<Border?> _getOverlay;
    private readonly Func<TextBlock?> _getMessage;
    private readonly Func<ProgressRing?> _getProgress;
    private readonly Func<Button?> _getRetry;
    private readonly Action _onShellInteractiveTiming;
    private readonly DispatcherQueue _dispatcher;
    private readonly Func<StartupRetryCoordinator?> _getRetryCoordinator;

    public MainWindowStartupOverlayShellBridge(
        Func<Border?> getOverlay,
        Func<TextBlock?> getMessage,
        Func<ProgressRing?> getProgress,
        Func<Button?> getRetry,
        DispatcherQueue dispatcher,
        Action onShellInteractiveTiming,
        Func<StartupRetryCoordinator?> getRetryCoordinator)
    {
        _getOverlay = getOverlay ?? throw new ArgumentNullException(nameof(getOverlay));
        _getMessage = getMessage ?? throw new ArgumentNullException(nameof(getMessage));
        _getProgress = getProgress ?? throw new ArgumentNullException(nameof(getProgress));
        _getRetry = getRetry ?? throw new ArgumentNullException(nameof(getRetry));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _onShellInteractiveTiming = onShellInteractiveTiming ?? throw new ArgumentNullException(nameof(onShellInteractiveTiming));
        _getRetryCoordinator = getRetryCoordinator ?? throw new ArgumentNullException(nameof(getRetryCoordinator));
    }

    /// <summary>Enqueue overlay update from <see cref="IStartupStateService.StateChanged"/>.</summary>
    public void OnStartupStateChanged(StartupStateChangedEventArgs e)
    {
        _ = _dispatcher.TryEnqueue(() => ApplyStartupOverlay(e.NewState, e.FailureMessage));
    }

    /// <summary>Apply visibility and copy for the startup overlay (initial + state transitions).</summary>
    public void ApplyStartupOverlay(StartupState state, string? failureMessage)
    {
        var overlay = _getOverlay();
        var message = _getMessage();
        var progress = _getProgress();
        var retryBtn = _getRetry();
        if (overlay == null)
        {
            return;
        }

        var showOverlay = state == StartupState.Starting || state == StartupState.BackendStarting || state == StartupState.BackendFailed;
        if (!showOverlay)
        {
            _onShellInteractiveTiming();
        }

        overlay.Visibility = showOverlay ? Visibility.Visible : Visibility.Collapsed;

        if (showOverlay)
        {
            if (state == StartupState.BackendFailed)
            {
                if (message != null)
                {
                    message.Text = string.IsNullOrEmpty(failureMessage) ? "Backend failed to start." : failureMessage;
                }

                if (progress != null)
                {
                    progress.IsActive = false;
                }

                if (retryBtn != null)
                {
                    retryBtn.Visibility = Visibility.Visible;
                }
            }
            else
            {
                if (message != null)
                {
                    message.Text = "Starting VoiceStudio services…";
                }

                if (progress != null)
                {
                    progress.IsActive = true;
                }

                if (retryBtn != null)
                {
                    retryBtn.Visibility = Visibility.Collapsed;
                }
            }
        }
    }

    /// <summary>Backend retry from the startup overlay (progress UI on dispatcher).</summary>
    public async Task OnRetryButtonClickAsync()
    {
        var coordinator = _getRetryCoordinator();
        if (coordinator == null)
        {
            return;
        }

        var messageEl = _getMessage();
        var progressEl = _getProgress();
        var retryBtn = _getRetry();

        var progress = new Progress<StartupRetryProgress>(p =>
        {
            _ = _dispatcher.TryEnqueue(() =>
            {
                if (messageEl != null)
                {
                    messageEl.Text = p.Message;
                }

                if (progressEl != null)
                {
                    progressEl.IsActive = true;
                }

                if (retryBtn != null)
                {
                    retryBtn.Visibility = Visibility.Collapsed;
                }
            });
        });

        if (messageEl != null)
        {
            messageEl.Text = "Retrying…";
        }

        if (progressEl != null)
        {
            progressEl.IsActive = true;
        }

        if (retryBtn != null)
        {
            retryBtn.Visibility = Visibility.Collapsed;
        }

        await coordinator.RetryAsync(progress).ConfigureAwait(true);
    }
}
