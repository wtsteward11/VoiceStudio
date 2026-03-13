using System;
using System.Collections.ObjectModel;
using System.Linq;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Services.UndoableActions
{
  /// <summary>
  /// Undoable action for creating an SSML document. Undo/Redo are local-only (collection mutation).
  /// </summary>
  public class CreateSSMLDocumentAction : IUndoableAction
  {
    private readonly ObservableCollection<SSMLDocumentItem> _documents;
    private readonly SSMLDocumentItem _document;
    private readonly Action<SSMLDocumentItem>? _onUndo;
    private readonly Action<SSMLDocumentItem>? _onRedo;

    public string ActionName => $"Create SSML Document '{_document.Name}'";

    public CreateSSMLDocumentAction(
        ObservableCollection<SSMLDocumentItem> documents,
        SSMLDocumentItem document,
        Action<SSMLDocumentItem>? onUndo = null,
        Action<SSMLDocumentItem>? onRedo = null)
    {
      _documents = documents ?? throw new ArgumentNullException(nameof(documents));
      _document = document ?? throw new ArgumentNullException(nameof(document));
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      var documentToRemove = _documents.FirstOrDefault(d => d.Id == _document.Id);
      if (documentToRemove != null)
      {
        _documents.Remove(documentToRemove);
        _onUndo?.Invoke(documentToRemove);
      }
    }

    public void Redo()
    {
      if (!_documents.Any(d => d.Id == _document.Id))
      {
        _documents.Add(_document);
        _onRedo?.Invoke(_document);
      }
    }
  }

  /// <summary>
  /// Undoable action for deleting an SSML document. Undo/Redo are local-only (collection mutation).
  /// </summary>
  public class DeleteSSMLDocumentAction : IUndoableAction
  {
    private readonly ObservableCollection<SSMLDocumentItem> _documents;
    private readonly SSMLDocumentItem _document;
    private readonly int _originalIndex;
    private readonly Action<SSMLDocumentItem>? _onUndo;
    private readonly Action<SSMLDocumentItem>? _onRedo;

    public string ActionName => $"Delete SSML Document '{_document.Name}'";

    public DeleteSSMLDocumentAction(
        ObservableCollection<SSMLDocumentItem> documents,
        SSMLDocumentItem document,
        int originalIndex,
        Action<SSMLDocumentItem>? onUndo = null,
        Action<SSMLDocumentItem>? onRedo = null)
    {
      _documents = documents ?? throw new ArgumentNullException(nameof(documents));
      _document = document ?? throw new ArgumentNullException(nameof(document));
      _originalIndex = originalIndex;
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      if (!_documents.Any(d => d.Id == _document.Id))
      {
        if (_originalIndex >= 0 && _originalIndex <= _documents.Count)
        {
          _documents.Insert(_originalIndex, _document);
        }
        else
        {
          _documents.Add(_document);
        }
        _onUndo?.Invoke(_document);
      }
    }

    public void Redo()
    {
      var documentToRemove = _documents.FirstOrDefault(d => d.Id == _document.Id);
      if (documentToRemove != null)
      {
        _documents.Remove(documentToRemove);
        _onRedo?.Invoke(documentToRemove);
      }
    }
  }
}