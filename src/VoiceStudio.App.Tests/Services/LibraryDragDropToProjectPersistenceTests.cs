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
/// Pass 05 P05-Persist-A4 — unit tests for <see cref="LibraryDragDropToProjectPersistence"/>.
/// Filter: append <c>FullyQualifiedName~LibraryDragDropToProjectPersistenceTests</c> to Option A §7 seam filter.
/// </summary>
[TestClass]
[TestCategory("SeamAware")]
public class LibraryDragDropToProjectPersistenceTests
{
  [TestMethod]
  public async Task TrySaveAfterLibraryDragDropUpload_NoActiveProject_DoesNotCallClient()
  {
    var mockClient = new Mock<IProjectAudioClient>();
    var ctx = new Mock<IContextManager>();
    ctx.SetupGet(c => c.ActiveProjectId).Returns((string?)null);

    await LibraryDragDropToProjectPersistence.TrySaveAfterLibraryDragDropUploadAsync(
        mockClient.Object,
        null,
        ctx.Object,
        "lib-audio-1",
        @"C:\tmp\in.wav",
        CancellationToken.None);

    mockClient.Verify(
        x => x.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
        Times.Never);
  }

  [TestMethod]
  public async Task TrySaveAfterLibraryDragDropUpload_WithProject_CallsSaveWithFilename()
  {
    var mockClient = new Mock<IProjectAudioClient>();
    mockClient
        .Setup(x => x.SaveAudioToProjectAsync("proj-1", "lib-audio-1", "in.wav", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ProjectAudioFile { Filename = "in.wav" });
    var ctx = new Mock<IContextManager>();
    ctx.SetupGet(c => c.ActiveProjectId).Returns("proj-1");

    await LibraryDragDropToProjectPersistence.TrySaveAfterLibraryDragDropUploadAsync(
        mockClient.Object,
        null,
        ctx.Object,
        "lib-audio-1",
        @"C:\temp\in.wav",
        CancellationToken.None);

    mockClient.Verify(
        x => x.SaveAudioToProjectAsync("proj-1", "lib-audio-1", "in.wav", It.IsAny<CancellationToken>()),
        Times.Once);
  }

  [TestMethod]
  public async Task TrySaveAfterLibraryDragDropUpload_OnSaveException_LogsAndDoesNotThrow()
  {
    var mockClient = new Mock<IProjectAudioClient>();
    mockClient
        .Setup(x => x.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new InvalidOperationException("boom"));
    var log = new Mock<IErrorLoggingService>();
    var ctx = new Mock<IContextManager>();
    ctx.SetupGet(c => c.ActiveProjectId).Returns("p1");

    await LibraryDragDropToProjectPersistence.TrySaveAfterLibraryDragDropUploadAsync(
        mockClient.Object,
        log.Object,
        ctx.Object,
        "a",
        "x.wav",
        CancellationToken.None);

    log.Verify(x => x.LogError(It.IsAny<Exception>(), "SaveDragDropToProject", null), Times.Once);
  }
}
