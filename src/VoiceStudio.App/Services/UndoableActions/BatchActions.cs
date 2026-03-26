using System;
using System.Collections.ObjectModel;
using System.Linq;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services.UndoableActions
{
  /// <summary>
  /// Undoable action for creating a batch job.
  /// </summary>
  public class CreateBatchJobAction : IUndoableAction
  {
    private readonly ObservableCollection<BatchJob> _jobs;
    private readonly IBatchProcessingClient _batchClient;
    private readonly BatchJob _job;
    private readonly Action<BatchJob>? _onUndo;
    private readonly Action<BatchJob>? _onRedo;

    public string ActionName => $"Create Batch Job '{_job.Name ?? "Unnamed"}'";

    public CreateBatchJobAction(
        ObservableCollection<BatchJob> jobs,
        IBatchProcessingClient batchClient,
        BatchJob job,
        Action<BatchJob>? onUndo = null,
        Action<BatchJob>? onRedo = null)
    {
      _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
      _batchClient = batchClient ?? throw new ArgumentNullException(nameof(batchClient));
      _job = job ?? throw new ArgumentNullException(nameof(job));
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      var jobToRemove = _jobs.FirstOrDefault(j => j.Id == _job.Id);
      if (jobToRemove != null)
      {
        _jobs.Remove(jobToRemove);
        _onUndo?.Invoke(jobToRemove);
      }
    }

    public void Redo()
    {
      if (!_jobs.Any(j => j.Id == _job.Id))
      {
        _jobs.Insert(0, _job);
        _onRedo?.Invoke(_job);
      }
    }
  }

  /// <summary>
  /// Undoable action for deleting a batch job.
  /// </summary>
  public class DeleteBatchJobAction : IUndoableAction
  {
    private readonly ObservableCollection<BatchJob> _jobs;
    private readonly IBatchProcessingClient _batchClient;
    private readonly BatchJob _job;
    private readonly int _originalIndex;
    private readonly Action<BatchJob>? _onUndo;
    private readonly Action<BatchJob>? _onRedo;

    public string ActionName => $"Delete Batch Job '{_job.Name ?? "Unnamed"}'";

    public DeleteBatchJobAction(
        ObservableCollection<BatchJob> jobs,
        IBatchProcessingClient batchClient,
        BatchJob job,
        int originalIndex,
        Action<BatchJob>? onUndo = null,
        Action<BatchJob>? onRedo = null)
    {
      _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
      _batchClient = batchClient ?? throw new ArgumentNullException(nameof(batchClient));
      _job = job ?? throw new ArgumentNullException(nameof(job));
      _originalIndex = originalIndex;
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      if (!_jobs.Any(j => j.Id == _job.Id))
      {
        if (_originalIndex >= 0 && _originalIndex <= _jobs.Count)
        {
          _jobs.Insert(_originalIndex, _job);
        }
        else
        {
          _jobs.Insert(0, _job); // Jobs are sorted by Created desc, so insert at 0
        }
        _onUndo?.Invoke(_job);
      }
    }

    public void Redo()
    {
      var jobToRemove = _jobs.FirstOrDefault(j => j.Id == _job.Id);
      if (jobToRemove != null)
      {
        _jobs.Remove(jobToRemove);
        _onRedo?.Invoke(jobToRemove);
      }
    }
  }
}