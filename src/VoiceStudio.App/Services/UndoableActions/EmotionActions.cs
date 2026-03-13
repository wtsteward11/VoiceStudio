using System;
using System.Collections.ObjectModel;
using System.Linq;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Services.UndoableActions
{
  /// <summary>
  /// Undoable action for creating an emotion preset.
  /// </summary>
  public class CreateEmotionPresetAction : IUndoableAction
  {
    private readonly ObservableCollection<EmotionControlPresetItem> _presets;
    private readonly EmotionControlPresetItem _preset;
    private readonly Action<EmotionControlPresetItem>? _onUndo;
    private readonly Action<EmotionControlPresetItem>? _onRedo;

    public string ActionName => $"Create Emotion Preset '{_preset.Name ?? "Unnamed"}'";

    public CreateEmotionPresetAction(
        ObservableCollection<EmotionControlPresetItem> presets,
        EmotionControlPresetItem preset,
        Action<EmotionControlPresetItem>? onUndo = null,
        Action<EmotionControlPresetItem>? onRedo = null)
    {
      _presets = presets ?? throw new ArgumentNullException(nameof(presets));
      _preset = preset ?? throw new ArgumentNullException(nameof(preset));
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      var presetToRemove = _presets.FirstOrDefault(p => p.PresetId == _preset.PresetId);
      if (presetToRemove != null)
      {
        _presets.Remove(presetToRemove);
        _onUndo?.Invoke(presetToRemove);
      }
    }

    public void Redo()
    {
      if (!_presets.Any(p => p.PresetId == _preset.PresetId))
      {
        _presets.Add(_preset);
        _onRedo?.Invoke(_preset);
      }
    }
  }

  /// <summary>
  /// Undoable action for deleting an emotion preset.
  /// </summary>
  public class DeleteEmotionPresetAction : IUndoableAction
  {
    private readonly ObservableCollection<EmotionControlPresetItem> _presets;
    private readonly EmotionControlPresetItem _preset;
    private readonly int _originalIndex;
    private readonly Action<EmotionControlPresetItem>? _onUndo;
    private readonly Action<EmotionControlPresetItem>? _onRedo;

    public string ActionName => $"Delete Emotion Preset '{_preset.Name ?? "Unnamed"}'";

    public DeleteEmotionPresetAction(
        ObservableCollection<EmotionControlPresetItem> presets,
        EmotionControlPresetItem preset,
        int originalIndex,
        Action<EmotionControlPresetItem>? onUndo = null,
        Action<EmotionControlPresetItem>? onRedo = null)
    {
      _presets = presets ?? throw new ArgumentNullException(nameof(presets));
      _preset = preset ?? throw new ArgumentNullException(nameof(preset));
      _originalIndex = originalIndex;
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      if (!_presets.Any(p => p.PresetId == _preset.PresetId))
      {
        if (_originalIndex >= 0 && _originalIndex <= _presets.Count)
        {
          _presets.Insert(_originalIndex, _preset);
        }
        else
        {
          _presets.Add(_preset);
        }
        _onUndo?.Invoke(_preset);
      }
    }

    public void Redo()
    {
      var presetToRemove = _presets.FirstOrDefault(p => p.PresetId == _preset.PresetId);
      if (presetToRemove != null)
      {
        _presets.Remove(presetToRemove);
        _onRedo?.Invoke(presetToRemove);
      }
    }
  }
}