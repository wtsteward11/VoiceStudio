using System;
using System.Collections.ObjectModel;
using System.Linq;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services.UndoableActions
{
  /// <summary>
  /// Undoable action for deleting a transcription.
  /// </summary>
  public class DeleteTranscriptionAction : IUndoableAction
  {
    private readonly ObservableCollection<TranscriptionResponse> _transcriptions;
    private readonly ITranscriptionClient _transcriptionClient;
    private readonly TranscriptionResponse _transcription;
    private readonly int _originalIndex;
    private readonly Action<TranscriptionResponse>? _onUndo;
    private readonly Action<TranscriptionResponse>? _onRedo;

    public string ActionName => "Delete Transcription";

    public DeleteTranscriptionAction(
        ObservableCollection<TranscriptionResponse> transcriptions,
        ITranscriptionClient transcriptionClient,
        TranscriptionResponse transcription,
        int originalIndex,
        Action<TranscriptionResponse>? onUndo = null,
        Action<TranscriptionResponse>? onRedo = null)
    {
      _transcriptions = transcriptions ?? throw new ArgumentNullException(nameof(transcriptions));
      _transcriptionClient = transcriptionClient ?? throw new ArgumentNullException(nameof(transcriptionClient));
      _transcription = transcription ?? throw new ArgumentNullException(nameof(transcription));
      _originalIndex = originalIndex;
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      // Check if transcription already exists
      if (!_transcriptions.Any(t => t.Id == _transcription.Id))
      {
        if (_originalIndex >= 0 && _originalIndex <= _transcriptions.Count)
        {
          _transcriptions.Insert(_originalIndex, _transcription);
        }
        else
        {
          // Insert at beginning (transcriptions are typically sorted by Created desc)
          _transcriptions.Insert(0, _transcription);
        }
        _onUndo?.Invoke(_transcription);
      }
      // Note: Backend synchronization is handled separately. Undo/redo operations
      // work on the UI collection for immediate feedback. The backend state is
      // synchronized when the user commits changes or saves the project.
    }

    public void Redo()
    {
      var transcriptionToRemove = _transcriptions.FirstOrDefault(t => t.Id == _transcription.Id);
      if (transcriptionToRemove != null)
      {
        _transcriptions.Remove(transcriptionToRemove);
        _onRedo?.Invoke(transcriptionToRemove);
      }
      // Note: Backend synchronization is handled separately. Undo/redo operations
      // work on the UI collection for immediate feedback. The backend state is
      // synchronized when the user commits changes or saves the project.
    }
  }
}