using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Event aggregator service providing pub/sub pattern for cross-panel synchronization.
  /// </summary>
  /// <remarks>
  /// Backend-Frontend Integration Plan - Phase 4: Cross-panel synchronization
  /// Thread-safe implementation that dispatches events to the UI thread when needed.
  /// Phase 1 Enhancement: Monotonic sequence numbers for event ordering.
  /// Phase 6 Enhancement: Telemetry hooks for debugging and analytics.
  /// </remarks>
  public class EventAggregator : IEventAggregator
  {
    private readonly ConcurrentDictionary<Type, List<Subscription>> _subscriptions = new();
    private readonly object _lock = new();
    private readonly DispatcherQueue? _dispatcherQueue;
    
    /// <summary>
    /// Monotonically increasing sequence counter for event ordering.
    /// </summary>
    private long _sequenceCounter;

    /// <summary>
    /// Telemetry callback invoked for each published event.
    /// </summary>
    private Action<EventTelemetry>? _telemetryCallback;

    /// <summary>
    /// Debug replay buffer (circular buffer for recent events).
    /// </summary>
    private readonly ConcurrentQueue<EventTelemetry> _replayBuffer = new();
    private const int MaxReplayBufferSize = 1000;

    /// <summary>
    /// Whether telemetry collection is enabled.
    /// </summary>
    public bool IsTelemetryEnabled { get; private set; }

    public EventAggregator()
    {
      // Try to get the dispatcher for UI thread dispatching
      try
      {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
      }
      catch
      {
        // May not be available during initialization
        _dispatcherQueue = null;
      }
    }

    /// <summary>
    /// Enables telemetry collection with an optional callback.
    /// </summary>
    /// <param name="callback">Optional callback for each event.</param>
    public void EnableTelemetry(Action<EventTelemetry>? callback = null)
    {
      IsTelemetryEnabled = true;
      _telemetryCallback = callback;
    }

    /// <summary>
    /// Disables telemetry collection.
    /// </summary>
    public void DisableTelemetry()
    {
      IsTelemetryEnabled = false;
      _telemetryCallback = null;
    }

    /// <summary>
    /// Gets a snapshot of recent events for debugging/replay.
    /// </summary>
    public IReadOnlyList<EventTelemetry> GetReplayBuffer()
    {
      return _replayBuffer.ToArray();
    }

    /// <summary>
    /// Clears the replay buffer.
    /// </summary>
    public void ClearReplayBuffer()
    {
      while (_replayBuffer.TryDequeue(out _)) { }
    }

    /// <summary>
    /// Gets telemetry statistics.
    /// </summary>
    public EventTelemetryStats GetTelemetryStats()
    {
      var buffer = _replayBuffer.ToArray();
      var byType = buffer.GroupBy(e => e.EventType).ToDictionary(g => g.Key, g => g.Count());
      var bySource = buffer.GroupBy(e => e.SourcePanelId ?? "unknown").ToDictionary(g => g.Key, g => g.Count());
      
      return new EventTelemetryStats
      {
        TotalEvents = Interlocked.Read(ref _sequenceCounter),
        BufferedEvents = buffer.Length,
        EventsByType = byType,
        EventsBySource = bySource
      };
    }

    /// <summary>
    /// Gets the next sequence number for event ordering.
    /// Thread-safe via Interlocked.
    /// </summary>
    public long NextSequence => Interlocked.Read(ref _sequenceCounter);

    /// <inheritdoc />
    public void Publish<TEvent>(TEvent eventMessage) where TEvent : class
    {
      if (eventMessage == null)
        throw new ArgumentNullException(nameof(eventMessage));

      // Assign monotonic sequence number to PanelEventBase events
      AssignSequenceNumber(eventMessage);

      // Record telemetry before dispatching
      RecordTelemetry(eventMessage);

      var eventType = typeof(TEvent);
      
      if (!_subscriptions.TryGetValue(eventType, out var subscriptions))
        return;

      List<Subscription> activeSubscriptions;
      lock (_lock)
      {
        activeSubscriptions = subscriptions.Where(s => s.IsActive).ToList();
      }

      foreach (var subscription in activeSubscriptions)
      {
        try
        {
          InvokeHandler(subscription, eventMessage);
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine($"EventAggregator: Error invoking handler: {ex.Message}");
        }
      }
    }

    /// <inheritdoc />
    public async Task PublishAsync<TEvent>(TEvent eventMessage) where TEvent : class
    {
      if (eventMessage == null)
        throw new ArgumentNullException(nameof(eventMessage));

      // Assign monotonic sequence number to PanelEventBase events
      AssignSequenceNumber(eventMessage);

      // Record telemetry before dispatching
      RecordTelemetry(eventMessage);

      var eventType = typeof(TEvent);
      
      if (!_subscriptions.TryGetValue(eventType, out var subscriptions))
        return;

      List<Subscription> activeSubscriptions;
      lock (_lock)
      {
        activeSubscriptions = subscriptions.Where(s => s.IsActive).ToList();
      }

      var tasks = new List<Task>();
      foreach (var subscription in activeSubscriptions)
      {
        try
        {
          tasks.Add(InvokeHandlerAsync(subscription, eventMessage));
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine($"EventAggregator: Error invoking async handler: {ex.Message}");
        }
      }

      await Task.WhenAll(tasks);
    }

    /// <inheritdoc />
    public ISubscriptionToken Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class
    {
      if (handler == null)
        throw new ArgumentNullException(nameof(handler));

      var eventType = typeof(TEvent);
      var subscription = new Subscription(
        eventType,
        (obj) => handler((TEvent)obj),
        null,
        this
      );

      AddSubscription(eventType, subscription);
      return subscription;
    }

    /// <inheritdoc />
    public ISubscriptionToken Subscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : class
    {
      if (handler == null)
        throw new ArgumentNullException(nameof(handler));

      var eventType = typeof(TEvent);
      var subscription = new Subscription(
        eventType,
        null,
        async (obj) => await handler((TEvent)obj),
        this
      );

      AddSubscription(eventType, subscription);
      return subscription;
    }

    /// <inheritdoc />
    public void Unsubscribe(ISubscriptionToken token)
    {
      if (token is Subscription subscription)
      {
        RemoveSubscription(subscription);
      }
    }

    /// <inheritdoc />
    public void UnsubscribeAll(object subscriber)
    {
      // This implementation doesn't track subscriber objects directly
      // A more sophisticated implementation could track this
      throw new NotSupportedException("UnsubscribeAll by object is not supported. Use individual tokens.");
    }

    private void AddSubscription(Type eventType, Subscription subscription)
    {
      var subscriptions = _subscriptions.GetOrAdd(eventType, _ => new List<Subscription>());
      lock (_lock)
      {
        subscriptions.Add(subscription);
      }
    }

    private void RemoveSubscription(Subscription subscription)
    {
      if (_subscriptions.TryGetValue(subscription.EventType, out var subscriptions))
      {
        lock (_lock)
        {
          subscriptions.Remove(subscription);
        }
      }
    }

    /// <summary>
    /// Records telemetry for an event.
    /// </summary>
    private void RecordTelemetry<TEvent>(TEvent eventMessage)
    {
      if (!IsTelemetryEnabled)
        return;

      string? sourcePanelId = null;
      long sequence = 0;
      InteractionIntent? intent = null;

      // Extract metadata from PanelEventBase
      if (eventMessage is PanelEventBase panelEvent)
      {
        sourcePanelId = panelEvent.SourcePanelId;
        sequence = panelEvent.Sequence;
        intent = panelEvent.Intent;
      }

      var telemetry = new EventTelemetry
      {
        EventType = typeof(TEvent).Name,
        Sequence = sequence,
        Timestamp = DateTime.UtcNow,
        SourcePanelId = sourcePanelId,
        Intent = intent?.ToString()
      };

      // Add to replay buffer (trim if needed)
      _replayBuffer.Enqueue(telemetry);
      while (_replayBuffer.Count > MaxReplayBufferSize)
      {
        _replayBuffer.TryDequeue(out _);
      }

      // Invoke callback if registered
      try
      {
        _telemetryCallback?.Invoke(telemetry);
      }
      // ALLOWED: empty catch - telemetry callback errors must not affect event aggregation
      catch
      {
        // Telemetry callbacks may throw; failures isolated to preserve event flow
      }
    }

    /// <summary>
    /// Assigns a monotonic sequence number to PanelEventBase events.
    /// </summary>
    private void AssignSequenceNumber<TEvent>(TEvent eventMessage)
    {
      // Assign sequence to PanelEventBase events
      if (eventMessage is PanelEventBase panelEvent)
      {
        panelEvent.Sequence = Interlocked.Increment(ref _sequenceCounter);
      }
      // Assign sequence to EventEnvelope events
      else if (eventMessage is object envelope)
      {
        // Use reflection to check for EventEnvelope<T> and set Sequence
        var type = envelope.GetType();
        if (type.IsGenericType && type.GetGenericTypeDefinition().FullName?.StartsWith("VoiceStudio.Core.Events.EventEnvelope") == true)
        {
          var sequenceProperty = type.GetProperty("Sequence");
          sequenceProperty?.SetValue(envelope, Interlocked.Increment(ref _sequenceCounter));
        }
      }
    }

    private void InvokeHandler<TEvent>(Subscription subscription, TEvent eventMessage) where TEvent : class
    {
      if (subscription.SyncHandler != null)
      {
        // Try to dispatch to UI thread if available
        if (_dispatcherQueue != null && !_dispatcherQueue.HasThreadAccess)
        {
          _dispatcherQueue.TryEnqueue(() => subscription.SyncHandler(eventMessage));
        }
        else
        {
          subscription.SyncHandler(eventMessage);
        }
      }
      else if (subscription.AsyncHandler != null)
      {
        // Fire and forget for sync publish of async handler
        _ = InvokeHandlerAsync(subscription, eventMessage);
      }
    }

    private async Task InvokeHandlerAsync<TEvent>(Subscription subscription, TEvent eventMessage) where TEvent : class
    {
      if (subscription.AsyncHandler != null)
      {
        await subscription.AsyncHandler(eventMessage);
      }
      else if (subscription.SyncHandler != null)
      {
        // Wrap sync handler for async execution
        if (_dispatcherQueue != null && !_dispatcherQueue.HasThreadAccess)
        {
          var tcs = new TaskCompletionSource<bool>();
          _dispatcherQueue.TryEnqueue(() =>
          {
            try
            {
              subscription.SyncHandler(eventMessage);
              tcs.SetResult(true);
            }
            catch (Exception ex)
            {
              tcs.SetException(ex);
            }
          });
          await tcs.Task;
        }
        else
        {
          subscription.SyncHandler(eventMessage);
        }
      }
    }

    /// <summary>
    /// Internal subscription class implementing ISubscriptionToken.
    /// </summary>
    private class Subscription : ISubscriptionToken
    {
      private readonly EventAggregator _aggregator;
      private bool _isActive = true;

      public Type EventType { get; }
      public Action<object>? SyncHandler { get; }
      public Func<object, Task>? AsyncHandler { get; }
      public bool IsActive => _isActive;

      public Subscription(
        Type eventType,
        Action<object>? syncHandler,
        Func<object, Task>? asyncHandler,
        EventAggregator aggregator)
      {
        EventType = eventType;
        SyncHandler = syncHandler;
        AsyncHandler = asyncHandler;
        _aggregator = aggregator;
      }

      public void Dispose()
      {
        if (_isActive)
        {
          _isActive = false;
          _aggregator.RemoveSubscription(this);
        }
      }
    }
  }

  /// <summary>
  /// Telemetry data for a single event.
  /// </summary>
  public sealed class EventTelemetry
  {
    /// <summary>
    /// Type name of the event.
    /// </summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>
    /// Monotonic sequence number.
    /// </summary>
    public long Sequence { get; init; }

    /// <summary>
    /// UTC timestamp when the event was published.
    /// </summary>
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// Source panel ID (if available).
    /// </summary>
    public string? SourcePanelId { get; init; }

    /// <summary>
    /// Interaction intent (if available).
    /// </summary>
    public string? Intent { get; init; }
  }

  /// <summary>
  /// Aggregated telemetry statistics.
  /// </summary>
  public sealed class EventTelemetryStats
  {
    /// <summary>
    /// Total events published since startup.
    /// </summary>
    public long TotalEvents { get; init; }

    /// <summary>
    /// Number of events in replay buffer.
    /// </summary>
    public int BufferedEvents { get; init; }

    /// <summary>
    /// Event counts by type.
    /// </summary>
    public IReadOnlyDictionary<string, int> EventsByType { get; init; } = new Dictionary<string, int>();

    /// <summary>
    /// Event counts by source panel.
    /// </summary>
    public IReadOnlyDictionary<string, int> EventsBySource { get; init; } = new Dictionary<string, int>();
  }
}
