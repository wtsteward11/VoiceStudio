using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services
{
  [TestClass]
  public class TimelineTrackServiceTests
  {
    [TestMethod]
    public async Task GetTracksAsync_ReturnsTracksOrderedByTrackNumberThenName()
    {
      var mockBackend = new Mock<IBackendClient>();
      var unordered = new List<AudioTrack>
      {
        new() { Id = "t2", Name = "Track 2", ProjectId = "p1", TrackNumber = 2 },
        new() { Id = "t1", Name = "Track 1", ProjectId = "p1", TrackNumber = 1 },
        new() { Id = "t3", Name = "Track 3", ProjectId = "p1", TrackNumber = 3 }
      };
      mockBackend.Setup(x => x.GetTracksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(unordered);

      var sut = new TimelineTrackService(mockBackend.Object);
      var result = await sut.GetTracksAsync("p1").ConfigureAwait(false);

      Assert.AreEqual(3, result.Count);
      Assert.AreEqual(1, result[0].TrackNumber);
      Assert.AreEqual(2, result[1].TrackNumber);
      Assert.AreEqual(3, result[2].TrackNumber);
    }

    [TestMethod]
    public async Task CreateTrackAsync_WhenNameIsNull_GeneratesDefaultName()
    {
      var mockBackend = new Mock<IBackendClient>();
      mockBackend.Setup(x => x.GetTracksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<AudioTrack>());
      mockBackend.Setup(x => x.CreateTrackAsync(It.IsAny<string>(), "Track 1", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new AudioTrack { Id = "t1", Name = "Track 1", ProjectId = "p1", TrackNumber = 1 });

      var sut = new TimelineTrackService(mockBackend.Object);
      var result = await sut.CreateTrackAsync("p1", null).ConfigureAwait(false);

      mockBackend.Verify(x => x.CreateTrackAsync("p1", "Track 1", null, It.IsAny<CancellationToken>()), Times.Once);
      Assert.AreEqual("Track 1", result.Name);
    }

    [TestMethod]
    public async Task CreateTrackAsync_WhenNameIsWhitespace_GeneratesDefaultName()
    {
      var mockBackend = new Mock<IBackendClient>();
      mockBackend.Setup(x => x.GetTracksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<AudioTrack>());
      mockBackend.Setup(x => x.CreateTrackAsync(It.IsAny<string>(), "Track 1", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new AudioTrack { Id = "t1", Name = "Track 1", ProjectId = "p1", TrackNumber = 1 });

      var sut = new TimelineTrackService(mockBackend.Object);
      _ = await sut.CreateTrackAsync("p1", "   ").ConfigureAwait(false);

      mockBackend.Verify(x => x.CreateTrackAsync("p1", "Track 1", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CreateTrackAsync_WhenNameProvided_UsesProvidedName()
    {
      var mockBackend = new Mock<IBackendClient>();
      mockBackend.Setup(x => x.CreateTrackAsync(It.IsAny<string>(), "My Track", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new AudioTrack { Id = "t1", Name = "My Track", ProjectId = "p1", TrackNumber = 1 });

      var sut = new TimelineTrackService(mockBackend.Object);
      var result = await sut.CreateTrackAsync("p1", "My Track").ConfigureAwait(false);

      mockBackend.Verify(x => x.CreateTrackAsync("p1", "My Track", null, It.IsAny<CancellationToken>()), Times.Once);
      mockBackend.Verify(x => x.GetTracksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
      Assert.AreEqual("My Track", result.Name);
    }
  }
}
