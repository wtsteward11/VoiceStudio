using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// GAP-I01: Event publisher with throttling and coalescing support.
  /// Reduces event storm impact by coalescing rapid-fire events.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Use this publisher when events may be fired rapidly (e.g., during
  /// slider drags, text input, or progress updates) and only the latest
  /// state matters to subscribers.
  /// </para>
  /// <para>
  /// Throttling modes:
  /// - Leading: Fires immediately, then throttles subsequent events
  /// - Trailing: Waits for quiet period, then fires last event
  /// - LeadingAndTrailing: Fires immediately and after quiet period
  /// </para>
  /// </remarks>
  public class ThrottledEventPublisher : IDisposable
  {
    private readonly IEventAggregator _eventAggregator;
    private readonly ConcurrentDictionary<Type, ThrottleState> _throttleStates = new();
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// Default throttle window in milliseconds.
    /// </summary>
    public int DefaultThrottleMs { get; set; } = 100;

    /// <summary>
    /// Default throttle mode.
    /// </summary>
    public ThrottleMode DefaultMode { get; set; } = ThrottleMode.Trailing;

    /// <summary>
    /// Creates a new throttled event publisher.
    /// </summary>
    /// <param name="eventAggregator">The underlying event aggregator.</param>
    public ThrottledEventPublisher(IEventAggregator eventAggregator)
    {
      _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
    }

    /// <summary>
    /// Publishes an event with throttling applied.
    /// </summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="eventMessage">The event to publish.</param>
    /// <param name="throttleMs">Throttle window in milliseconds (null = use default).</param>
    /// <param name="mode">Throttle mode (null = use default).</param>
    public void Publish<TEvent>(
      TEvent eventMessage,
      int? throttleMs = null,
      ThrottleMode? mode = null) where TEvent : class
    {
      if (eventMessage == null)
        throw new ArgumentNullException(nameof(eventMessage));

      var actualThrottleMs = throttleMs ?? DefaultThrottleMs;
      var actualMode = mode ?? DefaultMode;

      // Get or create throttle state for this event type
      var state = _throttleStates.GetOrAdd(typeof(TEvent), _ => new ThrottleState());

      lock (state.Lock)
      {
        state.LatestEvent = eventMessage;
        state.ThrottleMs = actualThrottleMs;
        state.Mode = actualMode;

        var now = DateTime.UtcNow;
        var timeSinceLastPublish = (now - state.LastPublishTime).TotalMilliseconds;

        // Leading edge: publish immediately if enough time has passed
        if ((actualMode == ThrottleMode.Leading || actualMode == ThrottleMode.LeadingAndTrailing) 
            && timeSinceLastPublish >= actualThrottleMs)
        {
          PublishImmediate(eventMessage);
          state.LastPublishTime = now;
          state.HasPendingTrailing = false;
        }
        else
        {
          // Mark that we need a trailing publish
          state.HasPendingTrailing = true;
        }

        // Schedule trailing edge publish if needed
        if ((actualMode == ThrottleMode.Trailing || actualMode == ThrottleMode.LeadingAndTrailing)
            && state.HasPendingTrailing)
        {
          ScheduleTrailingPublish<TEvent>(state, actualThrottleMs);
        }
      }
    }

    /// <summary>
    /// Publishes an event with throttling applied (async version).
    /// </summary>
    public async Task PublishAsync<TEvent>(
      TEvent eventMessage,
      int? throttleMs = null,
      ThrottleMode? mode = null) where TEvent : class
    {
      // For async, we use the same throttling logic but await the trailing publish
      Publish(eventMessage, throttleMs, mode);
      
      // If trailing mode and there's a pending publish, wait for it
      if (_throttleStates.TryGetValue(typeof(TEvent), out var state))
      {
        if (state.HasPendingTrailing && state.PendingTask != null)
        {
          await state.PendingTask;
        }
      }
    }

    /// <summary>
    /// Publishes an event immediately without throttling.
    /// </summary>
    public void PublishImmediate<TEvent>(TEvent eventMessage) where TEvent : class
    {
      _eventAggregator.Publish(eventMessage);
    }

    /// <summary>
    /// Flushes any pending throttled events for an event type.
    /// </summary>
    /// <typeparam name="TEvent">The event type to flush.</typeparam>
    public void Flush<TEvent>() where TEvent : class
    {
      if (_throttleStates.TryGetValue(typeof(TEvent), out var state))
      {
        lock (state.Lock)
        {
          if (state.HasPendingTrailing && state.LatestEvent != null)
          {
            _eventAggregator.Publish((TEvent)state.LatestEvent);
            state.HasPendingTrailing = false;
            state.LastPublishTime = DateTime.UtcNow;
          }
        }
      }
    }

    /// <summary>
    /// Flushes all pending throttled events.
    /// </summary>
    public void FlushAll()
    {
      foreach (var kvp in _throttleStates)
      {
        var state = kvp.Value;
        lock (state.Lock)
        {
          if (state.HasPendingTrailing && state.LatestEvent != null)
          {
            _eventAggregator.Publish(state.LatestEvent);
            state.HasPendingTrailing = false;
            state.LastPublishTime = DateTime.UtcNow;
          }
        }
      }
    }

    /// <summary>
    /// Cancels any pending throttled events for an event type.
    /// </summary>
    /// <typeparam name="TEvent">The event type to cancel.</typeparam>
    public void Cancel<TEvent>() where TEvent : class
    {
      if (_throttleStates.TryGetValue(typeof(TEvent), out var state))
      {
        lock (state.Lock)
        {
          state.HasPendingTrailing = false;
          state.LatestEvent = null;
          state.CancellationSource?.Cancel();
        }
      }
    }

    /// <summary>
    /// Configures throttling defaults for a specific event type.
    /// </summary>
    /// <typeparam name="TEvent">The event type to configure.</typeparam>
    /// <param name="throttleMs">Default throttle window for this event type.</param>
    /// <param name="mode">Default throttle mode for this event type.</param>
    public void Configure<TEvent>(int throttleMs, ThrottleMode mode = ThrottleMode.Trailing) where TEvent : class
    {
      var state = _throttleStates.GetOrAdd(typeof(TEvent), _ => new ThrottleState());
      lock (state.Lock)
      {
        state.ThrottleMs = throttleMs;
        state.Mode = mode;
      }
    }

    /// <summary>
    /// Gets statistics about throttled events.
    /// </summary>
    public ThrottleStats GetStats()
    {
      var stats = new ThrottleStats();
      foreach (var kvp in _throttleStates)
      {
        var eventType = kvp.Key.Name;
        var state = kvp.Value;
        lock (state.Lock)
        {
          stats.EventTypeStats[eventType] = new EventThrottleStats
          {
            TotalReceived = state.TotalReceived,
            TotalPublished = state.TotalPublished,
            TotalCoalesced = state.TotalReceived - state.TotalPublished,
            ThrottleMs = state.ThrottleMs,
            Mode = state.Mode
          };
        }
      }
      return stats;
    }

    private void ScheduleTrailingPublish<TEvent>(ThrottleState state, int throttleMs) where TEvent : class
    {
      // Cancel any existing pending task
      state.CancellationSource?.Cancel();
      state.CancellationSource = new CancellationTokenSource();
      var token = state.CancellationSource.Token;

      state.TotalReceived++;

      state.PendingTask = Task.Run(async () =>
      {
        try
        {
          await Task.Delay(throttleMs, token);

          if (token.IsCancellationRequested)
            return;

          lock (state.Lock)
          {
            if (state.HasPendingTrailing && state.LatestEvent != null)
            {
              _eventAggregator.Publish((TEvent)state.LatestEvent);
              state.HasPendingTrailing = false;
              state.LastPublishTime = DateTime.UtcNow;
              state.TotalPublished++;
            }
          }
        }
        catch (OperationCanceledException)
        {
          // Expected when cancelled - trailing edge processing stopped
          System.Diagnostics.Debug.WriteLine(
            "[ThrottledEventPublisher] Trailing publish cancelled (expected during disposal)");
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine(
            $"[ThrottledEventPublisher] Error in trailing publish: {ex.Message}");
        }
      }, token);
    }

    /// <summary>
    /// Disposes the throttled event publisher.
    /// </summary>
    public void Dispose()
    {
      if (_disposed)
        return;

      _disposed = true;

      // Cancel all pending tasks
      foreach (var state in _throttleStates.Values)
      {
        state.CancellationSource?.Cancel();
        state.CancellationSource?.Dispose();
      }

      _throttleStates.Clear();
    }

    /// <summary>
    /// Internal state for throttling a specific event type.
    /// </summary>
    private class ThrottleState
    {
      public object Lock { get; } = new();
      public object? LatestEvent { get; set; }
      public DateTime LastPublishTime { get; set; } = DateTime.MinValue;
      public bool HasPendingTrailing { get; set; }
      public int ThrottleMs { get; set; } = 100;
      public ThrottleMode Mode { get; set; } = ThrottleMode.Trailing;
      public CancellationTokenSource? CancellationSource { get; set; }
      public Task? PendingTask { get; set; }
      public long TotalReceived { get; set; }
      public long TotalPublished { get; set; }
    }
  }

  /// <summary>
  /// GAP-I01: Throttle mode for event publishing.
  /// </summary>
  public enum ThrottleMode
  {
    /// <summary>
    /// Fire immediately on first event, then throttle subsequent events
    /// until the throttle window expires.
    /// </summary>
    Leading = 0,

    /// <summary>
    /// Wait for the throttle window to expire, then fire the last event.
    /// </summary>
    Trailing = 1,

    /// <summary>
    /// Fire immediately on first event, and also fire after the throttle
    /// window expires if additional events arrived.
    /// </summary>
    LeadingAndTrailing = 2
  }

  /// <summary>
  /// GAP-I01: Statistics about throttled event publishing.
  /// </summary>
  public class ThrottleStats
  {
    /// <summary>
    /// Per-event-type throttle statistics.
    /// </summary>
    public ConcurrentDictionary<string, EventThrottleStats> EventTypeStats { get; } = new();

    /// <summary>
    /// Total events received across all types.
    /// </summary>
    public long TotalReceived => EventTypeStats.Values.Sum(s => s.TotalReceived);

    /// <summary>
    /// Total events published across all types.
    /// </summary>
    public long TotalPublished => EventTypeStats.Values.Sum(s => s.TotalPublished);

    /// <summary>
    /// Total events coalesced (not published due to throttling).
    /// </summary>
    public long TotalCoalesced => TotalReceived - TotalPublished;

    /// <summary>
    /// Throttle efficiency (percentage of events coalesced).
    /// </summary>
    public double EfficiencyPercent => TotalReceived > 0 
      ? (double)TotalCoalesced / TotalReceived * 100 
      : 0;
  }

  /// <summary>
  /// GAP-I01: Per-event-type throttle statistics.
  /// </summary>
  public class EventThrottleStats
  {
    /// <summary>
    /// Total events received for this type.
    /// </summary>
    public long TotalReceived { get; init; }

    /// <summary>
    /// Total events published for this type.
    /// </summary>
    public long TotalPublished { get; init; }

    /// <summary>
    /// Total events coalesced for this type.
    /// </summary>
    public long TotalCoalesced { get; init; }

    /// <summary>
    /// Configured throttle window in milliseconds.
    /// </summary>
    public int ThrottleMs { get; init; }

    /// <summary>
    /// Configured throttle mode.
    /// </summary>
    public ThrottleMode Mode { get; init; }
  }
}
