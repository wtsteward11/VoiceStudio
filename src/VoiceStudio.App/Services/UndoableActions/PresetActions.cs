using System;
using System.Collections.ObjectModel;
using System.Linq;
using Preset = VoiceStudio.App.ViewModels.Preset;

namespace VoiceStudio.App.Services.UndoableActions
{
  /// <summary>
  /// Undoable action for creating a preset. Undo/Redo are local-only (collection mutation).
  /// </summary>
  public class CreatePresetAction : IUndoableAction
  {
    private readonly ObservableCollection<Preset> _presets;
    private readonly Preset _preset;
    private readonly Action<Preset>? _onUndo;
    private readonly Action<Preset>? _onRedo;

    public string ActionName => $"Create Preset '{_preset.Name ?? "Unnamed"}'";

    public CreatePresetAction(
        ObservableCollection<Preset> presets,
        Preset preset,
        Action<Preset>? onUndo = null,
        Action<Preset>? onRedo = null)
    {
      _presets = presets ?? throw new ArgumentNullException(nameof(presets));
      _preset = preset ?? throw new ArgumentNullException(nameof(preset));
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      var presetToRemove = _presets.FirstOrDefault(p => p.Id == _preset.Id);
      if (presetToRemove != null)
      {
        _presets.Remove(presetToRemove);
        _onUndo?.Invoke(presetToRemove);
      }
    }

    public void Redo()
    {
      if (!_presets.Any(p => p.Id == _preset.Id))
      {
        _presets.Insert(0, _preset);
        _onRedo?.Invoke(_preset);
      }
    }
  }

  /// <summary>
  /// Undoable action for deleting a preset. Undo/Redo are local-only (collection mutation).
  /// </summary>
  public class DeletePresetAction : IUndoableAction
  {
    private readonly ObservableCollection<Preset> _presets;
    private readonly Preset _preset;
    private readonly int _originalIndex;
    private readonly Action<Preset>? _onUndo;
    private readonly Action<Preset>? _onRedo;

    public string ActionName => $"Delete Preset '{_preset.Name ?? "Unnamed"}'";

    public DeletePresetAction(
        ObservableCollection<Preset> presets,
        Preset preset,
        int originalIndex,
        Action<Preset>? onUndo = null,
        Action<Preset>? onRedo = null)
    {
      _presets = presets ?? throw new ArgumentNullException(nameof(presets));
      _preset = preset ?? throw new ArgumentNullException(nameof(preset));
      _originalIndex = originalIndex;
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      if (!_presets.Any(p => p.Id == _preset.Id))
      {
        if (_originalIndex >= 0 && _originalIndex <= _presets.Count)
        {
          _presets.Insert(_originalIndex, _preset);
        }
        else
        {
          _presets.Insert(0, _preset);
        }
        _onUndo?.Invoke(_preset);
      }
    }

    public void Redo()
    {
      var presetToRemove = _presets.FirstOrDefault(p => p.Id == _preset.Id);
      if (presetToRemove != null)
      {
        _presets.Remove(presetToRemove);
        _onRedo?.Invoke(presetToRemove);
      }
    }
  }
}