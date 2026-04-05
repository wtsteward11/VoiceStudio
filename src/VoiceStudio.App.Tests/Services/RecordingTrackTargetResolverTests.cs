using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class RecordingTrackTargetResolverTests
{
    [TestMethod]
    public async Task Resolve_NoProject_Fails()
    {
        var mockTracks = new Mock<ITimelineTrackService>();
        var (ok, id, err) = await RecordingTrackTargetResolver.ResolveRecordableTrackAsync(
            null,
            null,
            mockTracks.Object,
            CancellationToken.None);
        Assert.IsFalse(ok);
        Assert.IsNull(id);
        StringAssert.Contains(err, "project");
        mockTracks.Verify(
            x => x.GetTracksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task Resolve_NoTrackService_Fails()
    {
        var (ok, id, err) = await RecordingTrackTargetResolver.ResolveRecordableTrackAsync(
            "p1",
            null,
            null,
            CancellationToken.None);
        Assert.IsFalse(ok);
        Assert.IsNull(id);
        StringAssert.Contains(err, "Timeline track service");
    }

    [TestMethod]
    public async Task Resolve_EmptyTrackList_Fails()
    {
        var mockTracks = new Mock<ITimelineTrackService>();
        mockTracks
            .Setup(x => x.GetTracksAsync("p1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AudioTrack>());
        var (ok, id, err) = await RecordingTrackTargetResolver.ResolveRecordableTrackAsync(
            "p1",
            null,
            mockTracks.Object,
            CancellationToken.None);
        Assert.IsFalse(ok);
        StringAssert.Contains(err, "No timeline tracks");
    }

    [TestMethod]
    public async Task Resolve_UsesPrimaryWhenPresentOnProject()
    {
        var mockTracks = new Mock<ITimelineTrackService>();
        mockTracks
            .Setup(x => x.GetTracksAsync("p1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AudioTrack>
            {
                new() { Id = "a", Name = "First", TrackNumber = 1 },
                new() { Id = "b", Name = "Second", TrackNumber = 2 }
            });
        var mockCtx = new Mock<IContextManager>();
        mockCtx.Setup(x => x.ActiveTimelinePrimaryTrackId).Returns("b");
        var (ok, id, err) = await RecordingTrackTargetResolver.ResolveRecordableTrackAsync(
            "p1",
            mockCtx.Object,
            mockTracks.Object,
            CancellationToken.None);
        Assert.IsTrue(ok, err);
        Assert.AreEqual("b", id);
    }

    [TestMethod]
    public async Task Resolve_FallsBackToFirstTrackWhenPrimaryMissing()
    {
        var mockTracks = new Mock<ITimelineTrackService>();
        mockTracks
            .Setup(x => x.GetTracksAsync("p1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AudioTrack>
            {
                new() { Id = "a", Name = "First", TrackNumber = 1 },
                new() { Id = "b", Name = "Second", TrackNumber = 2 }
            });
        var mockCtx = new Mock<IContextManager>();
        mockCtx.Setup(x => x.ActiveTimelinePrimaryTrackId).Returns("ghost");
        var (ok, id, err) = await RecordingTrackTargetResolver.ResolveRecordableTrackAsync(
            "p1",
            mockCtx.Object,
            mockTracks.Object,
            CancellationToken.None);
        Assert.IsTrue(ok, err);
        Assert.AreEqual("a", id);
    }
}
