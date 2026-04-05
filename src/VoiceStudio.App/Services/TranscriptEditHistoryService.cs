using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// GAP-045 transcript edit history: kinds recorded in-session (no persistence).
  /// </summary>
  public enum TranscriptEditOperationKind
  {
    RegenerateSegment,
    SingleSegmentApply,
    MultiSegmentRangeApply,
    FillerCleanupDraft,
  }

  /// <summary>
  /// One session-visible transcript edit / apply / draft-assist action.
  /// </summary>
  public sealed class TranscriptEditHistoryEntry
  {
    public string EntryId { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public TranscriptEditOperationKind OperationKind { get; init; }
    public string? ProjectId { get; init; }
    public string? ClipId { get; init; }
    public string TranscriptionId { get; init; } = string.Empty;
    public IReadOnlyList<string> SegmentIds { get; init; } = Array.Empty<string>();
    public bool WasRegenerated { get; init; }
    public bool Succeeded { get; init; }
    public string MessageSummary { get; init; } = string.Empty;

    public string OperationKindLabel => OperationKind switch
    {
      TranscriptEditOperationKind.RegenerateSegment => "Regenerate",
      TranscriptEditOperationKind.SingleSegmentApply => "Apply (1 segment)",
      TranscriptEditOperationKind.MultiSegmentRangeApply => "Apply (range)",
      TranscriptEditOperationKind.FillerCleanupDraft => "Filler cleanup (draft)",
      _ => OperationKind.ToString(),
    };

    /// <summary>Compact row for Transcribe panel list.</summary>
    public string SummaryLine =>
        $"{(Succeeded ? "OK" : "FAIL")} · {OperationKindLabel} · {string.Join(',', SegmentIds)} · {MessageSummary}";
  }

  /// <summary>
  /// Session-local bounded history for transcript edit operations (newest-first).
  /// </summary>
  public sealed class TranscriptEditHistoryService
  {
    public const int DefaultMaxEntries = 20;

    public ObservableCollection<TranscriptEditHistoryEntry> Entries { get; } = new();

    private readonly int _maxEntries;

    public TranscriptEditHistoryService(int maxEntries = DefaultMaxEntries)
    {
      _maxEntries = maxEntries > 0 ? maxEntries : DefaultMaxEntries;
    }

    public void AddEntry(TranscriptEditHistoryEntry entry)
    {
      if (entry == null)
        throw new ArgumentNullException(nameof(entry));

      while (Entries.Count >= _maxEntries)
      {
        Entries.RemoveAt(Entries.Count - 1);
      }

      Entries.Insert(0, entry);
    }

    public void ClearSession()
    {
      Entries.Clear();
    }
  }
}
