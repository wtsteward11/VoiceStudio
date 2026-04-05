using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class TranscriptionExportFormatterTests
{
  [TestMethod]
  public void BuildPlainText_UsesTranscriptionTextWhenPresent()
  {
    var transcription = new TranscriptionResponse
    {
      Id = "tr1",
      Text = "hello world",
      Segments = new List<TranscriptionSegment>
      {
        new() { Id = "s1", Start = 0, End = 1, Text = "ignored" },
      },
    };

    var text = TranscriptionExportFormatter.BuildPlainText(transcription);

    Assert.AreEqual("hello world", text);
  }

  [TestMethod]
  public void BuildPlainText_FallsBackToSegmentLines()
  {
    var transcription = new TranscriptionResponse
    {
      Id = "tr1",
      Segments = new List<TranscriptionSegment>
      {
        new() { Id = "s1", Start = 0, End = 1.5, Text = "first line" },
        new() { Id = "s2", Start = 1.5, End = 3, Text = "second line" },
      },
    };

    var text = TranscriptionExportFormatter.BuildPlainText(transcription);

    var expected = "first line" + System.Environment.NewLine + "second line";
    Assert.AreEqual(expected, text);
  }

  [TestMethod]
  public void BuildSrt_FormatsSegmentsWithTimestampsAndSequenceNumbers()
  {
    var transcription = new TranscriptionResponse
    {
      Id = "tr1",
      Segments = new List<TranscriptionSegment>
      {
        new() { Id = "s1", Start = 0, End = 1.234, Text = "hello" },
        new() { Id = "s2", Start = 1.5, End = 3, Text = "world" },
      },
    };

    var srt = TranscriptionExportFormatter.BuildSrt(transcription);

    StringAssert.Contains(srt, "1");
    StringAssert.Contains(srt, "00:00:00,000 --> 00:00:01,234");
    StringAssert.Contains(srt, "hello");
    StringAssert.Contains(srt, "2");
    StringAssert.Contains(srt, "00:00:01,500 --> 00:00:03,000");
    StringAssert.Contains(srt, "world");
  }
}
