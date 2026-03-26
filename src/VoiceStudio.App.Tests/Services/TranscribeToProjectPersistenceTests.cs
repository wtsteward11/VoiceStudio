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
/// Pass 05 Option A — unit tests for <see cref="TranscribeToProjectPersistence"/>.
/// Filter: FullyQualifiedName~TranscribeToProjectPersistenceTests
/// </summary>
[TestClass]
[TestCategory("SeamAware")]
public class TranscribeToProjectPersistenceTests
{
  [TestMethod]
  public async Task TrySaveLibraryAudio_NoProjectId_DoesNotCallClient()
  {
    var mock = new Mock<IProjectAudioClient>();
    var outcome = await TranscribeToProjectPersistence.TrySaveLibraryAudioToProjectAsync(
        mock.Object,
        null,
        null,
        "audio-1",
        CancellationToken.None);
    Assert.AreEqual(TranscribeProjectAudioSaveOutcome.SkippedNoProject, outcome);
    mock.Verify(
        x => x.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
        Times.Never);
  }

  [TestMethod]
  public async Task TrySaveLibraryAudio_EmptyAudioId_DoesNotCallClient()
  {
    var mock = new Mock<IProjectAudioClient>();
    var outcome = await TranscribeToProjectPersistence.TrySaveLibraryAudioToProjectAsync(
        mock.Object,
        null,
        "proj-1",
        "",
        CancellationToken.None);
    Assert.AreEqual(TranscribeProjectAudioSaveOutcome.SkippedNoAudioId, outcome);
    mock.Verify(
        x => x.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
        Times.Never);
  }

  [TestMethod]
  public async Task TrySaveLibraryAudio_WithProject_CallsSaveWithNullFilename()
  {
    var mock = new Mock<IProjectAudioClient>();
    mock
        .Setup(x => x.SaveAudioToProjectAsync("proj-1", "audio-1", null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ProjectAudioFile { Filename = "audio-1.wav" });

    var outcome = await TranscribeToProjectPersistence.TrySaveLibraryAudioToProjectAsync(
        mock.Object,
        null,
        "proj-1",
        "audio-1",
        CancellationToken.None);

    Assert.AreEqual(TranscribeProjectAudioSaveOutcome.Saved, outcome);
    mock.Verify(
        x => x.SaveAudioToProjectAsync("proj-1", "audio-1", null, It.IsAny<CancellationToken>()),
        Times.Once);
  }

  [TestMethod]
  public async Task TrySaveLibraryAudio_OnSaveException_LogsAndReturnsFailed()
  {
    var mock = new Mock<IProjectAudioClient>();
    mock
        .Setup(x => x.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new InvalidOperationException("boom"));
    var log = new Mock<IErrorLoggingService>();

    var outcome = await TranscribeToProjectPersistence.TrySaveLibraryAudioToProjectAsync(
        mock.Object,
        log.Object,
        "p",
        "a",
        CancellationToken.None);

    Assert.AreEqual(TranscribeProjectAudioSaveOutcome.Failed, outcome);
    log.Verify(x => x.LogError(It.IsAny<InvalidOperationException>(), "SaveTranscribeSourceToProject", null), Times.Once);
  }

  [TestMethod]
  [ExpectedException(typeof(ArgumentNullException))]
  public async Task TrySaveLibraryAudio_NullClient_Throws()
  {
    await TranscribeToProjectPersistence.TrySaveLibraryAudioToProjectAsync(
        null!,
        null,
        "p",
        "a",
        CancellationToken.None);
  }
}
