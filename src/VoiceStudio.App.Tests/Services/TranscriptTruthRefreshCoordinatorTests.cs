using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Core.Models;
using VoiceStudio.App.Core.Services;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class TranscriptTruthRefreshCoordinatorTests
{
  private static Project BuildProject(string audioId, TranscriptTruthState truth)
  {
    var clip = new AudioClip
    {
      Id = "c1",
      AudioId = audioId,
      AudioUrl = "/a",
      Duration = TimeSpan.FromSeconds(3),
      StartTime = 0,
      TranscriptTruth = truth,
    };
    var track = new AudioTrack { Id = "t1", Name = "t", Clips = new List<AudioClip> { clip } };
    return new Project { Id = "p1", Name = "p", Tracks = new List<AudioTrack> { track } };
  }

  [TestMethod]
  public async Task TryRefresh_NotStale_FailClosed()
  {
    var project = BuildProject("au1", TranscriptTruthState.Current);
    var tx = new Mock<ITranscriptionClient>();
    var linkage = new Mock<IClipTranscriptLinkageService>();
    var sut = new TranscriptTruthRefreshCoordinator(tx.Object, linkage.Object, null, null, null);

    var err = await sut.TryRefreshStaleTranscriptForClipAsync(
        project,
        "t1",
        "c1",
        "whisper",
        "en",
        false,
        false,
        false,
        "panel",
        "p1",
        CancellationToken.None);

    Assert.AreEqual("This clip does not require transcript refresh (transcript is not marked stale).", err);
    tx.Verify(
        t => t.TranscribeAudioAsync(It.IsAny<TranscriptionRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
        Times.Never);
  }

  [TestMethod]
  public async Task TryRefresh_Success_RebuildsLinks_ClearsStale_PublishesCompleted()
  {
    var project = BuildProject("au1", TranscriptTruthState.StaleAfterClipRegeneration);
    var tx = new Mock<ITranscriptionClient>();
    tx.Setup(t => t.TranscribeAudioAsync(It.IsAny<TranscriptionRequest>(), "p1", It.IsAny<CancellationToken>()))
        .ReturnsAsync(
            new TranscriptionResponse
            {
              Id = "tx-new",
              AudioId = "au1",
              Text = "hello",
              Duration = 3,
              Language = "en",
              Segments = new List<TranscriptionSegment>
              {
                new() { Id = "s1", Start = 0, End = 1, Text = "h" },
                new() { Id = "s2", Start = 1, End = 2, Text = "i" },
              },
            });

    var linkage = new Mock<IClipTranscriptLinkageService>();
    var dirty = new Mock<IProjectSessionDirtyState>();
    var events = new Mock<IEventAggregator>();
    var sut = new TranscriptTruthRefreshCoordinator(tx.Object, linkage.Object, dirty.Object, events.Object, null);

    var err = await sut.TryRefreshStaleTranscriptForClipAsync(
        project,
        "t1",
        "c1",
        "whisper",
        "en",
        false,
        false,
        false,
        "transcribe-panel",
        "p1",
        CancellationToken.None);

    Assert.IsNull(err);
    linkage.Verify(l => l.RemoveLinksByClipId(project, "c1"), Times.Once);
    linkage.Verify(
        l => l.UpsertLinksForTranscription(
            project,
            "tx-new",
            "au1",
            It.Is<IReadOnlyList<TranscriptionSegmentLinkInput>>(
                inputs =>
                    inputs.Count == 2
                    && string.Equals(inputs[0].Id, "s1", StringComparison.Ordinal)
                    && string.Equals(inputs[1].Id, "s2", StringComparison.Ordinal))),
        Times.Once);
    Assert.AreEqual(TranscriptTruthState.Current, project.Tracks[0].Clips[0].TranscriptTruth);
    dirty.Verify(d => d.MarkProjectDirty("transcript_truth_refresh"), Times.Once);
    events.Verify(e => e.Publish(It.IsAny<TranscriptionCompletedEvent>()), Times.Once);
    events.Verify(e => e.Publish(It.IsAny<TranscriptTruthStateChangedEvent>()), Times.AtLeastOnce);
  }
}
