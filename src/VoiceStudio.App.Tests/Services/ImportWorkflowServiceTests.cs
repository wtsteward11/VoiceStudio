using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services;

/// <summary>
/// Pass 05 P05-Persist-A2 — seam tests for <see cref="ImportWorkflowService.ApplyPostSingleFileLibraryImportSuccessAsync"/>.
/// Filter: FullyQualifiedName~ImportWorkflowServiceTests
/// </summary>
[TestClass]
[TestCategory("SeamAware")]
public class ImportWorkflowServiceTests
{
  [TestMethod]
  public async Task ApplyPostSingleFileLibraryImportSuccessAsync_WithProject_CallsSaveAudioToProjectOnce()
  {
    var mockLibrary = new Mock<ILibraryClient>();
    var mockCtx = new Mock<IContextManager>();
    mockCtx.SetupGet(c => c.ActiveProjectId).Returns("proj-a2");
    var mockProject = new Mock<IProjectAudioClient>();
    mockProject
        .Setup(x => x.SaveAudioToProjectAsync("proj-a2", "playback-1", "clip.wav", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ProjectAudioFile { Filename = "clip.wav" });
    var mockAgg = new Mock<IEventAggregator>();
    var sut = new ImportWorkflowService(mockLibrary.Object, mockCtx.Object, mockProject.Object, null, mockAgg.Object);

    var asset = new LibraryAsset { Id = "asset-row-1", AudioId = "playback-1", Name = "clip.wav" };
    await sut.ApplyPostSingleFileLibraryImportSuccessAsync(asset, @"D:\audio\clip.wav", CancellationToken.None);

    mockProject.Verify(
        x => x.SaveAudioToProjectAsync("proj-a2", "playback-1", "clip.wav", It.IsAny<CancellationToken>()),
        Times.Once);
    mockAgg.Verify(
        x => x.Publish(It.Is<AssetAddedEvent>(e => e.SourcePanelId == "import-workflow" && e.AssetId == "playback-1")),
        Times.Once);
  }

  [TestMethod]
  public async Task ApplyPostSingleFileLibraryImportSuccessAsync_WithoutProject_DoesNotCallSaveAudioToProject()
  {
    var mockLibrary = new Mock<ILibraryClient>();
    var mockCtx = new Mock<IContextManager>();
    mockCtx.SetupGet(c => c.ActiveProjectId).Returns((string?)null);
    var mockProject = new Mock<IProjectAudioClient>();
    var mockAgg = new Mock<IEventAggregator>();
    var sut = new ImportWorkflowService(mockLibrary.Object, mockCtx.Object, mockProject.Object, null, mockAgg.Object);

    var asset = new LibraryAsset { Id = "a1", AudioId = "p1" };
    await sut.ApplyPostSingleFileLibraryImportSuccessAsync(asset, @"C:\x\y.wav", CancellationToken.None);

    mockProject.Verify(
        x => x.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
        Times.Never);
  }

  [TestMethod]
  public async Task ApplyPostSingleFileLibraryImportSuccessAsync_WhenSaveThrows_CompletesWithoutThrowing()
  {
    var mockLibrary = new Mock<ILibraryClient>();
    var mockCtx = new Mock<IContextManager>();
    mockCtx.SetupGet(c => c.ActiveProjectId).Returns("proj-x");
    var mockProject = new Mock<IProjectAudioClient>();
    mockProject
        .Setup(x => x.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new InvalidOperationException("save failed"));
    var log = new Mock<IErrorLoggingService>();
    var mockAgg = new Mock<IEventAggregator>();
    var sut = new ImportWorkflowService(mockLibrary.Object, mockCtx.Object, mockProject.Object, log.Object, mockAgg.Object);

    var asset = new LibraryAsset { Id = "a1", AudioId = "audio-x" };
    await sut.ApplyPostSingleFileLibraryImportSuccessAsync(asset, @"C:\z\s.wav", CancellationToken.None);

    log.Verify(x => x.LogError(It.IsAny<Exception>(), "SaveImportToProject", null), Times.Once);
    mockAgg.Verify(x => x.Publish(It.IsAny<AssetAddedEvent>()), Times.Once);
  }
}
