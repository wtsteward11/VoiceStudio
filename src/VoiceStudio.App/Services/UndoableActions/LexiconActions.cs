using System;
using System.Collections.ObjectModel;
using System.Linq;
using VoiceStudio.Core.Services;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Services.UndoableActions
{
  /// <summary>
  /// Undoable action for creating a lexicon.
  /// </summary>
  public class CreateLexiconAction : IUndoableAction
  {
    private readonly ObservableCollection<LexiconItem> _lexicons;
    private readonly ILexiconClient _lexiconClient;
    private readonly LexiconItem _lexicon;
    private readonly Action<LexiconItem>? _onUndo;
    private readonly Action<LexiconItem>? _onRedo;

    public string ActionName => $"Create Lexicon '{_lexicon.Name ?? "Unnamed"}'";

    public CreateLexiconAction(
        ObservableCollection<LexiconItem> lexicons,
        ILexiconClient lexiconClient,
        LexiconItem lexicon,
        Action<LexiconItem>? onUndo = null,
        Action<LexiconItem>? onRedo = null)
    {
      _lexicons = lexicons ?? throw new ArgumentNullException(nameof(lexicons));
      _lexiconClient = lexiconClient ?? throw new ArgumentNullException(nameof(lexiconClient));
      _lexicon = lexicon ?? throw new ArgumentNullException(nameof(lexicon));
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      var lexiconToRemove = _lexicons.FirstOrDefault(l => l.LexiconId == _lexicon.LexiconId);
      if (lexiconToRemove != null)
      {
        _lexicons.Remove(lexiconToRemove);
        _onUndo?.Invoke(lexiconToRemove);
      }
    }

    public void Redo()
    {
      if (!_lexicons.Any(l => l.LexiconId == _lexicon.LexiconId))
      {
        _lexicons.Add(_lexicon);
        _onRedo?.Invoke(_lexicon);
      }
    }
  }

  /// <summary>
  /// Undoable action for deleting a lexicon.
  /// </summary>
  public class DeleteLexiconAction : IUndoableAction
  {
    private readonly ObservableCollection<LexiconItem> _lexicons;
    private readonly ILexiconClient _lexiconClient;
    private readonly LexiconItem _lexicon;
    private readonly int _originalIndex;
    private readonly Action<LexiconItem>? _onUndo;
    private readonly Action<LexiconItem>? _onRedo;

    public string ActionName => $"Delete Lexicon '{_lexicon.Name ?? "Unnamed"}'";

    public DeleteLexiconAction(
        ObservableCollection<LexiconItem> lexicons,
        ILexiconClient lexiconClient,
        LexiconItem lexicon,
        int originalIndex,
        Action<LexiconItem>? onUndo = null,
        Action<LexiconItem>? onRedo = null)
    {
      _lexicons = lexicons ?? throw new ArgumentNullException(nameof(lexicons));
      _lexiconClient = lexiconClient ?? throw new ArgumentNullException(nameof(lexiconClient));
      _lexicon = lexicon ?? throw new ArgumentNullException(nameof(lexicon));
      _originalIndex = originalIndex;
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      if (!_lexicons.Any(l => l.LexiconId == _lexicon.LexiconId))
      {
        if (_originalIndex >= 0 && _originalIndex <= _lexicons.Count)
        {
          _lexicons.Insert(_originalIndex, _lexicon);
        }
        else
        {
          _lexicons.Add(_lexicon);
        }
        _onUndo?.Invoke(_lexicon);
      }
    }

    public void Redo()
    {
      var lexiconToRemove = _lexicons.FirstOrDefault(l => l.LexiconId == _lexicon.LexiconId);
      if (lexiconToRemove != null)
      {
        _lexicons.Remove(lexiconToRemove);
        _onRedo?.Invoke(lexiconToRemove);
      }
    }
  }

  /// <summary>
  /// Undoable action for creating a lexicon entry.
  /// </summary>
  public class CreateLexiconEntryAction : IUndoableAction
  {
    private readonly ObservableCollection<LexiconEntryItem> _entries;
    private readonly ILexiconClient _lexiconClient;
    private readonly string _lexiconId;
    private readonly LexiconEntryItem _entry;
    private readonly Action<LexiconEntryItem>? _onUndo;
    private readonly Action<LexiconEntryItem>? _onRedo;

    public string ActionName => $"Create Entry '{_entry.Word ?? "Unnamed"}'";

    public CreateLexiconEntryAction(
        ObservableCollection<LexiconEntryItem> entries,
        ILexiconClient lexiconClient,
        string lexiconId,
        LexiconEntryItem entry,
        Action<LexiconEntryItem>? onUndo = null,
        Action<LexiconEntryItem>? onRedo = null)
    {
      _entries = entries ?? throw new ArgumentNullException(nameof(entries));
      _lexiconClient = lexiconClient ?? throw new ArgumentNullException(nameof(lexiconClient));
      _lexiconId = lexiconId ?? throw new ArgumentNullException(nameof(lexiconId));
      _entry = entry ?? throw new ArgumentNullException(nameof(entry));
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      var entryToRemove = _entries.FirstOrDefault(e => e.Word == _entry.Word);
      if (entryToRemove != null)
      {
        _entries.Remove(entryToRemove);
        _onUndo?.Invoke(entryToRemove);
      }
    }

    public void Redo()
    {
      if (!_entries.Any(e => e.Word == _entry.Word))
      {
        _entries.Add(_entry);
        _onRedo?.Invoke(_entry);
      }
    }
  }

  /// <summary>
  /// Undoable action for deleting a lexicon entry.
  /// </summary>
  public class DeleteLexiconEntryAction : IUndoableAction
  {
    private readonly ObservableCollection<LexiconEntryItem> _entries;
    private readonly ILexiconClient _lexiconClient;
    private readonly string _lexiconId;
    private readonly LexiconEntryItem _entry;
    private readonly int _originalIndex;
    private readonly Action<LexiconEntryItem>? _onUndo;
    private readonly Action<LexiconEntryItem>? _onRedo;

    public string ActionName => $"Delete Entry '{_entry.Word ?? "Unnamed"}'";

    public DeleteLexiconEntryAction(
        ObservableCollection<LexiconEntryItem> entries,
        ILexiconClient lexiconClient,
        string lexiconId,
        LexiconEntryItem entry,
        int originalIndex,
        Action<LexiconEntryItem>? onUndo = null,
        Action<LexiconEntryItem>? onRedo = null)
    {
      _entries = entries ?? throw new ArgumentNullException(nameof(entries));
      _lexiconClient = lexiconClient ?? throw new ArgumentNullException(nameof(lexiconClient));
      _lexiconId = lexiconId ?? throw new ArgumentNullException(nameof(lexiconId));
      _entry = entry ?? throw new ArgumentNullException(nameof(entry));
      _originalIndex = originalIndex;
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      if (!_entries.Any(e => e.Word == _entry.Word))
      {
        if (_originalIndex >= 0 && _originalIndex <= _entries.Count)
        {
          _entries.Insert(_originalIndex, _entry);
        }
        else
        {
          _entries.Add(_entry);
        }
        _onUndo?.Invoke(_entry);
      }
    }

    public void Redo()
    {
      var entryToRemove = _entries.FirstOrDefault(e => e.Word == _entry.Word);
      if (entryToRemove != null)
      {
        _entries.Remove(entryToRemove);
        _onRedo?.Invoke(entryToRemove);
      }
    }
  }
}