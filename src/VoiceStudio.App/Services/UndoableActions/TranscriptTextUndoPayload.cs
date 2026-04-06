using System.Collections.Generic;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Services.UndoableActions;

/// <summary>
/// GAP-047: immutable snapshot of persisted transcript text + segments for coordinator/undo boundaries.
/// </summary>
public sealed class TranscriptTextUndoPayload
{
  public TranscriptTextUndoPayload(string text, IReadOnlyList<TranscriptionSegment> segments)
  {
    Text = text ?? string.Empty;
    Segments = CloneSegmentList(segments);
  }

  public string Text { get; }

  public List<TranscriptionSegment> Segments { get; }

  public static TranscriptTextUndoPayload FromTranscription(TranscriptionResponse transcription)
  {
    var segments = transcription.Segments ?? new List<TranscriptionSegment>();
    var text = string.IsNullOrWhiteSpace(transcription.Text)
        ? BuildTranscriptionText(segments)
        : transcription.Text!;
    return new TranscriptTextUndoPayload(text, segments);
  }

  public static List<TranscriptionSegment> CloneSegmentList(IReadOnlyList<TranscriptionSegment> source)
  {
    var list = new List<TranscriptionSegment>(source.Count);
    foreach (var s in source)
    {
      list.Add(new TranscriptionSegment
      {
        Id = s.Id,
        Start = s.Start,
        End = s.End,
        Text = s.Text,
        Words = s.Words,
      });
    }

    return list;
  }

  public static string BuildTranscriptionText(IReadOnlyList<TranscriptionSegment> segments)
  {
    var merged = new List<string>(segments.Count);
    foreach (var segment in segments)
    {
      var t = (segment.Text ?? string.Empty).Trim();
      if (!string.IsNullOrWhiteSpace(t))
        merged.Add(t);
    }

    return string.Join(" ", merged);
  }
}
