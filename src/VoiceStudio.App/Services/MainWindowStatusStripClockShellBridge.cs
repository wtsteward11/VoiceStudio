using System;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace VoiceStudio.App.Services;

/// <summary>
/// GAP-008 Slice 17: one-minute status-strip clock tick (separate from DispatcherTimer metrics on <see cref="MainWindowStatusStripMetricsShellBridge"/>).
/// </summary>
public sealed class MainWindowStatusStripClockShellBridge
{
    private readonly Func<TextBlock?> _getClockText;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly Func<bool> _getDisposed;

    private Timer? _clockTimer;

    public MainWindowStatusStripClockShellBridge(
        Func<TextBlock?> getClockText,
        DispatcherQueue dispatcherQueue,
        Func<bool> getDisposed)
    {
        _getClockText = getClockText ?? throw new ArgumentNullException(nameof(getClockText));
        _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
        _getDisposed = getDisposed ?? throw new ArgumentNullException(nameof(getDisposed));
    }

    /// <summary>
    /// Starts the 1-minute wall-clock refresh for <c>ClockText</c> (12-hour format). Idempotent: disposes any prior timer first.
    /// </summary>
    public void BeginClockTimer()
    {
        DisposeClockTimer();
        RefreshClockText();
        _clockTimer = new Timer(
            _ =>
            {
                if (!_getDisposed())
                {
                    _dispatcherQueue.TryEnqueue(RefreshClockText);
                }
            },
            null,
            TimeSpan.Zero,
            TimeSpan.FromMinutes(1));
    }

    public void RefreshClockText()
    {
        var clockText = _getClockText();
        if (clockText != null)
        {
            clockText.Text = DateTime.Now.ToString("h:mm tt");
        }
    }

    public void DisposeClockTimer()
    {
        _clockTimer?.Dispose();
        _clockTimer = null;
    }
}
