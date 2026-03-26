using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.UseCases;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.UseCases;

/// <summary>
/// P05-Persist-A3 — seam tests for <see cref="LibraryUseCase.ImportFilesAsync"/> project persistence (batch API only).
/// Filter: FullyQualifiedName~LibraryUseCaseImportFilesPersistenceTests
/// </summary>
[TestClass]
[TestCategory("SeamAware")]
public class LibraryUseCaseImportFilesPersistenceTests
{
  [TestMethod]
  public async Task ImportFilesAsync_WithActiveProject_CallsSaveAudioToProjectPerImportedItem()
  {
    var backend = new Mock<IBackendClient>();
    var context = new Mock<IContextManager>();
    var projectAudio = new Mock<IProjectAudioClient>();
    context.SetupGet(x => x.ActiveProjectId).Returns("proj-z");

    backend
        .Setup(x => x.PostAsync<LibraryUseCase.ImportFilesRequest, LibraryUseCase.ImportFilesResponse>(
            "/api/library/import",
            It.IsAny<LibraryUseCase.ImportFilesRequest>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new LibraryUseCase.ImportFilesResponse
        {
          ImportedItems = new List<LibraryItem>
          {
            new() { Id = "lib-1", Name = "a.wav" },
            new() { Id = "lib-2", Name = "b.wav" },
          },
        });

    projectAudio
        .Setup(x => x.SaveAudioToProjectAsync("proj-z", "lib-1", "f1.wav", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ProjectAudioFile());
    projectAudio
        .Setup(x => x.SaveAudioToProjectAsync("proj-z", "lib-2", "f2.wav", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ProjectAudioFile());

    var useCase = new LibraryUseCase(backend.Object, context.Object, projectAudio.Object);
    var paths = new List<string> { @"C:\imp\f1.wav", @"C:\imp\f2.wav" };

    var result = await useCase.ImportFilesAsync(paths, cancellationToken: CancellationToken.None);

    Assert.AreEqual(2, result.Count);
    projectAudio.Verify(
        x => x.SaveAudioToProjectAsync("proj-z", "lib-1", "f1.wav", It.IsAny<CancellationToken>()),
        Times.Once);
    projectAudio.Verify(
        x => x.SaveAudioToProjectAsync("proj-z", "lib-2", "f2.wav", It.IsAny<CancellationToken>()),
        Times.Once);
  }

  [TestMethod]
  public async Task ImportFilesAsync_WithoutActiveProject_DoesNotCallSaveAudioToProject()
  {
    var backend = new Mock<IBackendClient>();
    var context = new Mock<IContextManager>();
    var projectAudio = new Mock<IProjectAudioClient>();
    context.SetupGet(x => x.ActiveProjectId).Returns((string?)null);

    backend
        .Setup(x => x.PostAsync<LibraryUseCase.ImportFilesRequest, LibraryUseCase.ImportFilesResponse>(
            "/api/library/import",
            It.IsAny<LibraryUseCase.ImportFilesRequest>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new LibraryUseCase.ImportFilesResponse
        {
          ImportedItems = new List<LibraryItem> { new() { Id = "lib-1", Name = "a.wav" } },
        });

    var useCase = new LibraryUseCase(backend.Object, context.Object, projectAudio.Object);

    _ = await useCase.ImportFilesAsync(new List<string> { @"C:\x.wav" }, cancellationToken: CancellationToken.None);

    projectAudio.Verify(
        x => x.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
        Times.Never);
  }

  [TestMethod]
  public async Task ImportFilesAsync_WhenBackendReturnsEmpty_DoesNotCallSaveAudioToProject()
  {
    var backend = new Mock<IBackendClient>();
    var context = new Mock<IContextManager>();
    var projectAudio = new Mock<IProjectAudioClient>();
    context.SetupGet(x => x.ActiveProjectId).Returns("proj-z");

    backend
        .Setup(x => x.PostAsync<LibraryUseCase.ImportFilesRequest, LibraryUseCase.ImportFilesResponse>(
            "/api/library/import",
            It.IsAny<LibraryUseCase.ImportFilesRequest>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new LibraryUseCase.ImportFilesResponse { ImportedItems = new List<LibraryItem>() });

    var useCase = new LibraryUseCase(backend.Object, context.Object, projectAudio.Object);

    _ = await useCase.ImportFilesAsync(new List<string> { @"C:\x.wav" }, cancellationToken: CancellationToken.None);

    projectAudio.Verify(
        x => x.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
        Times.Never);
  }
}
