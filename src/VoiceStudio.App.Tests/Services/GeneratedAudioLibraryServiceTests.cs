using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services
{
  [TestClass]
  public sealed class GeneratedAudioLibraryServiceTests
  {
    private Mock<IEventAggregator> _events = null!;
    private Mock<ILibraryClient> _library = null!;
    private Mock<IContextManager> _ctx = null!;
    private Mock<IProjectAudioClient> _projectAudio = null!;
    private Mock<IErrorLoggingService>? _log;

    [TestInitialize]
    public void Setup()
    {
      _events = new Mock<IEventAggregator>();
      _library = new Mock<ILibraryClient>();
      _ctx = new Mock<IContextManager>();
      _projectAudio = new Mock<IProjectAudioClient>();
      _log = new Mock<IErrorLoggingService>();
    }

    private GeneratedAudioLibraryService CreateSut(IErrorLoggingService? log = null) =>
        new(_events.Object, _library.Object, _ctx.Object, _projectAudio.Object, log);

    private static GeneratedAudioSaveRequest Request(string audioId, string? audioRef) =>
        new(
            "voice-synthesis",
            audioId,
            audioRef,
            TimeSpan.FromSeconds(1),
            "pid",
            "pname",
            "eng",
            DateTime.UtcNow);

    private static void TryDeleteTempFile(string path)
    {
      try
      {
        if (File.Exists(path))
          File.Delete(path);
      }
      catch (IOException ex)
      {
        Debug.WriteLine("[GeneratedAudioLibraryServiceTests] Temp file cleanup: " + ex.Message);
      }
    }

    private static void TryDeleteTempDirectory(string path)
    {
      try
      {
        if (Directory.Exists(path))
          Directory.Delete(path);
      }
      catch (IOException ex)
      {
        Debug.WriteLine("[GeneratedAudioLibraryServiceTests] Temp dir cleanup: " + ex.Message);
      }
    }

    [TestMethod]
    public async Task LocalFile_UploadsAndSetsPlayable_WhenNoActiveProject_ReturnsLibraryBacked()
    {
      var tmp = Path.Combine(Path.GetTempPath(), $"vs-gen-save-{Guid.NewGuid():N}.wav");
      File.WriteAllText(tmp, "x");

      try
      {
        var uploaded = new LibraryAsset
        {
          Id = "asset-1",
          AudioId = "play-a1",
          Metadata = new Dictionary<string, object>(),
        };
        _library
            .Setup(l => l.UploadLibraryAssetAsync(tmp, It.IsAny<CancellationToken>()))
            .ReturnsAsync(uploaded);
        _ctx.Setup(c => c.ActiveProjectId).Returns((string?)null);

        var sut = CreateSut();
        var r = await sut.SaveAsync(Request("ignored", tmp)).ConfigureAwait(false);

        Assert.IsTrue(r.Success);
        Assert.AreEqual(GeneratedAudioSaveKind.LibraryBacked, r.SaveKind);
        Assert.AreEqual("asset-1", r.AssetId);
        Assert.AreEqual("play-a1", r.PlaybackAudioId);
        _projectAudio.Verify(
            p => p.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _ctx.Verify(c => c.SetCurrentPlayable("play-a1", TransportSource.Library, Path.GetFileName(tmp)), Times.Once);
        _ctx.Verify(c => c.SetActiveAsset(
            "asset-1",
            "audio",
            Path.GetFileName(tmp),
            It.IsAny<InteractionIntent>()),
            Times.Once);
        _events.Verify(
            e => e.Publish(It.Is<AssetAddedEvent>(a => a.AssetId == "play-a1" && a.AssetPath == tmp)),
            Times.Once);
      }
      finally
      {
        TryDeleteTempFile(tmp);
      }
    }

    [TestMethod]
    public async Task LocalFile_PrefersMetadataUploadId_WhenAudioIdMissing()
    {
      var tmp = Path.Combine(Path.GetTempPath(), $"vs-gen-up-{Guid.NewGuid():N}.wav");
      File.WriteAllText(tmp, "x");

      try
      {
        var uploaded = new LibraryAsset
        {
          Id = "row-id",
          AudioId = null,
          Metadata = new Dictionary<string, object> { { "upload_id", "up-99" } },
        };
        _library.Setup(l => l.UploadLibraryAssetAsync(tmp, It.IsAny<CancellationToken>())).ReturnsAsync(uploaded);
        _ctx.Setup(c => c.ActiveProjectId).Returns((string?)null);

        var sut = CreateSut();
        var r = await sut.SaveAsync(Request("x", tmp)).ConfigureAwait(false);

        Assert.AreEqual("up-99", r.PlaybackAudioId);
        _events.Verify(
            e => e.Publish(It.Is<AssetAddedEvent>(a => a.AssetId == "up-99")),
            Times.Once);
      }
      finally
      {
        TryDeleteTempFile(tmp);
      }
    }

    [TestMethod]
    public async Task LocalFile_FallsBackToId_WhenNoAudioIdOrUploadId()
    {
      var tmp = Path.Combine(Path.GetTempPath(), $"vs-gen-fb-{Guid.NewGuid():N}.wav");
      File.WriteAllText(tmp, "x");

      try
      {
        var uploaded = new LibraryAsset
        {
          Id = "only-id",
          AudioId = null,
          Metadata = new Dictionary<string, object>(),
        };
        _library.Setup(l => l.UploadLibraryAssetAsync(tmp, It.IsAny<CancellationToken>())).ReturnsAsync(uploaded);
        _ctx.Setup(c => c.ActiveProjectId).Returns((string?)null);

        var sut = CreateSut();
        var r = await sut.SaveAsync(Request("x", tmp)).ConfigureAwait(false);

        Assert.AreEqual("only-id", r.PlaybackAudioId);
      }
      finally
      {
        TryDeleteTempFile(tmp);
      }
    }

    [TestMethod]
    public async Task LocalFile_ActiveProject_CallsProjectSave_ReturnsProjectBacked()
    {
      var tmp = Path.Combine(Path.GetTempPath(), $"vs-gen-prj-{Guid.NewGuid():N}.wav");
      File.WriteAllText(tmp, "x");

      try
      {
        var uploaded = new LibraryAsset { Id = "a1", AudioId = "p1" };
        _library.Setup(l => l.UploadLibraryAssetAsync(tmp, It.IsAny<CancellationToken>())).ReturnsAsync(uploaded);
        _ctx.Setup(c => c.ActiveProjectId).Returns("proj-z");
        _projectAudio
            .Setup(p => p.SaveAudioToProjectAsync("proj-z", "p1", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectAudioFile());

        var sut = CreateSut();
        var r = await sut.SaveAsync(Request("id", tmp)).ConfigureAwait(false);

        Assert.IsTrue(r.Success);
        Assert.AreEqual(GeneratedAudioSaveKind.ProjectBacked, r.SaveKind);
        Assert.AreEqual("proj-z", r.ProjectId);
        _projectAudio.Verify(
            p => p.SaveAudioToProjectAsync("proj-z", "p1", Path.GetFileName(tmp), It.IsAny<CancellationToken>()),
            Times.Once);
      }
      finally
      {
        TryDeleteTempFile(tmp);
      }
    }

    [TestMethod]
    public async Task LocalFile_ProjectSaveFails_ReturnsLibraryBacked_WithMessage()
    {
      var tmp = Path.Combine(Path.GetTempPath(), $"vs-gen-pe-{Guid.NewGuid():N}.wav");
      File.WriteAllText(tmp, "x");

      try
      {
        var uploaded = new LibraryAsset { Id = "a1", AudioId = "p1" };
        _library.Setup(l => l.UploadLibraryAssetAsync(tmp, It.IsAny<CancellationToken>())).ReturnsAsync(uploaded);
        _ctx.Setup(c => c.ActiveProjectId).Returns("proj-z");
        _projectAudio
            .Setup(p => p.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("project boom"));

        var sut = CreateSut(_log!.Object);
        var r = await sut.SaveAsync(Request("id", tmp)).ConfigureAwait(false);

        Assert.IsTrue(r.Success);
        Assert.AreEqual(GeneratedAudioSaveKind.LibraryBacked, r.SaveKind);
        Assert.IsTrue(r.Message?.Contains("project save failed", StringComparison.OrdinalIgnoreCase) == true);
        _log.Verify(
            l => l.LogError(
                It.IsAny<Exception>(),
                "GeneratedAudioLibrary.SaveToProject",
                It.IsAny<Dictionary<string, object>?>()),
            Times.Once);
      }
      finally
      {
        TryDeleteTempFile(tmp);
      }
    }

    [TestMethod]
    public async Task LocalFile_UploadNull_ReturnsFailed_NoProjectSave()
    {
      var tmp = Path.Combine(Path.GetTempPath(), $"vs-gen-null-{Guid.NewGuid():N}.wav");
      File.WriteAllText(tmp, "x");

      try
      {
        _library.Setup(l => l.UploadLibraryAssetAsync(tmp, It.IsAny<CancellationToken>())).ReturnsAsync((LibraryAsset?)null);

        var sut = CreateSut();
        var r = await sut.SaveAsync(Request("id", tmp)).ConfigureAwait(false);

        Assert.IsFalse(r.Success);
        Assert.AreEqual(GeneratedAudioSaveKind.Failed, r.SaveKind);
        _projectAudio.Verify(
            p => p.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _events.Verify(e => e.Publish(It.IsAny<AssetAddedEvent>()), Times.Never);
      }
      finally
      {
        TryDeleteTempFile(tmp);
      }
    }

    [TestMethod]
    public async Task ApiOnly_PublishesEvent_DoesNotUploadOrProjectSave_ReturnsEventNotified()
    {
      var sut = CreateSut();
      var r = await sut.SaveAsync(Request("aid-1", "/api/audio/aid-1")).ConfigureAwait(false);

      Assert.IsTrue(r.Success);
      Assert.AreEqual(GeneratedAudioSaveKind.EventNotified, r.SaveKind);
      _library.Verify(
          l => l.UploadLibraryAssetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
          Times.Never);
      _projectAudio.Verify(
          p => p.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
          Times.Never);
      _events.Verify(
          e => e.Publish(It.Is<AssetAddedEvent>(a => a.AssetId == "aid-1")),
          Times.Once);
    }

    [TestMethod]
    public void TryResolveLocalFile_RejectsDirectories()
    {
      var dir = Path.Combine(Path.GetTempPath(), $"vs-dir-{Guid.NewGuid():N}");
      Directory.CreateDirectory(dir);
      try
      {
        var req = Request("x", dir);
        Assert.IsFalse(GeneratedAudioLibraryService.TryResolveLocalFileForUpload(req, out _));
      }
      finally
      {
        TryDeleteTempDirectory(dir);
      }
    }

    [TestMethod]
    public async Task NoLocalFile_NoIds_ReturnsFailed()
    {
      var sut = CreateSut();
      var r = await sut.SaveAsync(Request(string.Empty, null)).ConfigureAwait(false);
      Assert.IsFalse(r.Success);
      Assert.AreEqual(GeneratedAudioSaveKind.Failed, r.SaveKind);
    }
  }
}
