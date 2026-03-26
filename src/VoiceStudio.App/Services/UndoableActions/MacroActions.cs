using System;
using System.Collections.ObjectModel;
using System.Linq;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services.UndoableActions
{
  /// <summary>
  /// Undoable action for creating a macro.
  /// </summary>
  public class CreateMacroAction : IUndoableAction
  {
    private readonly ObservableCollection<Macro> _macros;
    private readonly IMacroClient _client;
    private readonly Macro _macro;
    private readonly Action<Macro>? _onUndo;
    private readonly Action<Macro>? _onRedo;

    public string ActionName => $"Create Macro '{_macro.Name ?? "Unnamed"}'";

    public CreateMacroAction(
        ObservableCollection<Macro> macros,
        IMacroClient client,
        Macro macro,
        Action<Macro>? onUndo = null,
        Action<Macro>? onRedo = null)
    {
      _macros = macros ?? throw new ArgumentNullException(nameof(macros));
      _client = client ?? throw new ArgumentNullException(nameof(client));
      _macro = macro ?? throw new ArgumentNullException(nameof(macro));
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      var macroToRemove = _macros.FirstOrDefault(m => m.Id == _macro.Id);
      if (macroToRemove != null)
      {
        _macros.Remove(macroToRemove);
        _onUndo?.Invoke(macroToRemove);
      }
    }

    public void Redo()
    {
      if (!_macros.Any(m => m.Id == _macro.Id))
      {
        _macros.Add(_macro);
        _onRedo?.Invoke(_macro);
      }
    }
  }

  /// <summary>
  /// Undoable action for deleting a macro.
  /// </summary>
  public class DeleteMacroAction : IUndoableAction
  {
    private readonly ObservableCollection<Macro> _macros;
    private readonly IMacroClient _client;
    private readonly Macro _macro;
    private readonly int _originalIndex;
    private readonly Action<Macro>? _onUndo;
    private readonly Action<Macro>? _onRedo;

    public string ActionName => $"Delete Macro '{_macro.Name ?? "Unnamed"}'";

    public DeleteMacroAction(
        ObservableCollection<Macro> macros,
        IMacroClient client,
        Macro macro,
        int originalIndex,
        Action<Macro>? onUndo = null,
        Action<Macro>? onRedo = null)
    {
      _macros = macros ?? throw new ArgumentNullException(nameof(macros));
      _client = client ?? throw new ArgumentNullException(nameof(client));
      _macro = macro ?? throw new ArgumentNullException(nameof(macro));
      _originalIndex = originalIndex;
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      if (!_macros.Any(m => m.Id == _macro.Id))
      {
        if (_originalIndex >= 0 && _originalIndex <= _macros.Count)
        {
          _macros.Insert(_originalIndex, _macro);
        }
        else
        {
          _macros.Add(_macro);
        }
        _onUndo?.Invoke(_macro);
      }
    }

    public void Redo()
    {
      var macroToRemove = _macros.FirstOrDefault(m => m.Id == _macro.Id);
      if (macroToRemove != null)
      {
        _macros.Remove(macroToRemove);
        _onRedo?.Invoke(macroToRemove);
      }
    }
  }

  /// <summary>
  /// Undoable action for creating an automation curve (macro version).
  /// </summary>
  public class CreateAutomationCurveMacroAction : IUndoableAction
  {
    private readonly ObservableCollection<AutomationCurve> _curves;
    private readonly IMacroClient _client;
    private readonly AutomationCurve _curve;
    private readonly Action<AutomationCurve>? _onUndo;
    private readonly Action<AutomationCurve>? _onRedo;

    public string ActionName => $"Create Automation Curve '{_curve.Name ?? "Unnamed"}'";

    public CreateAutomationCurveMacroAction(
        ObservableCollection<AutomationCurve> curves,
        IMacroClient client,
        AutomationCurve curve,
        Action<AutomationCurve>? onUndo = null,
        Action<AutomationCurve>? onRedo = null)
    {
      _curves = curves ?? throw new ArgumentNullException(nameof(curves));
      _client = client ?? throw new ArgumentNullException(nameof(client));
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
  /// Undoable action for deleting an automation curve (macro version).
  /// </summary>
  public class DeleteAutomationCurveMacroAction : IUndoableAction
  {
    private readonly ObservableCollection<AutomationCurve> _curves;
    private readonly IMacroClient _client;
    private readonly AutomationCurve _curve;
    private readonly int _originalIndex;
    private readonly Action<AutomationCurve>? _onUndo;
    private readonly Action<AutomationCurve>? _onRedo;

    public string ActionName => $"Delete Automation Curve '{_curve.Name ?? "Unnamed"}'";

    public DeleteAutomationCurveMacroAction(
        ObservableCollection<AutomationCurve> curves,
        IMacroClient client,
        AutomationCurve curve,
        int originalIndex,
        Action<AutomationCurve>? onUndo = null,
        Action<AutomationCurve>? onRedo = null)
    {
      _curves = curves ?? throw new ArgumentNullException(nameof(curves));
      _client = client ?? throw new ArgumentNullException(nameof(client));
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