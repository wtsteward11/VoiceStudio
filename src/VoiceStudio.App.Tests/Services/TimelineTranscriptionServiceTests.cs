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
  public class TimelineTranscriptionServiceTests
  {
    [TestMethod]
    public async Task GetTranscriptionAsync_WhenBackendReturnsNull_ReturnsEmptyResponse()
    {
      var mockBackend = new Mock<IBackendClient>();
      mockBackend.Setup(x => x.GetTranscriptionAsync("tid-1", It.IsAny<CancellationToken>()))
          .ReturnsAsync((TranscriptionResponse?)null);

      var sut = new TimelineTranscriptionService(mockBackend.Object);
      var result = await sut.GetTranscriptionAsync("tid-1").ConfigureAwait(false);

      Assert.IsNotNull(result);
      Assert.IsNotNull(result.Segments);
      Assert.AreEqual(0, result.Segments.Count);
    }

    [TestMethod]
    public async Task GetTranscriptionAsync_WhenBackendReturnsNullSegments_NormalizesToEmptyList()
    {
      var mockBackend = new Mock<IBackendClient>();
      mockBackend.Setup(x => x.GetTranscriptionAsync("tid-1", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new TranscriptionResponse { Id = "tid-1", Text = "Hello", Segments = null! });

      var sut = new TimelineTranscriptionService(mockBackend.Object);
      var result = await sut.GetTranscriptionAsync("tid-1").ConfigureAwait(false);

      Assert.IsNotNull(result);
      Assert.IsNotNull(result.Segments);
      Assert.AreEqual(0, result.Segments.Count);
      Assert.AreEqual("tid-1", result.Id);
      Assert.AreEqual("Hello", result.Text);
    }

    [TestMethod]
    public async Task GetTranscriptionAsync_WhenBackendReturnsValidResponse_PassesThrough()
    {
      var segments = new List<TranscriptionSegment>
      {
        new() { Text = "Hello", Start = 0, End = 1 }
      };
      var mockBackend = new Mock<IBackendClient>();
      mockBackend.Setup(x => x.GetTranscriptionAsync("tid-1", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new TranscriptionResponse { Id = "tid-1", Text = "Hello", Segments = segments });

      var sut = new TimelineTranscriptionService(mockBackend.Object);
      var result = await sut.GetTranscriptionAsync("tid-1").ConfigureAwait(false);

      Assert.IsNotNull(result);
      Assert.IsNotNull(result.Segments);
      Assert.AreEqual(1, result.Segments.Count);
      Assert.AreEqual("Hello", result.Segments[0].Text);
    }
  }
}
