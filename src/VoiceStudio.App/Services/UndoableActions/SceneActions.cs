using System;
using System.Collections.ObjectModel;
using System.Linq;
using VoiceStudio.Core.Services;
using SceneItem = VoiceStudio.App.ViewModels.SceneItem;

namespace VoiceStudio.App.Services.UndoableActions
{
  /// <summary>
  /// Undoable action for creating a scene.
  /// </summary>
  public class CreateSceneAction : IUndoableAction
  {
    private readonly ObservableCollection<SceneItem> _scenes;
    private readonly ISceneBuilderClient _sceneBuilderClient;
    private readonly SceneItem _scene;
    private readonly Action<SceneItem>? _onUndo;
    private readonly Action<SceneItem>? _onRedo;

    public string ActionName => $"Create Scene '{_scene.Name ?? "Unnamed"}'";

    public CreateSceneAction(
        ObservableCollection<SceneItem> scenes,
        ISceneBuilderClient sceneBuilderClient,
        SceneItem scene,
        Action<SceneItem>? onUndo = null,
        Action<SceneItem>? onRedo = null)
    {
      _scenes = scenes ?? throw new ArgumentNullException(nameof(scenes));
      _sceneBuilderClient = sceneBuilderClient ?? throw new ArgumentNullException(nameof(sceneBuilderClient));
      _scene = scene ?? throw new ArgumentNullException(nameof(scene));
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      var sceneToRemove = _scenes.FirstOrDefault(s => s.Id == _scene.Id);
      if (sceneToRemove != null)
      {
        _scenes.Remove(sceneToRemove);
        _onUndo?.Invoke(sceneToRemove);
      }
    }

    public void Redo()
    {
      if (!_scenes.Any(s => s.Id == _scene.Id))
      {
        _scenes.Add(_scene);
        _onRedo?.Invoke(_scene);
      }
    }
  }

  /// <summary>
  /// Undoable action for deleting a scene.
  /// </summary>
  public class DeleteSceneAction : IUndoableAction
  {
    private readonly ObservableCollection<SceneItem> _scenes;
    private readonly ISceneBuilderClient _sceneBuilderClient;
    private readonly SceneItem _scene;
    private readonly int _originalIndex;
    private readonly Action<SceneItem>? _onUndo;
    private readonly Action<SceneItem>? _onRedo;

    public string ActionName => $"Delete Scene '{_scene.Name ?? "Unnamed"}'";

    public DeleteSceneAction(
        ObservableCollection<SceneItem> scenes,
        ISceneBuilderClient sceneBuilderClient,
        SceneItem scene,
        int originalIndex,
        Action<SceneItem>? onUndo = null,
        Action<SceneItem>? onRedo = null)
    {
      _scenes = scenes ?? throw new ArgumentNullException(nameof(scenes));
      _sceneBuilderClient = sceneBuilderClient ?? throw new ArgumentNullException(nameof(sceneBuilderClient));
      _scene = scene ?? throw new ArgumentNullException(nameof(scene));
      _originalIndex = originalIndex;
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      if (!_scenes.Any(s => s.Id == _scene.Id))
      {
        if (_originalIndex >= 0 && _originalIndex <= _scenes.Count)
        {
          _scenes.Insert(_originalIndex, _scene);
        }
        else
        {
          _scenes.Add(_scene);
        }
        _onUndo?.Invoke(_scene);
      }
    }

    public void Redo()
    {
      var sceneToRemove = _scenes.FirstOrDefault(s => s.Id == _scene.Id);
      if (sceneToRemove != null)
      {
        _scenes.Remove(sceneToRemove);
        _onRedo?.Invoke(sceneToRemove);
      }
    }
  }
}