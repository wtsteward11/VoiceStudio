using System;
using System.Collections.ObjectModel;
using System.Linq;
using VoiceStudio.Core.Services;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Services.UndoableActions
{
  /// <summary>
  /// Undoable action for adding an audio file to a training dataset.
  /// </summary>
  public class AddDatasetAudioAction : IUndoableAction
  {
    private readonly DatasetDetailItem _dataset;
    private readonly DatasetAudioFileItem _audioFile;
    private readonly ITrainingDatasetEditorClient _datasetEditorClient;
    private readonly int _originalIndex;
    private readonly Action<DatasetAudioFileItem>? _onUndo;
    private readonly Action<DatasetAudioFileItem>? _onRedo;

    public string ActionName => "Add Audio to Dataset";

    public AddDatasetAudioAction(
        DatasetDetailItem dataset,
        DatasetAudioFileItem audioFile,
        ITrainingDatasetEditorClient datasetEditorClient,
        Action<DatasetAudioFileItem>? onUndo = null,
        Action<DatasetAudioFileItem>? onRedo = null)
    {
      _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
      _audioFile = audioFile ?? throw new ArgumentNullException(nameof(audioFile));
      _datasetEditorClient = datasetEditorClient ?? throw new ArgumentNullException(nameof(datasetEditorClient));
      _originalIndex = dataset.AudioFiles.IndexOf(audioFile);
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      if (_dataset.AudioFiles.Contains(_audioFile))
      {
        _dataset.AudioFiles.Remove(_audioFile);
        _onUndo?.Invoke(_audioFile);
      }
    }

    public void Redo()
    {
      if (!_dataset.AudioFiles.Any(a => a.Id == _audioFile.Id))
      {
        if (_originalIndex >= 0 && _originalIndex <= _dataset.AudioFiles.Count)
        {
          _dataset.AudioFiles.Insert(_originalIndex, _audioFile);
        }
        else
        {
          _dataset.AudioFiles.Add(_audioFile);
        }
        _onRedo?.Invoke(_audioFile);
      }
    }
  }

  /// <summary>
  /// Undoable action for removing an audio file from a training dataset.
  /// </summary>
  public class RemoveDatasetAudioAction : IUndoableAction
  {
    private readonly DatasetDetailItem _dataset;
    private readonly DatasetAudioFileItem _audioFile;
    private readonly ITrainingDatasetEditorClient _datasetEditorClient;
    private readonly int _originalIndex;
    private readonly Action<DatasetAudioFileItem>? _onUndo;
    private readonly Action<DatasetAudioFileItem>? _onRedo;

    public string ActionName => "Remove Audio from Dataset";

    public RemoveDatasetAudioAction(
        DatasetDetailItem dataset,
        DatasetAudioFileItem audioFile,
        ITrainingDatasetEditorClient datasetEditorClient,
        int originalIndex,
        Action<DatasetAudioFileItem>? onUndo = null,
        Action<DatasetAudioFileItem>? onRedo = null)
    {
      _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
      _audioFile = audioFile ?? throw new ArgumentNullException(nameof(audioFile));
      _datasetEditorClient = datasetEditorClient ?? throw new ArgumentNullException(nameof(datasetEditorClient));
      _originalIndex = originalIndex;
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      if (!_dataset.AudioFiles.Any(a => a.Id == _audioFile.Id))
      {
        if (_originalIndex >= 0 && _originalIndex <= _dataset.AudioFiles.Count)
        {
          _dataset.AudioFiles.Insert(_originalIndex, _audioFile);
        }
        else
        {
          _dataset.AudioFiles.Add(_audioFile);
        }
        _onUndo?.Invoke(_audioFile);
      }
    }

    public void Redo()
    {
      var audioFileToRemove = _dataset.AudioFiles.FirstOrDefault(a => a.Id == _audioFile.Id);
      if (audioFileToRemove != null)
      {
        _dataset.AudioFiles.Remove(audioFileToRemove);
        _onRedo?.Invoke(audioFileToRemove);
      }
    }
  }

  /// <summary>
  /// Undoable action for updating an audio file in a training dataset.
  /// </summary>
  public class UpdateDatasetAudioAction : IUndoableAction
  {
    private readonly DatasetDetailItem _dataset;
    private readonly DatasetAudioFileItem _audioFile;
    private readonly ITrainingDatasetEditorClient _datasetEditorClient;
    private readonly string _originalTranscript;
    private readonly int _originalOrder;
    private readonly string _newTranscript;
    private readonly int _newOrder;
    private readonly Action<DatasetAudioFileItem>? _onUndo;
    private readonly Action<DatasetAudioFileItem>? _onRedo;

    public string ActionName => "Update Audio in Dataset";

    public UpdateDatasetAudioAction(
        DatasetDetailItem dataset,
        DatasetAudioFileItem audioFile,
        ITrainingDatasetEditorClient datasetEditorClient,
        string originalTranscript,
        int originalOrder,
        string newTranscript,
        int newOrder,
        Action<DatasetAudioFileItem>? onUndo = null,
        Action<DatasetAudioFileItem>? onRedo = null)
    {
      _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
      _audioFile = audioFile ?? throw new ArgumentNullException(nameof(audioFile));
      _datasetEditorClient = datasetEditorClient ?? throw new ArgumentNullException(nameof(datasetEditorClient));
      _originalTranscript = originalTranscript;
      _originalOrder = originalOrder;
      _newTranscript = newTranscript;
      _newOrder = newOrder;
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      var audioFile = _dataset.AudioFiles.FirstOrDefault(a => a.Id == _audioFile.Id);
      if (audioFile != null)
      {
        audioFile.Transcript = _originalTranscript;
        audioFile.Order = _originalOrder;
        // Properties are ObservableObject so they notify automatically
        _onUndo?.Invoke(audioFile);
      }
    }

    public void Redo()
    {
      var audioFile = _dataset.AudioFiles.FirstOrDefault(a => a.Id == _audioFile.Id);
      if (audioFile != null)
      {
        audioFile.Transcript = _newTranscript;
        audioFile.Order = _newOrder;
        // Properties are ObservableObject so they notify automatically
        _onRedo?.Invoke(audioFile);
      }
    }
  }
}