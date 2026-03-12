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
  public class ProjectAudioClientTests
  {
    [TestMethod]
    [ExpectedException(typeof(System.ArgumentException))]
    public async Task SaveAudioToProjectAsync_WhenFilenameContainsInvalidChars_ThrowsArgumentException()
    {
      var mockBackend = new Mock<IBackendClient>();
      var sut = new ProjectAudioClient(mockBackend.Object);

      await sut.SaveAudioToProjectAsync("p1", "audio-1", "file<>name.wav").ConfigureAwait(false);

      mockBackend.Verify(x => x.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task SaveAudioToProjectAsync_WhenFilenameExistsInProject_ReturnsExistingWithoutCallingBackend()
    {
      var mockBackend = new Mock<IBackendClient>();
      var existing = new List<ProjectAudioFile>
      {
        new() { Filename = "clip1.wav", Url = "http://localhost/audio/clip1.wav" }
      };
      mockBackend.Setup(x => x.ListProjectAudioAsync("p1", It.IsAny<CancellationToken>()))
          .ReturnsAsync(existing);

      var sut = new ProjectAudioClient(mockBackend.Object);
      var result = await sut.SaveAudioToProjectAsync("p1", "audio-1", "clip1.wav").ConfigureAwait(false);

      Assert.AreEqual("clip1.wav", result.Filename);
      mockBackend.Verify(x => x.ListProjectAudioAsync("p1", It.IsAny<CancellationToken>()), Times.Once);
      mockBackend.Verify(x => x.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task SaveAudioToProjectAsync_WhenFilenameNotExists_CallsBackend()
    {
      var mockBackend = new Mock<IBackendClient>();
      mockBackend.Setup(x => x.ListProjectAudioAsync("p1", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<ProjectAudioFile>());
      mockBackend.Setup(x => x.SaveAudioToProjectAsync("p1", "audio-1", "new.wav", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new ProjectAudioFile { Filename = "new.wav", Url = "http://localhost/audio/new.wav" });

      var sut = new ProjectAudioClient(mockBackend.Object);
      var result = await sut.SaveAudioToProjectAsync("p1", "audio-1", "new.wav").ConfigureAwait(false);

      Assert.AreEqual("new.wav", result.Filename);
      mockBackend.Verify(x => x.SaveAudioToProjectAsync("p1", "audio-1", "new.wav", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task SaveAudioToProjectAsync_WhenFilenameNull_CallsBackendWithoutDedup()
    {
      var mockBackend = new Mock<IBackendClient>();
      mockBackend.Setup(x => x.SaveAudioToProjectAsync("p1", "audio-1", null, It.IsAny<CancellationToken>()))
          .ReturnsAsync(new ProjectAudioFile { Filename = "generated.wav", Url = "http://localhost/audio/generated.wav" });

      var sut = new ProjectAudioClient(mockBackend.Object);
      var result = await sut.SaveAudioToProjectAsync("p1", "audio-1", null).ConfigureAwait(false);

      Assert.AreEqual("generated.wav", result.Filename);
      mockBackend.Verify(x => x.ListProjectAudioAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
      mockBackend.Verify(x => x.SaveAudioToProjectAsync("p1", "audio-1", null, It.IsAny<CancellationToken>()), Times.Once);
    }
  }
}
