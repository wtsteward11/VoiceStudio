using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class GlobalTransportOrchestratorTests
{
  private sealed class CapturingTimelineController : ITimelineTransportController
  {
    public bool IsPlaying { get; set; }
    public int PlayCount;
    public int PauseCount;
    public int StopCount;

    public Task PlayAsync()
    {
      PlayCount++;
      IsPlaying = true;
      return Task.CompletedTask;
    }

    public void Pause()
    {
      PauseCount++;
      IsPlaying = false;
    }

    public void Stop()
    {
      StopCount++;
      IsPlaying = false;
    }
  }

  private static GlobalTransportOrchestrator CreateSut(
      Mock<IContextManager> context,
      Mock<IAudioPlayerService> player,
      TransportOrchestrationBootstrap bootstrap)
  {
    return new GlobalTransportOrchestrator(
        context.Object,
        player.Object,
        new BackendClientConfig(),
        toastService: null,
        bootstrap);
  }

  [TestMethod]
  public async Task TogglePlaybackAsync_Timeline_WhenPlaying_CallsControllerPause()
  {
    var ctx = new Mock<IContextManager>();
    ctx.SetupGet(c => c.CurrentPlayableSource).Returns(TransportSource.Timeline);
    ctx.SetupGet(c => c.CurrentPlayableAudioId).Returns("t1");

    var player = new Mock<IAudioPlayerService>();
    var controller = new CapturingTimelineController { IsPlaying = true };
    var bootstrap = new TransportOrchestrationBootstrap();
    bootstrap.SetGetTimelineController(() => controller);

    var sut = CreateSut(ctx, player, bootstrap);
    await sut.TogglePlaybackAsync();

    Assert.AreEqual(1, controller.PauseCount);
    Assert.AreEqual(0, controller.PlayCount);
  }

  [TestMethod]
  public async Task TogglePlaybackAsync_Timeline_WhenNotPlaying_CallsControllerPlayAsync()
  {
    var ctx = new Mock<IContextManager>();
    ctx.SetupGet(c => c.CurrentPlayableSource).Returns(TransportSource.Timeline);
    ctx.SetupGet(c => c.CurrentPlayableAudioId).Returns("t1");

    var player = new Mock<IAudioPlayerService>();
    var controller = new CapturingTimelineController { IsPlaying = false };
    var bootstrap = new TransportOrchestrationBootstrap();
    bootstrap.SetGetTimelineController(() => controller);

    var sut = CreateSut(ctx, player, bootstrap);
    await sut.TogglePlaybackAsync();

    Assert.AreEqual(1, controller.PlayCount);
    Assert.AreEqual(0, controller.PauseCount);
  }

  [TestMethod]
  public async Task TogglePlaybackAsync_Timeline_ControllerNull_PlayerPlaying_PausesPlayer()
  {
    var ctx = new Mock<IContextManager>();
    ctx.SetupGet(c => c.CurrentPlayableSource).Returns(TransportSource.Timeline);
    ctx.SetupGet(c => c.CurrentPlayableAudioId).Returns("t1");

    var player = new Mock<IAudioPlayerService>();
    player.SetupGet(p => p.IsPlaying).Returns(true);
    player.SetupGet(p => p.IsPaused).Returns(false);

    var bootstrap = new TransportOrchestrationBootstrap();
    bootstrap.SetGetTimelineController(() => null);

    var sut = CreateSut(ctx, player, bootstrap);
    await sut.TogglePlaybackAsync();

    player.Verify(p => p.Pause(), Times.Once);
  }

  [TestMethod]
  public async Task TogglePlaybackAsync_Timeline_ControllerNull_PlayerPaused_ResumesPlayer()
  {
    var ctx = new Mock<IContextManager>();
    ctx.SetupGet(c => c.CurrentPlayableSource).Returns(TransportSource.Timeline);
    ctx.SetupGet(c => c.CurrentPlayableAudioId).Returns("t1");

    var player = new Mock<IAudioPlayerService>();
    player.SetupGet(p => p.IsPlaying).Returns(false);
    player.SetupGet(p => p.IsPaused).Returns(true);

    var bootstrap = new TransportOrchestrationBootstrap();
    bootstrap.SetGetTimelineController(() => null);

    var sut = CreateSut(ctx, player, bootstrap);
    await sut.TogglePlaybackAsync();

    player.Verify(p => p.Resume(), Times.Once);
  }

  [TestMethod]
  public void StopPlayback_Timeline_ControllerNull_CallsPlayerStop()
  {
    var ctx = new Mock<IContextManager>();
    ctx.SetupGet(c => c.CurrentPlayableSource).Returns(TransportSource.Timeline);

    var player = new Mock<IAudioPlayerService>();
    var bootstrap = new TransportOrchestrationBootstrap();
    bootstrap.SetGetTimelineController(() => null);

    var sut = CreateSut(ctx, player, bootstrap);
    sut.StopPlayback();

    player.Verify(p => p.Stop(), Times.Once);
  }

  [TestMethod]
  public void StopPlayback_Timeline_ControllerPresent_CallsControllerStopNotPlayer()
  {
    var ctx = new Mock<IContextManager>();
    ctx.SetupGet(c => c.CurrentPlayableSource).Returns(TransportSource.Timeline);

    var player = new Mock<IAudioPlayerService>();
    var controller = new CapturingTimelineController();
    var bootstrap = new TransportOrchestrationBootstrap();
    bootstrap.SetGetTimelineController(() => controller);

    var sut = CreateSut(ctx, player, bootstrap);
    sut.StopPlayback();

    Assert.AreEqual(1, controller.StopCount);
    player.Verify(p => p.Stop(), Times.Never);
  }

  [TestMethod]
  public void PausePlayback_Timeline_ControllerPlaying_CallsControllerPause()
  {
    var ctx = new Mock<IContextManager>();
    ctx.SetupGet(c => c.CurrentPlayableSource).Returns(TransportSource.Timeline);

    var player = new Mock<IAudioPlayerService>();
    var controller = new CapturingTimelineController { IsPlaying = true };
    var bootstrap = new TransportOrchestrationBootstrap();
    bootstrap.SetGetTimelineController(() => controller);

    var sut = CreateSut(ctx, player, bootstrap);
    sut.PausePlayback();

    Assert.AreEqual(1, controller.PauseCount);
    player.Verify(p => p.Pause(), Times.Never);
  }

  [TestMethod]
  public async Task TogglePlaybackAsync_Timeline_SequentialPlayPausePlay_NoOrphanCalls()
  {
    var ctx = new Mock<IContextManager>();
    ctx.SetupGet(c => c.CurrentPlayableSource).Returns(TransportSource.Timeline);
    ctx.SetupGet(c => c.CurrentPlayableAudioId).Returns("t1");

    var player = new Mock<IAudioPlayerService>();
    var controller = new CapturingTimelineController { IsPlaying = false };
    var bootstrap = new TransportOrchestrationBootstrap();
    bootstrap.SetGetTimelineController(() => controller);

    var sut = CreateSut(ctx, player, bootstrap);

    await sut.TogglePlaybackAsync();
    Assert.AreEqual(1, controller.PlayCount);
    Assert.AreEqual(0, controller.PauseCount);

    controller.IsPlaying = true;
    await sut.TogglePlaybackAsync();
    Assert.AreEqual(1, controller.PauseCount);

    controller.IsPlaying = false;
    await sut.TogglePlaybackAsync();
    Assert.AreEqual(2, controller.PlayCount);
  }

  [TestMethod]
  public void PausePlayback_Timeline_ControllerNull_PlayerPlaying_PausesPlayer()
  {
    var ctx = new Mock<IContextManager>();
    ctx.SetupGet(c => c.CurrentPlayableSource).Returns(TransportSource.Timeline);

    var player = new Mock<IAudioPlayerService>();
    player.SetupGet(p => p.IsPlaying).Returns(true);

    var bootstrap = new TransportOrchestrationBootstrap();
    bootstrap.SetGetTimelineController(() => null);

    var sut = CreateSut(ctx, player, bootstrap);
    sut.PausePlayback();

    player.Verify(p => p.Pause(), Times.Once);
  }
}
