using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services;

/// <summary>
/// Pass 05 Option C — unit tests for <see cref="RecordingToProjectPersistence"/>.
/// Filter: FullyQualifiedName~RecordingToProjectPersistenceTests
/// </summary>
[TestClass]
[TestCategory("SeamAware")]
public class RecordingToProjectPersistenceTests
{
  [TestMethod]
  public async Task TrySaveAfterUpload_NoProjectId_DoesNotCallClient()
  {
    var mock = new Mock<IProjectAudioClient>();
    await RecordingToProjectPersistence.TrySaveAfterUploadAsync(
        mock.Object,
        null,
        null,
        "audio-1",
        @"C:\tmp\rec.wav",
        CancellationToken.None);
    mock.Verify(
        x => x.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
        Times.Never);
  }

  [TestMethod]
  public async Task TrySaveAfterUpload_EmptyAudioId_DoesNotCallClient()
  {
    var mock = new Mock<IProjectAudioClient>();
    await RecordingToProjectPersistence.TrySaveAfterUploadAsync(
        mock.Object,
        null,
        "proj-1",
        "",
        @"C:\tmp\rec.wav",
        CancellationToken.None);
    mock.Verify(
        x => x.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
        Times.Never);
  }

  [TestMethod]
  public async Task TrySaveAfterUpload_WithProject_CallsSaveWithFilename()
  {
    var mock = new Mock<IProjectAudioClient>();
    mock
        .Setup(x => x.SaveAudioToProjectAsync("proj-1", "audio-1", "rec.wav", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ProjectAudioFile { Filename = "rec.wav" });

    await RecordingToProjectPersistence.TrySaveAfterUploadAsync(
        mock.Object,
        null,
        "proj-1",
        "audio-1",
        @"C:\temp\rec.wav",
        CancellationToken.None);

    mock.Verify(
        x => x.SaveAudioToProjectAsync("proj-1", "audio-1", "rec.wav", It.IsAny<CancellationToken>()),
        Times.Once);
  }

  [TestMethod]
  public async Task TrySaveAfterUpload_OnSaveException_LogsAndDoesNotThrow()
  {
    var mock = new Mock<IProjectAudioClient>();
    mock
        .Setup(x => x.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new InvalidOperationException("boom"));
    var log = new Mock<IErrorLoggingService>();

    await RecordingToProjectPersistence.TrySaveAfterUploadAsync(
        mock.Object,
        log.Object,
        "p",
        "a",
        "x.wav",
        CancellationToken.None);

    log.Verify(x => x.LogError(It.IsAny<Exception>(), "SaveRecordingToProject", null), Times.Once);
  }

  [TestMethod]
  [ExpectedException(typeof(ArgumentNullException))]
  public async Task TrySaveAfterUpload_NullClient_Throws()
  {
    await RecordingToProjectPersistence.TrySaveAfterUploadAsync(
        null!,
        null,
        "p",
        "a",
        null,
        CancellationToken.None);
  }
}
