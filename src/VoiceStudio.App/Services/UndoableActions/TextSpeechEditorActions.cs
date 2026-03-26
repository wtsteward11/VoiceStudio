using System;
using System.Collections.ObjectModel;
using System.Linq;
using VoiceStudio.Core.Services;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Services.UndoableActions
{
  /// <summary>
  /// Undoable action for creating a text speech editor session.
  /// </summary>
  public class CreateTextSpeechSessionAction : IUndoableAction
  {
    private readonly ObservableCollection<EditorSessionItem> _sessions;
    private readonly ITextSpeechEditorClient _client;
    private readonly EditorSessionItem _session;
    private readonly Action<EditorSessionItem>? _onUndo;
    private readonly Action<EditorSessionItem>? _onRedo;

    public string ActionName => $"Create Session '{_session.Title}'";

    public CreateTextSpeechSessionAction(
        ObservableCollection<EditorSessionItem> sessions,
        ITextSpeechEditorClient client,
        EditorSessionItem session,
        Action<EditorSessionItem>? onUndo = null,
        Action<EditorSessionItem>? onRedo = null)
    {
      _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
      _client = client ?? throw new ArgumentNullException(nameof(client));
      _session = session ?? throw new ArgumentNullException(nameof(session));
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      var sessionToRemove = _sessions.FirstOrDefault(s => s.SessionId == _session.SessionId);
      if (sessionToRemove != null)
      {
        _sessions.Remove(sessionToRemove);
        _onUndo?.Invoke(sessionToRemove);
      }
    }

    public void Redo()
    {
      if (!_sessions.Any(s => s.SessionId == _session.SessionId))
      {
        _sessions.Add(_session);
        _onRedo?.Invoke(_session);
      }
    }
  }

  /// <summary>
  /// Undoable action for deleting a text speech editor session.
  /// </summary>
  public class DeleteTextSpeechSessionAction : IUndoableAction
  {
    private readonly ObservableCollection<EditorSessionItem> _sessions;
    private readonly ITextSpeechEditorClient _client;
    private readonly EditorSessionItem _session;
    private readonly int _originalIndex;
    private readonly Action<EditorSessionItem>? _onUndo;
    private readonly Action<EditorSessionItem>? _onRedo;

    public string ActionName => $"Delete Session '{_session.Title}'";

    public DeleteTextSpeechSessionAction(
        ObservableCollection<EditorSessionItem> sessions,
        ITextSpeechEditorClient client,
        EditorSessionItem session,
        int originalIndex,
        Action<EditorSessionItem>? onUndo = null,
        Action<EditorSessionItem>? onRedo = null)
    {
      _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
      _client = client ?? throw new ArgumentNullException(nameof(client));
      _session = session ?? throw new ArgumentNullException(nameof(session));
      _originalIndex = originalIndex;
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      if (!_sessions.Any(s => s.SessionId == _session.SessionId))
      {
        if (_originalIndex >= 0 && _originalIndex <= _sessions.Count)
        {
          _sessions.Insert(_originalIndex, _session);
        }
        else
        {
          _sessions.Add(_session);
        }
        _onUndo?.Invoke(_session);
      }
    }

    public void Redo()
    {
      var sessionToRemove = _sessions.FirstOrDefault(s => s.SessionId == _session.SessionId);
      if (sessionToRemove != null)
      {
        _sessions.Remove(sessionToRemove);
        _onRedo?.Invoke(sessionToRemove);
      }
    }
  }

  /// <summary>
  /// Undoable action for adding a text segment to a session.
  /// </summary>
  public class AddTextSegmentAction : IUndoableAction
  {
    private readonly ObservableCollection<TextSegmentItem> _segments;
    private readonly EditorSessionItem? _session;
    private readonly TextSegmentItem _segment;
    private readonly Action<TextSegmentItem>? _onUndo;
    private readonly Action<TextSegmentItem>? _onRedo;

    public string ActionName => "Add Text Segment";

    public AddTextSegmentAction(
        ObservableCollection<TextSegmentItem> segments,
        EditorSessionItem? session,
        TextSegmentItem segment,
        Action<TextSegmentItem>? onUndo = null,
        Action<TextSegmentItem>? onRedo = null)
    {
      _segments = segments ?? throw new ArgumentNullException(nameof(segments));
      _session = session;
      _segment = segment ?? throw new ArgumentNullException(nameof(segment));
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      if (_segments.Contains(_segment))
      {
        _segments.Remove(_segment);
        _session?.Segments.Remove(_segment);
        _onUndo?.Invoke(_segment);
      }
    }

    public void Redo()
    {
      if (!_segments.Any(s => s.Id == _segment.Id))
      {
        _segments.Add(_segment);
        if (_session?.Segments.Any(s => s.Id == _segment.Id) == false)
        {
          _session.Segments.Add(_segment);
        }
        _onRedo?.Invoke(_segment);
      }
    }
  }

  /// <summary>
  /// Undoable action for removing a text segment from a session.
  /// </summary>
  public class RemoveTextSegmentAction : IUndoableAction
  {
    private readonly ObservableCollection<TextSegmentItem> _segments;
    private readonly EditorSessionItem? _session;
    private readonly TextSegmentItem _segment;
    private readonly int _originalIndex;
    private readonly Action<TextSegmentItem>? _onUndo;
    private readonly Action<TextSegmentItem>? _onRedo;

    public string ActionName => "Remove Text Segment";

    public RemoveTextSegmentAction(
        ObservableCollection<TextSegmentItem> segments,
        EditorSessionItem? session,
        TextSegmentItem segment,
        int originalIndex,
        Action<TextSegmentItem>? onUndo = null,
        Action<TextSegmentItem>? onRedo = null)
    {
      _segments = segments ?? throw new ArgumentNullException(nameof(segments));
      _session = session;
      _segment = segment ?? throw new ArgumentNullException(nameof(segment));
      _originalIndex = originalIndex;
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      if (!_segments.Any(s => s.Id == _segment.Id))
      {
        if (_originalIndex >= 0 && _originalIndex <= _segments.Count)
        {
          _segments.Insert(_originalIndex, _segment);
        }
        else
        {
          _segments.Add(_segment);
        }
        if (_session?.Segments.Any(s => s.Id == _segment.Id) == false)
        {
          _session.Segments.Add(_segment);
        }
        _onUndo?.Invoke(_segment);
      }
    }

    public void Redo()
    {
      var segmentToRemove = _segments.FirstOrDefault(s => s.Id == _segment.Id);
      if (segmentToRemove != null)
      {
        _segments.Remove(segmentToRemove);
        _session?.Segments.Remove(segmentToRemove);
        _onRedo?.Invoke(segmentToRemove);
      }
    }
  }
}