using System;
using System.Collections.Generic;
using System.Text;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Services;

/// <summary>
/// Formats transcription payloads for operator exports (TXT / SRT).
/// Callers must pass a <see cref="TranscriptionResponse"/> that matches backend authority for the active selection
/// (e.g. list rehydrate + selected row, or an explicit per-id read). The formatter does not fetch missing segments.
/// </summary>
public static class TranscriptionExportFormatter
{
  public static string BuildPlainText(TranscriptionResponse transcription)
  {
    if (transcription == null)
      throw new ArgumentNullException(nameof(transcription));

    if (!string.IsNullOrWhiteSpace(transcription.Text))
      return transcription.Text.Trim();

    var segments = transcription.Segments;
    if (segments == null || segments.Count == 0)
      return string.Empty;

    var lines = new List<string>(segments.Count);
    foreach (var segment in segments)
    {
      var text = (segment.Text ?? string.Empty).Trim();
      if (!string.IsNullOrWhiteSpace(text))
        lines.Add(text);
    }

    return string.Join(Environment.NewLine, lines);
  }

  public static string BuildSrt(TranscriptionResponse transcription)
  {
    if (transcription == null)
      throw new ArgumentNullException(nameof(transcription));

    var segments = transcription.Segments;
    if (segments == null || segments.Count == 0)
      return string.Empty;

    var builder = new StringBuilder(segments.Count * 48);
    var sequence = 1;
    foreach (var segment in segments)
    {
      var text = (segment.Text ?? string.Empty).Trim();
      if (string.IsNullOrWhiteSpace(text))
        continue;

      var startSeconds = Math.Max(0, segment.Start);
      var endSeconds = Math.Max(startSeconds, segment.End);

      builder.Append(sequence);
      builder.AppendLine();
      builder.Append(FormatSrtTimestamp(startSeconds));
      builder.Append(" --> ");
      builder.Append(FormatSrtTimestamp(endSeconds));
      builder.AppendLine();
      builder.AppendLine(text);
      builder.AppendLine();
      sequence++;
    }

    return builder.ToString().TrimEnd();
  }

  private static string FormatSrtTimestamp(double seconds)
  {
    var time = TimeSpan.FromSeconds(seconds);
    return $"{time.Hours:D2}:{time.Minutes:D2}:{time.Seconds:D2},{time.Milliseconds:D3}";
  }
}
