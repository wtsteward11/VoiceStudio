using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Collections.Generic;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services.UndoableActions
{
  /// <summary>
  /// Undoable action for adding a track to the timeline.
  /// </summary>
  public class AddTrackAction : IUndoableAction
  {
    private readonly ObservableCollection<AudioTrack> _tracks;
    private readonly ITimelineTrackService _trackService;
    private readonly AudioTrack _track;
    private readonly Action<AudioTrack>? _onUndo;
    private readonly Action<AudioTrack>? _onRedo;

    public string ActionName => $"Add Track '{_track.Name ?? "Unnamed"}'";

    public AddTrackAction(
        ObservableCollection<AudioTrack> tracks,
        ITimelineTrackService trackService,
        AudioTrack track,
        Action<AudioTrack>? onUndo = null,
        Action<AudioTrack>? onRedo = null)
    {
      _tracks = tracks ?? throw new ArgumentNullException(nameof(tracks));
      _trackService = trackService ?? throw new ArgumentNullException(nameof(trackService));
      _track = track ?? throw new ArgumentNullException(nameof(track));
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      // Remove the track from the collection
      var trackToRemove = _tracks.FirstOrDefault(t => t.Id == _track.Id);
      if (trackToRemove != null)
      {
        _tracks.Remove(trackToRemove);
        _onUndo?.Invoke(trackToRemove);
      }
    }

    public void Redo()
    {
      // Re-add the track to the collection if not already present
      if (!_tracks.Any(t => t.Id == _track.Id))
      {
        _tracks.Add(_track);
        _onRedo?.Invoke(_track);
      }
    }
  }

  /// <summary>
  /// Undoable action for adding a clip to a track.
  /// </summary>
  public class AddClipAction : IUndoableAction
  {
    private readonly ObservableCollection<AudioTrack> _tracks;
    private readonly AudioTrack _track;
    private readonly AudioClip _clip;
    private readonly int _originalIndex;
    private readonly Action<AudioClip>? _onUndo;
    private readonly Action<AudioClip>? _onRedo;

    public string ActionName => $"Add Clip '{_clip.Name ?? "Unnamed"}'";

    public AddClipAction(
        ObservableCollection<AudioTrack> tracks,
        AudioTrack track,
        AudioClip clip,
        Action<AudioClip>? onUndo = null,
        Action<AudioClip>? onRedo = null)
    {
      _tracks = tracks ?? throw new ArgumentNullException(nameof(tracks));
      _track = track ?? throw new ArgumentNullException(nameof(track));
      _clip = clip ?? throw new ArgumentNullException(nameof(clip));
      _originalIndex = track.Clips?.IndexOf(clip) ?? -1;
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      // Find the track and remove the clip
      var track = _tracks.FirstOrDefault(t => t.Id == _track.Id);
      if (track?.Clips != null)
      {
        var clipToRemove = track.Clips.FirstOrDefault(c => c.Id == _clip.Id);
        if (clipToRemove != null)
        {
          track.Clips.Remove(clipToRemove);
          _onUndo?.Invoke(clipToRemove);
        }
      }
    }

    public void Redo()
    {
      // Find the track and re-add the clip at its original position
      var track = _tracks.FirstOrDefault(t => t.Id == _track.Id);
      if (track?.Clips != null && !track.Clips.Any(c => c.Id == _clip.Id))
      {
        if (_originalIndex >= 0 && _originalIndex <= track.Clips.Count)
        {
          track.Clips.Insert(_originalIndex, _clip);
        }
        else
        {
          track.Clips.Add(_clip);
        }
        _onRedo?.Invoke(_clip);
      }
    }
  }

  /// <summary>
  /// Undoable action for deleting clips from tracks.
  /// </summary>
  public class DeleteClipsAction : IUndoableAction
  {
    private readonly ObservableCollection<AudioTrack> _tracks;
    private readonly List<(AudioTrack Track, AudioClip Clip, int Index)> _deletedClips;
    private readonly Action<IEnumerable<AudioClip>>? _onUndo;
    private readonly Action<IEnumerable<AudioClip>>? _onRedo;

    public string ActionName => $"Delete {_deletedClips.Count} Clip(s)";

    public DeleteClipsAction(
        ObservableCollection<AudioTrack> tracks,
        IEnumerable<(AudioTrack Track, AudioClip Clip)> clipsToDelete,
        Action<IEnumerable<AudioClip>>? onUndo = null,
        Action<IEnumerable<AudioClip>>? onRedo = null)
    {
      _tracks = tracks ?? throw new ArgumentNullException(nameof(tracks));
      _deletedClips = clipsToDelete?.Select(c => (
          c.Track,
          c.Clip,
          c.Track.Clips?.IndexOf(c.Clip) ?? -1
      )).ToList() ?? throw new ArgumentNullException(nameof(clipsToDelete));
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      // Re-add all clips at their original positions (in reverse order to maintain indices)
      var clipsToRestore = _deletedClips
          .GroupBy(c => c.Track.Id)
          .SelectMany(g => g.OrderByDescending(c => c.Index))
          .ToList();

      foreach (var (track, clip, originalIndex) in clipsToRestore)
      {
        var trackInCollection = _tracks.FirstOrDefault(t => t.Id == track.Id);
        if (trackInCollection?.Clips != null && !trackInCollection.Clips.Any(c => c.Id == clip.Id))
        {
          if (originalIndex >= 0 && originalIndex <= trackInCollection.Clips.Count)
          {
            trackInCollection.Clips.Insert(originalIndex, clip);
          }
          else
          {
            trackInCollection.Clips.Add(clip);
          }
        }
      }
      _onUndo?.Invoke(_deletedClips.Select(x => x.Clip));
    }

    public void Redo()
    {
      // Remove all clips from their tracks
      foreach (var (track, clip, _) in _deletedClips)
      {
        var trackInCollection = _tracks.FirstOrDefault(t => t.Id == track.Id);
        if (trackInCollection?.Clips != null)
        {
          var clipToRemove = trackInCollection.Clips.FirstOrDefault(c => c.Id == clip.Id);
          if (clipToRemove != null)
          {
            trackInCollection.Clips.Remove(clipToRemove);
          }
        }
      }
      _onRedo?.Invoke(_deletedClips.Select(x => x.Clip));
    }
  }
}