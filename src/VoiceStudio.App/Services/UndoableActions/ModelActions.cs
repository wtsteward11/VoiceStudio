using System;
using System.Collections.ObjectModel;
using System.Linq;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services.UndoableActions
{
  /// <summary>
  /// Undoable action for deleting a model from the registry.
  /// </summary>
  public class DeleteModelAction : IUndoableAction
  {
    private readonly ObservableCollection<ModelInfo> _models;
    private readonly IModelManagerClient _client;
    private readonly ModelInfo _model;
    private readonly int _originalIndex;
    private readonly Action<ModelInfo>? _onUndo;
    private readonly Action<ModelInfo>? _onRedo;

    public string ActionName => $"Delete Model '{_model.Engine}/{_model.ModelName}'";

    public DeleteModelAction(
        ObservableCollection<ModelInfo> models,
        IModelManagerClient client,
        ModelInfo model,
        int originalIndex,
        Action<ModelInfo>? onUndo = null,
        Action<ModelInfo>? onRedo = null)
    {
      _models = models ?? throw new ArgumentNullException(nameof(models));
      _client = client ?? throw new ArgumentNullException(nameof(client));
      _model = model ?? throw new ArgumentNullException(nameof(model));
      _originalIndex = originalIndex;
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    private bool ModelMatches(ModelInfo model)
    {
      return model.Engine == _model.Engine && model.ModelName == _model.ModelName;
    }

    public void Undo()
    {
      if (!_models.Any(ModelMatches))
      {
        // Re-add the model at original position
        // Note: This doesn't re-register it with the backend - it just restores the UI state
        // The model files may still exist, but it won't be in the backend registry
        if (_originalIndex >= 0 && _originalIndex <= _models.Count)
        {
          _models.Insert(_originalIndex, _model);
        }
        else
        {
          // Insert in sorted position (by engine, then name)
          int insertIndex = 0;
          for (int i = 0; i < _models.Count; i++)
          {
            var current = _models[i];
            if (string.Compare(current.Engine, _model.Engine, StringComparison.OrdinalIgnoreCase) > 0 ||
                (current.Engine == _model.Engine && string.Compare(current.ModelName, _model.ModelName, StringComparison.OrdinalIgnoreCase) > 0))
            {
              insertIndex = i;
              break;
            }
            insertIndex = i + 1;
          }
          _models.Insert(insertIndex, _model);
        }
        _onUndo?.Invoke(_model);
      }
    }

    public void Redo()
    {
      var modelToRemove = _models.FirstOrDefault(ModelMatches);
      if (modelToRemove != null)
      {
        _models.Remove(modelToRemove);
        _onRedo?.Invoke(modelToRemove);
      }
    }
  }
}