using System;
using System.Collections.ObjectModel;
using System.Linq;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Services.UndoableActions
{
  /// <summary>
  /// Undoable action for creating a script.
  /// </summary>
  public class CreateScriptAction : IUndoableAction
  {
    private readonly ObservableCollection<ScriptItem> _scripts;
    private readonly IScriptEditorClient _scriptEditorClient;
    private readonly ScriptItem _script;
    private readonly Action<ScriptItem>? _onUndo;
    private readonly Action<ScriptItem>? _onRedo;

    public string ActionName => $"Create Script '{_script.Name ?? "Unnamed"}'";

    public CreateScriptAction(
        ObservableCollection<ScriptItem> scripts,
        IScriptEditorClient scriptEditorClient,
        ScriptItem script,
        Action<ScriptItem>? onUndo = null,
        Action<ScriptItem>? onRedo = null)
    {
      _scripts = scripts ?? throw new ArgumentNullException(nameof(scripts));
      _scriptEditorClient = scriptEditorClient ?? throw new ArgumentNullException(nameof(scriptEditorClient));
      _script = script ?? throw new ArgumentNullException(nameof(script));
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      var scriptToRemove = _scripts.FirstOrDefault(s => s.Id == _script.Id);
      if (scriptToRemove != null)
      {
        _scripts.Remove(scriptToRemove);
        _onUndo?.Invoke(scriptToRemove);
      }
    }

    public void Redo()
    {
      if (!_scripts.Any(s => s.Id == _script.Id))
      {
        _scripts.Add(_script);
        _onRedo?.Invoke(_script);
      }
    }
  }

  /// <summary>
  /// Undoable action for deleting a script.
  /// </summary>
  public class DeleteScriptAction : IUndoableAction
  {
    private readonly ObservableCollection<ScriptItem> _scripts;
    private readonly IScriptEditorClient _scriptEditorClient;
    private readonly ScriptItem _script;
    private readonly int _originalIndex;
    private readonly Action<ScriptItem>? _onUndo;
    private readonly Action<ScriptItem>? _onRedo;

    public string ActionName => $"Delete Script '{_script.Name ?? "Unnamed"}'";

    public DeleteScriptAction(
        ObservableCollection<ScriptItem> scripts,
        IScriptEditorClient scriptEditorClient,
        ScriptItem script,
        int originalIndex,
        Action<ScriptItem>? onUndo = null,
        Action<ScriptItem>? onRedo = null)
    {
      _scripts = scripts ?? throw new ArgumentNullException(nameof(scripts));
      _scriptEditorClient = scriptEditorClient ?? throw new ArgumentNullException(nameof(scriptEditorClient));
      _script = script ?? throw new ArgumentNullException(nameof(script));
      _originalIndex = originalIndex;
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      if (!_scripts.Any(s => s.Id == _script.Id))
      {
        if (_originalIndex >= 0 && _originalIndex <= _scripts.Count)
        {
          _scripts.Insert(_originalIndex, _script);
        }
        else
        {
          _scripts.Add(_script);
        }
        _onUndo?.Invoke(_script);
      }
    }

    public void Redo()
    {
      var scriptToRemove = _scripts.FirstOrDefault(s => s.Id == _script.Id);
      if (scriptToRemove != null)
      {
        _scripts.Remove(scriptToRemove);
        _onRedo?.Invoke(scriptToRemove);
      }
    }
  }

  /// <summary>
  /// Undoable action for adding a segment to a script.
  /// </summary>
  public class AddScriptSegmentAction : IUndoableAction
  {
    private readonly ScriptItem _script;
    private readonly ScriptSegment _segment;
    private readonly IScriptEditorClient _scriptEditorClient;
    private readonly int _originalIndex;
    private readonly Action<ScriptSegment>? _onUndo;
    private readonly Action<ScriptSegment>? _onRedo;

    public string ActionName => "Add Segment";

    public AddScriptSegmentAction(
        ScriptItem script,
        ScriptSegment segment,
        IScriptEditorClient scriptEditorClient,
        Action<ScriptSegment>? onUndo = null,
        Action<ScriptSegment>? onRedo = null)
    {
      _script = script ?? throw new ArgumentNullException(nameof(script));
      _segment = segment ?? throw new ArgumentNullException(nameof(segment));
      _scriptEditorClient = scriptEditorClient ?? throw new ArgumentNullException(nameof(scriptEditorClient));
      _originalIndex = _script.Segments.IndexOf(_segment);
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      if (_script.Segments.Contains(_segment))
      {
        _script.Segments.Remove(_segment);
        _onUndo?.Invoke(_segment);
      }
    }

    public void Redo()
    {
      if (!_script.Segments.Any(s => s.Id == _segment.Id))
      {
        if (_originalIndex >= 0 && _originalIndex <= _script.Segments.Count)
        {
          _script.Segments.Insert(_originalIndex, _segment);
        }
        else
        {
          _script.Segments.Add(_segment);
        }
        _onRedo?.Invoke(_segment);
      }
    }
  }

  /// <summary>
  /// Undoable action for removing a segment from a script.
  /// </summary>
  public class RemoveScriptSegmentAction : IUndoableAction
  {
    private readonly ScriptItem _script;
    private readonly ScriptSegment _segment;
    private readonly IScriptEditorClient _scriptEditorClient;
    private readonly int _originalIndex;
    private readonly Action<ScriptSegment>? _onUndo;
    private readonly Action<ScriptSegment>? _onRedo;

    public string ActionName => "Remove Segment";

    public RemoveScriptSegmentAction(
        ScriptItem script,
        ScriptSegment segment,
        IScriptEditorClient scriptEditorClient,
        int originalIndex,
        Action<ScriptSegment>? onUndo = null,
        Action<ScriptSegment>? onRedo = null)
    {
      _script = script ?? throw new ArgumentNullException(nameof(script));
      _segment = segment ?? throw new ArgumentNullException(nameof(segment));
      _scriptEditorClient = scriptEditorClient ?? throw new ArgumentNullException(nameof(scriptEditorClient));
      _originalIndex = originalIndex;
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      if (!_script.Segments.Any(s => s.Id == _segment.Id))
      {
        if (_originalIndex >= 0 && _originalIndex <= _script.Segments.Count)
        {
          _script.Segments.Insert(_originalIndex, _segment);
        }
        else
        {
          _script.Segments.Add(_segment);
        }
        _onUndo?.Invoke(_segment);
      }
    }

    public void Redo()
    {
      var segmentToRemove = _script.Segments.FirstOrDefault(s => s.Id == _segment.Id);
      if (segmentToRemove != null)
      {
        _script.Segments.Remove(segmentToRemove);
        _onRedo?.Invoke(segmentToRemove);
      }
    }
  }
}