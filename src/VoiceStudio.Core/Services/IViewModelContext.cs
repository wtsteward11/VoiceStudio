using System;
using Microsoft.Extensions.Logging;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Abstraction for dispatching work to the UI thread.
  /// Enables ViewModels to be tested without WinUI DispatcherQueue.
  /// </summary>
  public interface IDispatcher
  {
    /// <summary>
    /// Enqueues an action to run on the UI thread.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <returns>True if the action was enqueued.</returns>
    bool TryEnqueue(Action action);

    /// <summary>
    /// Creates a timer that fires on the UI thread.
    /// </summary>
    /// <returns>A timer instance.</returns>
    IDispatcherTimer CreateTimer();
  }

  /// <summary>
  /// Abstraction for a UI-thread timer.
  /// Enables ViewModels to be tested without WinUI DispatcherQueueTimer.
  /// </summary>
  public interface IDispatcherTimer
  {
    TimeSpan Interval { get; set; }
    bool IsRepeating { get; set; }
    event EventHandler<object>? Tick;
    void Start();
    void Stop();
  }

  /// <summary>
  /// Ambient context for ViewModels providing logger and UI dispatcher.
  /// Part of TD-004 ViewModel DI migration to eliminate parameterless BaseViewModel().
  /// </summary>
  /// <remarks>
  /// Registered as singleton in DI container.
  /// Provides access to UI thread dispatcher and scoped logging.
  /// </remarks>
  public interface IViewModelContext
  {
    /// <summary>
    /// Logger for ViewModel diagnostic output.
    /// Used by BaseViewModel for error logging and trace output.
    /// </summary>
    ILogger Logger { get; }

    /// <summary>
    /// UI thread dispatcher for marshalling async results to UI.
    /// Required for any ViewModel that updates observable properties from background tasks.
    /// </summary>
    IDispatcher Dispatcher { get; }
  }
}