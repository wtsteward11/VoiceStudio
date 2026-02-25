using System;
using System.Collections.ObjectModel;
using System.Linq;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services.UndoableActions
{
  /// <summary>
  /// Undoable action for creating a training dataset.
  /// </summary>
  public class CreateTrainingDatasetAction : IUndoableAction
  {
    private readonly ObservableCollection<TrainingDataset> _datasets;
    private readonly IBackendClient _backendClient;
    private readonly TrainingDataset _dataset;
    private readonly Action<TrainingDataset>? _onUndo;
    private readonly Action<TrainingDataset>? _onRedo;

    public string ActionName => $"Create Training Dataset '{_dataset.Name ?? "Unnamed"}'";

    public CreateTrainingDatasetAction(
        ObservableCollection<TrainingDataset> datasets,
        IBackendClient backendClient,
        TrainingDataset dataset,
        Action<TrainingDataset>? onUndo = null,
        Action<TrainingDataset>? onRedo = null)
    {
      _datasets = datasets ?? throw new ArgumentNullException(nameof(datasets));
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
      _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      var datasetToRemove = _datasets.FirstOrDefault(d => d.Id == _dataset.Id);
      if (datasetToRemove != null)
      {
        _datasets.Remove(datasetToRemove);
        _onUndo?.Invoke(datasetToRemove);
      }
    }

    public void Redo()
    {
      if (!_datasets.Any(d => d.Id == _dataset.Id))
      {
        _datasets.Insert(0, _dataset);
        _onRedo?.Invoke(_dataset);
      }
    }
  }

  /// <summary>
  /// Undoable action for deleting a training dataset.
  /// Undo re-inserts the dataset at its original position.
  /// </summary>
  public class DeleteTrainingDatasetAction : IUndoableAction
  {
    private readonly ObservableCollection<TrainingDataset> _datasets;
    private readonly IBackendClient _backendClient;
    private readonly TrainingDataset _dataset;
    private readonly int _originalIndex;

    public string ActionName => $"Delete Training Dataset '{_dataset.Name ?? "Unnamed"}'";

    public DeleteTrainingDatasetAction(
        ObservableCollection<TrainingDataset> datasets,
        IBackendClient backendClient,
        TrainingDataset dataset,
        int originalIndex)
    {
      _datasets = datasets ?? throw new ArgumentNullException(nameof(datasets));
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
      _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
      _originalIndex = Math.Max(0, originalIndex);
    }

    public void Undo()
    {
      if (!_datasets.Any(d => d.Id == _dataset.Id))
      {
        var insertAt = Math.Min(_originalIndex, _datasets.Count);
        _datasets.Insert(insertAt, _dataset);
      }
    }

    public void Redo()
    {
      var datasetToRemove = _datasets.FirstOrDefault(d => d.Id == _dataset.Id);
      if (datasetToRemove != null)
      {
        _datasets.Remove(datasetToRemove);
      }
    }
  }
}