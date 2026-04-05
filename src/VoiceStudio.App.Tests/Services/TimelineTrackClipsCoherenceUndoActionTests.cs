using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.App.UseCases;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class TimelineTrackClipsCoherenceUndoActionTests
{
  [TestMethod]
  public void Undo_DeletesNewClipAndUpdatesOriginal_FromSplitLikeAfterState()
  {
    var backend = new Mock<IBackendClient>(MockBehavior.Strict);
    var useCase = new Mock<ITimelineUseCase>(MockBehavior.Strict);

    const string projectId = "p1";
    const string trackId = "t1";

    var beforeLeft = new AudioClip
    {
      Id = "clip-a",
      Name = "A",
      StartTime = 0,
      Duration = TimeSpan.FromSeconds(10),
      SourceStartSeconds = 0,
      FadeInSeconds = 0,
      FadeOutSeconds = 0,
      AudioId = "aud1",
      AudioUrl = "http://x/a.wav",
    };

    var afterLeft = new AudioClip
    {
      Id = "clip-a",
      Name = "A",
      StartTime = 0,
      Duration = TimeSpan.FromSeconds(4),
      SourceStartSeconds = 0,
      FadeInSeconds = 0,
      FadeOutSeconds = 0,
      AudioId = "aud1",
      AudioUrl = "http://x/a.wav",
    };

    var afterRight = new AudioClip
    {
      Id = "clip-b",
      Name = "A (2)",
      StartTime = 4,
      Duration = TimeSpan.FromSeconds(6),
      SourceStartSeconds = 4,
      FadeInSeconds = 0,
      FadeOutSeconds = 0,
      AudioId = "aud1",
      AudioUrl = "http://x/a.wav",
    };

    var track = new AudioTrack
    {
      Id = trackId,
      Clips = new List<AudioClip> { afterLeft, afterRight },
    };

    backend
        .Setup(b => b.DeleteClipAsync(projectId, trackId, "clip-b", It.IsAny<CancellationToken>()))
        .ReturnsAsync(true);

    backend
        .Setup(b => b.UpdateClipAsync(
            projectId,
            trackId,
            "clip-a",
            beforeLeft.Name,
            beforeLeft.StartTime,
            beforeLeft.AudioId,
            beforeLeft.AudioUrl,
            beforeLeft.Duration.TotalSeconds,
            beforeLeft.SourceStartSeconds,
            beforeLeft.FadeInSeconds,
            beforeLeft.FadeOutSeconds,
            null,
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(beforeLeft);

    useCase
        .Setup(u => u.ImportProjectTimelineAsync(projectId, It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

    var action = new TimelineTrackClipsCoherenceUndoAction(
        backend.Object,
        useCase.Object,
        sessionDirty: null,
        projectId,
        trackId,
        track,
        new[] { beforeLeft },
        new[] { afterLeft, afterRight },
        "Split clip at playhead");

    action.Undo();

    Assert.AreEqual(1, track.Clips.Count);
    Assert.AreEqual("clip-a", track.Clips[0].Id);
    Assert.AreEqual(10.0, track.Clips[0].Duration.TotalSeconds, 0.001);

    backend.Verify(
        b => b.DeleteClipAsync(projectId, trackId, "clip-b", It.IsAny<CancellationToken>()),
        Times.Once);
    backend.Verify(
        b => b.UpdateClipAsync(
            projectId,
            trackId,
            "clip-a",
            beforeLeft.Name,
            beforeLeft.StartTime,
            beforeLeft.AudioId,
            beforeLeft.AudioUrl,
            beforeLeft.Duration.TotalSeconds,
            beforeLeft.SourceStartSeconds,
            beforeLeft.FadeInSeconds,
            beforeLeft.FadeOutSeconds,
            null,
            It.IsAny<CancellationToken>()),
        Times.Once);
    useCase.Verify(u => u.ImportProjectTimelineAsync(projectId, It.IsAny<CancellationToken>()), Times.Once);
  }
}
