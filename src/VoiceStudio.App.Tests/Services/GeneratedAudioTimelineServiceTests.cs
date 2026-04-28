using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services
{
  [TestClass]
  public sealed class GeneratedAudioTimelineServiceTests
  {
    private Mock<IContextManager> _ctx = null!;
    private Mock<ITimelineTrackService> _tracks = null!;
    private Mock<ITimelineClipService> _clips = null!;

    [TestInitialize]
    public void Setup()
    {
      _ctx = new Mock<IContextManager>();
      _tracks = new Mock<ITimelineTrackService>();
      _clips = new Mock<ITimelineClipService>();
    }

    private GeneratedAudioTimelineService CreateSut() =>
        new(_ctx.Object, _tracks.Object, _clips.Object);

    private static GeneratedAudioTimelineRequest Request(string audioId) =>
        new(
            audioId,
            "/api/audio/test",
            TimeSpan.FromSeconds(2),
            "prof-1",
            "Prof",
            "piper",
            DateTime.Now,
            0.88,
            "lib-asset-1",
            "Hello");

    [TestMethod]
    public async Task NoActiveProject_ReturnsUnavailable()
    {
      _ctx.Setup(c => c.ActiveProjectId).Returns((string?)null);

      var sut = CreateSut();
      var r = await sut.AddGeneratedClipAsync(Request("a1")).ConfigureAwait(false);

      Assert.IsFalse(r.Success);
      Assert.AreEqual(GeneratedAudioTimelineKind.Unavailable, r.Kind);
      StringAssert.Contains(r.Message!, "project");

      _tracks.Verify(t => t.GetTracksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task NoProfileId_ReturnsUnavailable()
    {
      _ctx.Setup(c => c.ActiveProjectId).Returns("proj-1");

      var req = new GeneratedAudioTimelineRequest(
          "a1",
          "/x",
          TimeSpan.FromSeconds(1),
          "",
          null,
          "e",
          DateTime.Now,
          null,
          null,
          null);

      var sut = CreateSut();
      var r = await sut.AddGeneratedClipAsync(req).ConfigureAwait(false);

      Assert.IsFalse(r.Success);
      Assert.AreEqual(GeneratedAudioTimelineKind.Unavailable, r.Kind);
      _tracks.Verify(t => t.GetTracksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task NoTracks_ReturnsUnavailable()
    {
      _ctx.Setup(c => c.ActiveProjectId).Returns("proj-1");
      _ctx.Setup(c => c.ActiveTimelinePrimaryTrackId).Returns((string?)null);
      _tracks.Setup(t => t.GetTracksAsync("proj-1", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<AudioTrack>());

      var sut = CreateSut();
      var r = await sut.AddGeneratedClipAsync(Request("a1")).ConfigureAwait(false);

      Assert.IsFalse(r.Success);
      Assert.AreEqual(GeneratedAudioTimelineKind.Unavailable, r.Kind);
      _clips.Verify(
          c => c.CreateClipAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AudioClip>(), It.IsAny<CancellationToken>()),
          Times.Never);
    }

    [TestMethod]
    public async Task Success_CreatesClip_WithMetadataAndDerivedFromLibraryAsset()
    {
      _ctx.Setup(c => c.ActiveProjectId).Returns("proj-1");
      _ctx.Setup(c => c.ActiveTimelinePrimaryTrackId).Returns((string?)null);

      var track = new AudioTrack
      {
        Id = "tr-1",
        Name = "T1",
        ProjectId = "proj-1",
        TrackNumber = 1,
        Clips = new List<AudioClip>
        {
          new()
          {
            Id = "c0",
            StartTime = 0,
            Duration = TimeSpan.FromSeconds(3),
            ProfileId = "x",
            AudioId = "old",
          },
        },
      };

      _tracks.Setup(t => t.GetTracksAsync("proj-1", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<AudioTrack> { track });

      AudioClip? captured = null;
      _clips
          .Setup(c => c.CreateClipAsync("proj-1", "tr-1", It.IsAny<AudioClip>(), It.IsAny<CancellationToken>()))
          .Callback<string, string, AudioClip, CancellationToken>((_, _, clip, _) => captured = clip)
          .ReturnsAsync((string _, string _, AudioClip clip, CancellationToken _) =>
          {
            clip.Id = "persisted-clip";
            return clip;
          });

      var sut = CreateSut();
      var r = await sut.AddGeneratedClipAsync(Request("syn-a1")).ConfigureAwait(false);

      Assert.IsTrue(r.Success);
      Assert.AreEqual(GeneratedAudioTimelineKind.ExactAppend, r.Kind);
      Assert.AreEqual("proj-1", r.ProjectId);
      Assert.AreEqual("tr-1", r.TrackId);
      Assert.AreEqual("persisted-clip", r.ClipId);
      Assert.IsTrue(r.PlacementStartSeconds.HasValue);
      Assert.AreEqual(3.0, r.PlacementStartSeconds!.Value, 0.001);

      Assert.IsNotNull(captured);
      Assert.AreEqual("syn-a1", captured!.AudioId);
      Assert.AreEqual("/api/audio/test", captured.AudioUrl);
      Assert.AreEqual("prof-1", captured.ProfileId);
      Assert.AreEqual("piper", captured.Engine);
      Assert.AreEqual(0.88, captured.QualityScore);
      Assert.AreEqual("lib-asset-1", captured.DerivedFromClipId);
      Assert.AreEqual(3.0, captured.StartTime, 0.001);
    }

    [TestMethod]
    public async Task CreateClipThrows_ReturnsFailed()
    {
      _ctx.Setup(c => c.ActiveProjectId).Returns("proj-1");
      _tracks.Setup(t => t.GetTracksAsync("proj-1", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<AudioTrack>
          {
            new()
            {
              Id = "tr-1",
              Name = "T1",
              Clips = new List<AudioClip>(),
            },
          });

      _clips
          .Setup(c => c.CreateClipAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AudioClip>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("backend"));

      var sut = CreateSut();
      var r = await sut.AddGeneratedClipAsync(Request("a1")).ConfigureAwait(false);

      Assert.IsFalse(r.Success);
      Assert.AreEqual(GeneratedAudioTimelineKind.Failed, r.Kind);
      StringAssert.Contains(r.Message!, "backend");
    }

    [TestMethod]
    public async Task EmptyTrack_InsertsAtZero_DefaultAtZeroKind()
    {
      _ctx.Setup(c => c.ActiveProjectId).Returns("proj-1");
      _ctx.Setup(c => c.ActiveTimelinePrimaryTrackId).Returns((string?)null);

      var track = new AudioTrack
      {
        Id = "tr-1",
        Name = "T1",
        ProjectId = "proj-1",
        TrackNumber = 1,
        Clips = new List<AudioClip>(),
      };

      _tracks.Setup(t => t.GetTracksAsync("proj-1", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<AudioTrack> { track });

      AudioClip? captured = null;
      _clips
          .Setup(c => c.CreateClipAsync("proj-1", "tr-1", It.IsAny<AudioClip>(), It.IsAny<CancellationToken>()))
          .Callback<string, string, AudioClip, CancellationToken>((_, _, clip, _) => captured = clip)
          .ReturnsAsync((string _, string _, AudioClip clip, CancellationToken _) =>
          {
            clip.Id = "new-clip";
            return clip;
          });

      var sut = CreateSut();
      var r = await sut.AddGeneratedClipAsync(Request("syn-empty")).ConfigureAwait(false);

      Assert.IsTrue(r.Success);
      Assert.AreEqual(GeneratedAudioTimelineKind.DefaultAtZeroBecauseTrackEmpty, r.Kind);
      Assert.IsTrue(r.PlacementStartSeconds.HasValue);
      Assert.AreEqual(0.0, r.PlacementStartSeconds!.Value, 0.001);
      Assert.IsNotNull(captured);
      Assert.AreEqual(0.0, captured!.StartTime, 0.001);
    }

    [TestMethod]
    public async Task ClipsNull_ReturnsPlacementUnavailable_NoCreate()
    {
      _ctx.Setup(c => c.ActiveProjectId).Returns("proj-1");
      _ctx.Setup(c => c.ActiveTimelinePrimaryTrackId).Returns((string?)null);

      var track = new AudioTrack
      {
        Id = "tr-1",
        Name = "T1",
        ProjectId = "proj-1",
        TrackNumber = 1,
        Clips = null!, // SAFETY: simulates track payloads without hydrated clip list (service fail-closed path).
      };

      _tracks.Setup(t => t.GetTracksAsync("proj-1", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<AudioTrack> { track });

      var sut = CreateSut();
      var r = await sut.AddGeneratedClipAsync(Request("syn-null")).ConfigureAwait(false);

      Assert.IsFalse(r.Success);
      Assert.AreEqual(GeneratedAudioTimelineKind.PlacementUnavailable, r.Kind);
      Assert.IsFalse(string.IsNullOrWhiteSpace(r.Message));
      StringAssert.Contains(r.Message!, "clip");
      _clips.Verify(
          c => c.CreateClipAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AudioClip>(), It.IsAny<CancellationToken>()),
          Times.Never);
    }

    [TestMethod]
    public async Task AllInvalidClips_ReturnsPlacementUnavailable_NoCreate()
    {
      _ctx.Setup(c => c.ActiveProjectId).Returns("proj-1");
      _ctx.Setup(c => c.ActiveTimelinePrimaryTrackId).Returns((string?)null);

      var track = new AudioTrack
      {
        Id = "tr-1",
        Name = "T1",
        ProjectId = "proj-1",
        TrackNumber = 1,
        Clips = new List<AudioClip>
        {
          new()
          {
            Id = "bad",
            StartTime = 0,
            Duration = TimeSpan.Zero,
            ProfileId = "x",
            AudioId = "old",
          },
          new()
          {
            Id = "bad2",
            StartTime = -1,
            Duration = TimeSpan.FromSeconds(1),
            ProfileId = "x",
            AudioId = "old2",
          },
        },
      };

      _tracks.Setup(t => t.GetTracksAsync("proj-1", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<AudioTrack> { track });

      var sut = CreateSut();
      var r = await sut.AddGeneratedClipAsync(Request("syn-inv")).ConfigureAwait(false);

      Assert.IsFalse(r.Success);
      Assert.AreEqual(GeneratedAudioTimelineKind.PlacementUnavailable, r.Kind);
      _clips.Verify(
          c => c.CreateClipAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AudioClip>(), It.IsAny<CancellationToken>()),
          Times.Never);
    }

    [TestMethod]
    public async Task MixedValidAndInvalid_AppendsAtMaxValidEnd()
    {
      _ctx.Setup(c => c.ActiveProjectId).Returns("proj-1");
      _ctx.Setup(c => c.ActiveTimelinePrimaryTrackId).Returns((string?)null);

      var track = new AudioTrack
      {
        Id = "tr-1",
        Name = "T1",
        ProjectId = "proj-1",
        TrackNumber = 1,
        Clips = new List<AudioClip>
        {
          new()
          {
            Id = "bad",
            StartTime = 0,
            Duration = TimeSpan.Zero,
            ProfileId = "x",
            AudioId = "old",
          },
          new()
          {
            Id = "good",
            StartTime = 2,
            Duration = TimeSpan.FromSeconds(4),
            ProfileId = "x",
            AudioId = "old2",
          },
        },
      };

      _tracks.Setup(t => t.GetTracksAsync("proj-1", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<AudioTrack> { track });

      AudioClip? captured = null;
      _clips
          .Setup(c => c.CreateClipAsync("proj-1", "tr-1", It.IsAny<AudioClip>(), It.IsAny<CancellationToken>()))
          .Callback<string, string, AudioClip, CancellationToken>((_, _, clip, _) => captured = clip)
          .ReturnsAsync((string _, string _, AudioClip clip, CancellationToken _) =>
          {
            clip.Id = "c-new";
            return clip;
          });

      var sut = CreateSut();
      var r = await sut.AddGeneratedClipAsync(Request("syn-mix")).ConfigureAwait(false);

      Assert.IsTrue(r.Success);
      Assert.AreEqual(GeneratedAudioTimelineKind.ExactAppend, r.Kind);
      Assert.IsTrue(r.PlacementStartSeconds.HasValue);
      Assert.AreEqual(6.0, r.PlacementStartSeconds!.Value, 0.001);
      Assert.IsNotNull(captured);
      Assert.AreEqual(6.0, captured!.StartTime, 0.001);
    }
  }
}
