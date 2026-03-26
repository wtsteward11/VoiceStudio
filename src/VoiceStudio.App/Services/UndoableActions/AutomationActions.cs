using System;
using System.Collections.ObjectModel;
using System.Linq;
using VoiceStudio.Core.Services;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Services.UndoableActions
{
  /// <summary>
  /// Undoable action for creating an automation curve.
  /// </summary>
  public class CreateAutomationCurveAction : IUndoableAction
  {
    private readonly ObservableCollection<AutomationCurveItem> _curves;
    private readonly IAutomationClient _automationClient;
    private readonly AutomationCurveItem _curve;
    private readonly Action<AutomationCurveItem>? _onUndo;
    private readonly Action<AutomationCurveItem>? _onRedo;

    public string ActionName => $"Create Automation Curve '{_curve.Name}'";

    public CreateAutomationCurveAction(
        ObservableCollection<AutomationCurveItem> curves,
        IAutomationClient automationClient,
        AutomationCurveItem curve,
        Action<AutomationCurveItem>? onUndo = null,
        Action<AutomationCurveItem>? onRedo = null)
    {
      _curves = curves ?? throw new ArgumentNullException(nameof(curves));
      _automationClient = automationClient ?? throw new ArgumentNullException(nameof(automationClient));
      _curve = curve ?? throw new ArgumentNullException(nameof(curve));
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      var curveToRemove = _curves.FirstOrDefault(c => c.Id == _curve.Id);
      if (curveToRemove != null)
      {
        _curves.Remove(curveToRemove);
        _onUndo?.Invoke(curveToRemove);
      }
    }

    public void Redo()
    {
      if (!_curves.Any(c => c.Id == _curve.Id))
      {
        _curves.Add(_curve);
        _onRedo?.Invoke(_curve);
      }
    }
  }

  /// <summary>
  /// Undoable action for deleting an automation curve.
  /// </summary>
  public class DeleteAutomationCurveAction : IUndoableAction
  {
    private readonly ObservableCollection<AutomationCurveItem> _curves;
    private readonly IAutomationClient _automationClient;
    private readonly AutomationCurveItem _curve;
    private readonly int _originalIndex;
    private readonly Action<AutomationCurveItem>? _onUndo;
    private readonly Action<AutomationCurveItem>? _onRedo;

    public string ActionName => $"Delete Automation Curve '{_curve.Name}'";

    public DeleteAutomationCurveAction(
        ObservableCollection<AutomationCurveItem> curves,
        IAutomationClient automationClient,
        AutomationCurveItem curve,
        int originalIndex,
        Action<AutomationCurveItem>? onUndo = null,
        Action<AutomationCurveItem>? onRedo = null)
    {
      _curves = curves ?? throw new ArgumentNullException(nameof(curves));
      _automationClient = automationClient ?? throw new ArgumentNullException(nameof(automationClient));
      _curve = curve ?? throw new ArgumentNullException(nameof(curve));
      _originalIndex = originalIndex;
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      if (!_curves.Any(c => c.Id == _curve.Id))
      {
        if (_originalIndex >= 0 && _originalIndex <= _curves.Count)
        {
          _curves.Insert(_originalIndex, _curve);
        }
        else
        {
          _curves.Add(_curve);
        }
        _onUndo?.Invoke(_curve);
      }
    }

    public void Redo()
    {
      var curveToRemove = _curves.FirstOrDefault(c => c.Id == _curve.Id);
      if (curveToRemove != null)
      {
        _curves.Remove(curveToRemove);
        _onRedo?.Invoke(curveToRemove);
      }
    }
  }
}