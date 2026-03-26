using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.App.UseCases;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services;

/// <summary>
/// Pass 05 P05-Persist-A2 — unit tests for <see cref="ImportToProjectPersistence"/>.
/// Filter: FullyQualifiedName~ImportToProjectPersistenceTests
/// </summary>
[TestClass]
[TestCategory("SeamAware")]
public class ImportToProjectPersistenceTests
{
  [TestMethod]
  public async Task TrySaveAfterSingleFileImport_NoProjectId_DoesNotCallClient()
  {
    var mock = new Mock<IProjectAudioClient>();
    await ImportToProjectPersistence.TrySaveAfterSingleFileImportAsync(
        mock.Object,
        null,
        null,
        "audio-1",
        @"C:\tmp\in.wav",
        CancellationToken.None);
    mock.Verify(
        x => x.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
        Times.Never);
  }

  [TestMethod]
  public async Task TrySaveAfterSingleFileImport_EmptyAudioId_DoesNotCallClient()
  {
    var mock = new Mock<IProjectAudioClient>();
    await ImportToProjectPersistence.TrySaveAfterSingleFileImportAsync(
        mock.Object,
        null,
        "proj-1",
        "",
        @"C:\tmp\in.wav",
        CancellationToken.None);
    mock.Verify(
        x => x.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
        Times.Never);
  }

  [TestMethod]
  public async Task TrySaveAfterSingleFileImport_WithProject_CallsSaveWithFilename()
  {
    var mock = new Mock<IProjectAudioClient>();
    mock
        .Setup(x => x.SaveAudioToProjectAsync("proj-1", "lib-audio-1", "in.wav", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ProjectAudioFile { Filename = "in.wav" });

    await ImportToProjectPersistence.TrySaveAfterSingleFileImportAsync(
        mock.Object,
        null,
        "proj-1",
        "lib-audio-1",
        @"C:\temp\in.wav",
        CancellationToken.None);

    mock.Verify(
        x => x.SaveAudioToProjectAsync("proj-1", "lib-audio-1", "in.wav", It.IsAny<CancellationToken>()),
        Times.Once);
  }

  [TestMethod]
  public async Task TrySaveAfterSingleFileImport_OnSaveException_LogsAndDoesNotThrow()
  {
    var mock = new Mock<IProjectAudioClient>();
    mock
        .Setup(x => x.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new InvalidOperationException("boom"));
    var log = new Mock<IErrorLoggingService>();

    await ImportToProjectPersistence.TrySaveAfterSingleFileImportAsync(
        mock.Object,
        log.Object,
        "p",
        "a",
        "x.wav",
        CancellationToken.None);

    log.Verify(x => x.LogError(It.IsAny<Exception>(), "SaveImportToProject", null), Times.Once);
  }

  [TestMethod]
  [ExpectedException(typeof(ArgumentNullException))]
  public async Task TrySaveAfterSingleFileImport_NullClient_Throws()
  {
    await ImportToProjectPersistence.TrySaveAfterSingleFileImportAsync(
        null!,
        null,
        "p",
        "a",
        null,
        CancellationToken.None);
  }

  [TestMethod]
  public async Task TrySaveAfterBatchLibraryImport_NoProjectId_DoesNotCallClient()
  {
    var mock = new Mock<IProjectAudioClient>();
    await ImportToProjectPersistence.TrySaveAfterBatchLibraryImportAsync(
        mock.Object,
        null,
        null,
        new List<string> { @"C:\a\1.wav" },
        new List<LibraryItem> { new() { Id = "x1", Name = "1.wav" } },
        CancellationToken.None);
    mock.Verify(
        x => x.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
        Times.Never);
  }

  [TestMethod]
  public async Task TrySaveAfterBatchLibraryImport_WithProject_CallsSavePerItemWithFilename()
  {
    var mock = new Mock<IProjectAudioClient>();
    mock
        .Setup(x => x.SaveAudioToProjectAsync("proj-1", "id-1", "one.wav", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ProjectAudioFile { Filename = "one.wav" });
    mock
        .Setup(x => x.SaveAudioToProjectAsync("proj-1", "id-2", "two.wav", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ProjectAudioFile { Filename = "two.wav" });

    await ImportToProjectPersistence.TrySaveAfterBatchLibraryImportAsync(
        mock.Object,
        null,
        "proj-1",
        new List<string> { @"C:\x\one.wav", @"D:\y\two.wav" },
        new List<LibraryItem>
        {
          new() { Id = "id-1", Name = "n1" },
          new() { Id = "id-2", Name = "n2" },
        },
        CancellationToken.None);

    mock.Verify(
        x => x.SaveAudioToProjectAsync("proj-1", "id-1", "one.wav", It.IsAny<CancellationToken>()),
        Times.Once);
    mock.Verify(
        x => x.SaveAudioToProjectAsync("proj-1", "id-2", "two.wav", It.IsAny<CancellationToken>()),
        Times.Once);
  }

  [TestMethod]
  public async Task TrySaveAfterBatchLibraryImport_WhenSaveThrows_LogsBatchContextAndContinues()
  {
    var mock = new Mock<IProjectAudioClient>();
    mock
        .Setup(x => x.SaveAudioToProjectAsync("p", "first", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new InvalidOperationException("boom"));
    mock
        .Setup(x => x.SaveAudioToProjectAsync("p", "second", "b.wav", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ProjectAudioFile());
    var log = new Mock<IErrorLoggingService>();

    await ImportToProjectPersistence.TrySaveAfterBatchLibraryImportAsync(
        mock.Object,
        log.Object,
        "p",
        new List<string> { @"C:\a.wav", @"C:\b.wav" },
        new List<LibraryItem>
        {
          new() { Id = "first", Name = "a.wav" },
          new() { Id = "second", Name = "b.wav" },
        },
        CancellationToken.None);

    log.Verify(x => x.LogError(It.IsAny<Exception>(), "SaveBatchImportToProject", null), Times.Once);
    mock.Verify(
        x => x.SaveAudioToProjectAsync("p", "second", "b.wav", It.IsAny<CancellationToken>()),
        Times.Once);
  }

  [TestMethod]
  [ExpectedException(typeof(ArgumentNullException))]
  public async Task TrySaveAfterBatchLibraryImport_NullImportedItems_Throws()
  {
    await ImportToProjectPersistence.TrySaveAfterBatchLibraryImportAsync(
        new Mock<IProjectAudioClient>().Object,
        null,
        "p",
        new List<string>(),
        null!,
        CancellationToken.None);
  }
}
