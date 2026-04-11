using System;
using System.Collections.Generic;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services.UndoableActions;

/// <summary>
/// Composes multiple <see cref="IUndoableAction"/> instances (e.g. batch delete). Undo runs in reverse order.
/// </summary>
public sealed class CompositeUndoAction : IUndoableAction
{
  private readonly IReadOnlyList<IUndoableAction> _actions;
  private readonly IErrorLoggingService? _log;

  public CompositeUndoAction(string actionName, IReadOnlyList<IUndoableAction> actions, IErrorLoggingService? log = null)
  {
    ActionName = actionName ?? throw new ArgumentNullException(nameof(actionName));
    _actions = actions ?? throw new ArgumentNullException(nameof(actions));
    _log = log;
    if (_actions.Count == 0)
      throw new ArgumentException("At least one action is required.", nameof(actions));
  }

  public string ActionName { get; }

  public void Undo()
  {
    for (var i = _actions.Count - 1; i >= 0; i--)
    {
      try
      {
        _actions[i].Undo();
      }
      catch (Exception ex)
      {
        _log?.LogError(ex, $"CompositeUndoAction.Undo step {i}");
        throw;
      }
    }
  }

  public void Redo()
  {
    for (var i = 0; i < _actions.Count; i++)
    {
      try
      {
        _actions[i].Redo();
      }
      catch (Exception ex)
      {
        _log?.LogError(ex, $"CompositeUndoAction.Redo step {i}");
        throw;
      }
    }
  }
}
