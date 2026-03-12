using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VoiceStudio.Core.State;
using VoiceStudio.Core.State.Commands;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Central application state store with undo/redo support.
  /// Implements Redux-like pattern for predictable state management.
  /// </summary>
  public sealed class AppStateStore : IAppStateStore
  {
    private readonly object _lock = new();
    private readonly Stack<UndoFrame> _undoStack = new();
    private readonly Stack<UndoFrame> _redoStack = new();
    private readonly List<SubscriptionEntry> _subscriptions = new();
    private readonly int _maxUndoHistory;

    private AppState _state = AppState.Empty;
    private int _subscriptionId;

    /// <summary>
    /// Initializes a new instance of AppStateStore.
    /// </summary>
    /// <param name="maxUndoHistory">Maximum number of undo operations to retain.</param>
    public AppStateStore(int maxUndoHistory = 100)
    {
      _maxUndoHistory = maxUndoHistory;
    }

    /// <inheritdoc />
    public AppState State
    {
      get
      {
        lock (_lock) return _state;
      }
    }

    /// <inheritdoc />
    public event EventHandler<StateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Gets whether undo is available.
    /// </summary>
    public bool CanUndo
    {
      get
      {
        lock (_lock) return _undoStack.Count > 0;
      }
    }

    /// <summary>
    /// Gets whether redo is available.
    /// </summary>
    public bool CanRedo
    {
      get
      {
        lock (_lock) return _redoStack.Count > 0;
      }
    }

    /// <summary>
    /// Gets the count of undo operations available.
    /// </summary>
    public int UndoCount
    {
      get
      {
        lock (_lock) return _undoStack.Count;
      }
    }

    /// <summary>
    /// Gets the count of redo operations available.
    /// </summary>
    public int RedoCount
    {
      get
      {
        lock (_lock) return _redoStack.Count;
      }
    }

    /// <inheritdoc />
    public void Dispatch(Func<AppState, AppState> update)
    {
      AppState previousState;
      AppState newState;

      lock (_lock)
      {
        previousState = _state;
        newState = update(_state);
        _state = newState;
      }

      if (!ReferenceEquals(previousState, newState))
      {
        NotifyStateChanged(previousState, newState, null);
      }
    }

    /// <summary>
    /// Dispatches an undoable command.
    /// </summary>
    public void Dispatch(IUndoableCommand command)
    {
      AppState previousState;
      AppState newState;

      lock (_lock)
      {
        previousState = _state;
        newState = command.Execute(_state);
        _state = newState;

        // Clear redo stack on new action
        _redoStack.Clear();

        // Add to undo stack with merge support
        if (command.CanMerge && _undoStack.Count > 0)
        {
          var topFrame = _undoStack.Peek();
          if (topFrame.Command is IUndoableCommand topCmd)
          {
            var merged = topCmd.Merge(command);
            if (merged != null)
            {
              _undoStack.Pop();
              _undoStack.Push(new UndoFrame(merged, topFrame.StateBefore));
              NotifyStateChanged(previousState, newState, command.Name);
              return;
            }
          }
        }

        // Push new undo frame
        _undoStack.Push(new UndoFrame(command, previousState));

        // Trim undo history if needed
        while (_undoStack.Count > _maxUndoHistory)
        {
          TrimOldestUndo();
        }
      }

      NotifyStateChanged(previousState, newState, command.Name);
    }

    /// <summary>
    /// Dispatches a non-undoable command.
    /// </summary>
    public void Dispatch(IStateCommand command)
    {
      if (command is IUndoableCommand undoable)
      {
        Dispatch(undoable);
        return;
      }

      AppState previousState;
      AppState newState;

      lock (_lock)
      {
        previousState = _state;
        newState = command.Execute(_state);
        _state = newState;
      }

      if (!ReferenceEquals(previousState, newState))
      {
        NotifyStateChanged(previousState, newState, command.Name);
      }
    }

    /// <inheritdoc />
    public void ExecuteCommand(IStateCommand command)
    {
      Dispatch(command);
    }

    // Note: Undo() and Redo() already implemented below.
    // The interface expects void return type for simplicity;
    // internal callers can use the bool-returning versions.

    /// <inheritdoc />
    public async Task DispatchAsync(Func<AppState, Task<AppState>> update)
    {
      AppState previousState;
      AppState newState;

      lock (_lock)
      {
        previousState = _state;
      }

      newState = await update(previousState);

      lock (_lock)
      {
        _state = newState;
      }

      if (!ReferenceEquals(previousState, newState))
      {
        NotifyStateChanged(previousState, newState, null);
      }
    }

    /// <summary>
    /// Undoes the last command.
    /// </summary>
    /// <returns>True if undo was performed.</returns>
    public bool Undo()
    {
      AppState previousState;
      AppState newState;
      string? actionName;

      lock (_lock)
      {
        if (_undoStack.Count == 0)
          return false;

        var frame = _undoStack.Pop();
        previousState = _state;

        // Use the command's Undo method if available, otherwise restore snapshot
        if (frame.Command is IUndoableCommand undoable)
        {
          newState = undoable.Undo(_state);
        }
        else
        {
          newState = frame.StateBefore;
        }

        _state = newState;
        actionName = frame.Command.Name;

        // Push to redo stack
        _redoStack.Push(new UndoFrame(frame.Command, previousState));
      }

      NotifyStateChanged(previousState, newState, $"Undo: {actionName}");
      return true;
    }

    /// <summary>
    /// Redoes the last undone command.
    /// </summary>
    /// <returns>True if redo was performed.</returns>
    public bool Redo()
    {
      AppState previousState;
      AppState newState;
      string? actionName;

      lock (_lock)
      {
        if (_redoStack.Count == 0)
          return false;

        var frame = _redoStack.Pop();
        previousState = _state;
        newState = frame.Command.Execute(_state);
        _state = newState;
        actionName = frame.Command.Name;

        // Push back to undo stack
        _undoStack.Push(new UndoFrame(frame.Command, previousState));
      }

      NotifyStateChanged(previousState, newState, $"Redo: {actionName}");
      return true;
    }

    /// <summary>
    /// Clears all undo/redo history.
    /// </summary>
    public void ClearHistory()
    {
      lock (_lock)
      {
        _undoStack.Clear();
        _redoStack.Clear();
      }
    }

    /// <inheritdoc />
    public IDisposable Subscribe<T>(Func<AppState, T> selector, Action<T> handler)
    {
      int id;
      lock (_lock)
      {
        id = ++_subscriptionId;
        _subscriptions.Add(new SubscriptionEntry(id, state =>
        {
          var value = selector(state);
          handler(value);
        }));
      }

      return new SubscriptionDisposable(this, id);
    }

    /// <inheritdoc />
    public T Select<T>(Func<AppState, T> selector)
    {
      lock (_lock)
      {
        return selector(_state);
      }
    }

    private void NotifyStateChanged(AppState previousState, AppState newState, string? actionName)
    {
      // Raise event
      StateChanged?.Invoke(this, new StateChangedEventArgs(previousState, newState, actionName));

      // Notify subscriptions
      List<SubscriptionEntry> subs;
      lock (_lock)
      {
        subs = new List<SubscriptionEntry>(_subscriptions);
      }

      foreach (var sub in subs)
      {
        try
        {
          sub.Handler(newState);
        }
        // ALLOWED: empty catch - subscriber handler errors must not propagate to state store
        catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "AppStateStore.NotifyStateChanged");
      }
      }
    }

    private void TrimOldestUndo()
    {
      // Convert stack to array, remove oldest, rebuild stack
      var items = _undoStack.ToArray();
      _undoStack.Clear();
      for (int i = items.Length - 2; i >= 0; i--)
      {
        _undoStack.Push(items[i]);
      }
    }

    private void Unsubscribe(int id)
    {
      lock (_lock)
      {
        _subscriptions.RemoveAll(s => s.Id == id);
      }
    }

    private sealed class UndoFrame
    {
      public IStateCommand Command { get; }
      public AppState StateBefore { get; }

      public UndoFrame(IStateCommand command, AppState stateBefore)
      {
        Command = command;
        StateBefore = stateBefore;
      }
    }

    private sealed class SubscriptionEntry
    {
      public int Id { get; }
      public Action<AppState> Handler { get; }

      public SubscriptionEntry(int id, Action<AppState> handler)
      {
        Id = id;
        Handler = handler;
      }
    }

    private sealed class SubscriptionDisposable : IDisposable
    {
      private readonly AppStateStore _store;
      private readonly int _id;
      private bool _disposed;

      public SubscriptionDisposable(AppStateStore store, int id)
      {
        _store = store;
        _id = id;
      }

      public void Dispose()
      {
        if (!_disposed)
        {
          _disposed = true;
          _store.Unsubscribe(_id);
        }
      }
    }
  }
}
