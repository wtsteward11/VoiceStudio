using System;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// Adapter that wraps WinUI DispatcherQueue to implement IDispatcher.
  /// </summary>
  internal sealed class DispatcherQueueAdapter : IDispatcher
  {
    private readonly DispatcherQueue _queue;

    public DispatcherQueueAdapter(DispatcherQueue queue)
    {
      _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    public bool TryEnqueue(Action action)
    {
      return _queue.TryEnqueue(() => action());
    }

    public IDispatcherTimer CreateTimer()
    {
      var timer = _queue.CreateTimer();
      return new DispatcherQueueTimerAdapter(timer);
    }
  }

  /// <summary>
  /// Adapter that wraps WinUI DispatcherQueueTimer to implement IDispatcherTimer.
  /// </summary>
  internal sealed class DispatcherQueueTimerAdapter : IDispatcherTimer
  {
    private readonly DispatcherQueueTimer _timer;

    public DispatcherQueueTimerAdapter(DispatcherQueueTimer timer)
    {
      _timer = timer ?? throw new ArgumentNullException(nameof(timer));
      _timer.Tick += (_, args) => Tick?.Invoke(this, args);
    }

    public TimeSpan Interval
    {
      get => _timer.Interval;
      set => _timer.Interval = value;
    }

    public bool IsRepeating
    {
      get => _timer.IsRepeating;
      set => _timer.IsRepeating = value;
    }

    public event EventHandler<object>? Tick;

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();
  }

  /// <summary>
  /// Concrete implementation of IViewModelContext for ViewModel ambient context.
  /// Used when DI is not yet initialized (e.g. BaseViewModel fallback).
  /// </summary>
  public sealed class ViewModelContext : IViewModelContext
  {
    public ILogger Logger { get; }
    public IDispatcher Dispatcher { get; }

    public ViewModelContext(ILogger logger, IDispatcher dispatcher)
    {
      Logger = logger ?? throw new ArgumentNullException(nameof(logger));
      Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    /// <summary>
    /// Constructor that accepts WinUI DispatcherQueue for DI registration.
    /// </summary>
    public ViewModelContext(ILogger logger, DispatcherQueue dispatcherQueue)
    {
      Logger = logger ?? throw new ArgumentNullException(nameof(logger));
      Dispatcher = new DispatcherQueueAdapter(dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue)));
    }
  }
}