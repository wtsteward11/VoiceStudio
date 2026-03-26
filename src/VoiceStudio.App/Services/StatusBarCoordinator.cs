// VoiceStudio - Status Bar Orchestration (Transport Coherence Wave 3 Task 5)
// Extracted from MainWindow.xaml.cs per MAINWINDOW_DECOMPOSITION_PLAN.md

using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// Coordinates status bar updates: current media, reachability, degraded banner, activity indicators.
/// MainWindow attaches and subscribes; coordinator owns event handlers and UI refresh logic.
/// </summary>
public sealed class StatusBarCoordinator
{
    private Func<string, object?>? _findName;
    private DispatcherQueue? _dispatcherQueue;
    private IContextManager? _contextManager;
    private StatusBarActivityService? _activityService;
    private GracefulDegradationService? _gracefulDegradation;
    private bool _subscribed;

    /// <summary>
    /// Attaches to the visual tree. Call from MainWindow Loaded.
    /// </summary>
    /// <param name="dispatcherQueue">UI thread dispatcher for marshalling updates.</param>
    /// <param name="findName">Delegate to resolve elements by name (e.g. FindNameOnContent).</param>
    public void Attach(DispatcherQueue dispatcherQueue, Func<string, object?> findName)
    {
        _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
        _findName = findName ?? throw new ArgumentNullException(nameof(findName));
    }

    /// <summary>
    /// Subscribes to context, activity, reachability, and degraded mode events.
    /// </summary>
    public void Subscribe(
        IContextManager ctx,
        StatusBarActivityService? activityService,
        GracefulDegradationService? gracefulDegradation)
    {
        if (_subscribed || _findName == null || _dispatcherQueue == null)
            return;

        _contextManager = ctx ?? throw new ArgumentNullException(nameof(ctx));
        _activityService = activityService;
        _gracefulDegradation = gracefulDegradation;

        _contextManager.TransportContextChanged += OnTransportContextChanged;

        if (AppServices.TryGetErrorPresentationService() is ErrorPresentationService eps)
        {
            eps.StartBackendMonitoring();
            ErrorPresentationService.BackendReachabilityChanged += OnBackendReachabilityChanged;
        }

        if (_gracefulDegradation != null)
            _gracefulDegradation.DegradedModeChanged += OnDegradedModeChanged;

        if (_activityService != null)
        {
            _activityService.ActivityStatusChanged += OnActivityStatusChanged;
            UpdateActivityIndicators(_activityService);
        }

        UpdateCurrentMedia(_contextManager);
        _subscribed = true;
    }

    /// <summary>
    /// Unsubscribes from all events. Call from MainWindow Cleanup/Unloaded.
    /// </summary>
    public void Unsubscribe()
    {
        if (!_subscribed)
            return;

        if (_contextManager != null)
        {
            _contextManager.TransportContextChanged -= OnTransportContextChanged;
            _contextManager = null;
        }

        ErrorPresentationService.BackendReachabilityChanged -= OnBackendReachabilityChanged;

        if (_gracefulDegradation != null)
        {
            _gracefulDegradation.DegradedModeChanged -= OnDegradedModeChanged;
            _gracefulDegradation = null;
        }

        if (_activityService != null)
        {
            _activityService.ActivityStatusChanged -= OnActivityStatusChanged;
            _activityService = null;
        }

        _subscribed = false;
    }

    /// <summary>
    /// Refreshes current media display from context.
    /// </summary>
    public void UpdateCurrentMedia(IContextManager ctx)
    {
        if (_findName == null)
            return;

        var currentMediaText = _findName("CurrentMediaStatusText") as Microsoft.UI.Xaml.Controls.TextBlock;
        if (currentMediaText == null)
            return;

        var title = ctx.CurrentPlayableTitle;
        var sourceDisplay = ctx.CurrentPlayableSource.ToDisplayString();
        if (!string.IsNullOrEmpty(ctx.CurrentPlayableAudioId) && ctx.CurrentPlayableSource != null && ctx.CurrentPlayableSource != TransportSource.None)
            currentMediaText.Text = $" | {title ?? "—"} ({sourceDisplay})";
        else
            currentMediaText.Text = "";
    }

    private void OnTransportContextChanged(object? sender, TransportContextChangedEventArgs e)
    {
        _dispatcherQueue?.TryEnqueue(() =>
        {
            var ctx = AppServices.GetContextManager();
            UpdateCurrentMedia(ctx);
        });
    }

    private void OnBackendReachabilityChanged(object? sender, bool reachable)
    {
        _dispatcherQueue?.TryEnqueue(() =>
        {
            UpdateNetworkIndicator(reachable ? NetworkStatus.Connected : NetworkStatus.Disconnected);

            var statusText = _findName?.Invoke("StatusText") as Microsoft.UI.Xaml.Controls.TextBlock;
            if (statusText != null)
                statusText.Text = reachable ? "Ready" : "Backend offline \u2014 reconnecting\u2026";
        });
    }

    private void OnDegradedModeChanged(object? sender, bool isDegraded)
    {
        _dispatcherQueue?.TryEnqueue(() =>
        {
            var banner = _findName?.Invoke("DegradedModeBanner") as Microsoft.UI.Xaml.Controls.InfoBar;
            if (banner == null)
                return;
            banner.IsOpen = isDegraded;
            if (isDegraded && sender is GracefulDegradationService gds)
                banner.Message = gds.DegradationReason ?? "Backend temporarily unavailable.";
        });
    }

    private void OnActivityStatusChanged(object? sender, ActivityStatusChangedEventArgs e)
    {
        _dispatcherQueue?.TryEnqueue(() => UpdateActivityIndicators(e));
    }

    private void UpdateActivityIndicators(StatusBarActivityService? service = null)
    {
        if (service == null)
            service = AppServices.TryGetStatusBarActivityService();

        if (service == null)
            return;

        var status = new ActivityStatusChangedEventArgs
        {
            ProcessingStatus = service.ProcessingStatus,
            NetworkStatus = service.NetworkStatus,
            EngineStatus = service.EngineStatus,
            ActiveJobCount = service.ActiveJobCount,
            QueuedOperationCount = service.QueuedOperationCount
        };
        UpdateActivityIndicators(status);
    }

    private void UpdateActivityIndicators(ActivityStatusChangedEventArgs status)
    {
        UpdateProcessingIndicator(status.ProcessingStatus, status.ActiveJobCount, status.QueuedOperationCount);
        UpdateNetworkIndicator(status.NetworkStatus);
        UpdateEngineIndicator(status.EngineStatus);
        UpdateStatusText(status);
    }

    private void UpdateProcessingIndicator(ProcessingStatus status, int activeJobCount, int queuedCount)
    {
        var processingIndicator = _findName?.Invoke("ProcessingIndicator") as FrameworkElement;
        if (processingIndicator == null)
            return;

        var tooltip = status switch
        {
            ProcessingStatus.Processing => $"Processing: {activeJobCount} active job(s), {queuedCount} queued",
            ProcessingStatus.Paused => "Processing: Paused",
            ProcessingStatus.Error => "Processing: Error",
            _ => "Processing: Idle"
        };
        ToolTipService.SetToolTip(processingIndicator, tooltip);

        var color = status switch
        {
            ProcessingStatus.Processing => Color.FromArgb(255, 0, 255, 127),
            ProcessingStatus.Paused => Color.FromArgb(255, 255, 255, 0),
            ProcessingStatus.Error => Color.FromArgb(255, 255, 0, 0),
            _ => Color.FromArgb(255, 128, 128, 128)
        };
        processingIndicator.SetValue(Control.BackgroundProperty, new SolidColorBrush(color));
        processingIndicator.Opacity = status == ProcessingStatus.Idle ? 0.3 : 1.0;
    }

    private void UpdateNetworkIndicator(NetworkStatus status)
    {
        var networkIndicator = _findName?.Invoke("NetworkIndicator") as FrameworkElement;
        if (networkIndicator == null)
            return;

        var tooltip = status switch
        {
            NetworkStatus.Connected => "Network: Connected",
            NetworkStatus.Disconnected => "Network: Disconnected",
            NetworkStatus.Reconnecting => "Network: Reconnecting...",
            _ => "Network: Error"
        };
        ToolTipService.SetToolTip(networkIndicator, tooltip);

        var color = status switch
        {
            NetworkStatus.Connected => Color.FromArgb(255, 0, 255, 127),
            NetworkStatus.Reconnecting => Color.FromArgb(255, 255, 255, 0),
            _ => Color.FromArgb(255, 255, 0, 0)
        };
        networkIndicator.SetValue(Control.BackgroundProperty, new SolidColorBrush(color));
        networkIndicator.Opacity = status == NetworkStatus.Connected ? 1.0 : 0.7;
    }

    private void UpdateEngineIndicator(EngineStatus status)
    {
        var engineIndicator = _findName?.Invoke("EngineIndicator") as FrameworkElement;
        if (engineIndicator == null)
            return;

        // Round 5 Task 6: No "Engine: Ready" before backend ready (fake-ready audit)
        var startupState = AppServices.GetService<IStartupStateService>();
        if (startupState != null && !startupState.IsReady)
        {
            ToolTipService.SetToolTip(engineIndicator, "Engine: Starting…");
            engineIndicator.SetValue(Control.BackgroundProperty, new SolidColorBrush(Color.FromArgb(255, 255, 255, 0)));
            engineIndicator.Opacity = 0.8;
            return;
        }

        var tooltip = status switch
        {
            EngineStatus.Ready => "Engine: Ready",
            EngineStatus.Busy => "Engine: Busy",
            EngineStatus.Starting => "Engine: Starting...",
            EngineStatus.Offline => "Engine: Offline",
            _ => "Engine: Error"
        };
        ToolTipService.SetToolTip(engineIndicator, tooltip);

        var color = status switch
        {
            EngineStatus.Ready => Color.FromArgb(255, 0, 255, 127),
            EngineStatus.Busy => Color.FromArgb(255, 0, 120, 212),
            EngineStatus.Starting => Color.FromArgb(255, 255, 255, 0),
            _ => Color.FromArgb(255, 255, 0, 0)
        };
        engineIndicator.SetValue(Control.BackgroundProperty, new SolidColorBrush(color));
        engineIndicator.Opacity = status == EngineStatus.Ready ? 1.0 : 0.8;
    }

    private void UpdateStatusText(ActivityStatusChangedEventArgs status)
    {
        var statusText = _findName?.Invoke("StatusText") as Microsoft.UI.Xaml.Controls.TextBlock;
        if (statusText == null)
            return;

        if (ErrorPresentationService.IsBackendOffline)
            return;

        // Round 5 Task 6: No "Ready" before backend ready (fake-ready audit)
        var startupState = AppServices.GetService<IStartupStateService>();
        if (startupState != null && !startupState.IsReady)
        {
            statusText.Text = "Starting…";
            return;
        }

        statusText.Text = status.ProcessingStatus switch
        {
            ProcessingStatus.Processing => $"Processing ({status.ActiveJobCount} job(s))",
            ProcessingStatus.Paused => "Paused",
            ProcessingStatus.Error => "Error",
            _ => "Ready"
        };
    }
}
