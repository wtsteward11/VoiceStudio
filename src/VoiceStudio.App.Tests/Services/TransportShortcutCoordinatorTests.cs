using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class TransportShortcutCoordinatorTests
{
  [TestMethod]
  public void Attach_PlayShortcut_InvokesOrchestratorToggle()
  {
    var orchestrator = new Mock<IGlobalTransportOrchestrator>();
    orchestrator.Setup(o => o.TogglePlaybackAsync()).Returns(Task.CompletedTask);
    var shortcuts = new KeyboardShortcutService();
    var sut = new TransportShortcutCoordinator(orchestrator.Object);

    sut.Attach(shortcuts);
    shortcuts.ExecuteShortcut("playback.play");

    orchestrator.Verify(o => o.TogglePlaybackAsync(), Times.Once);
    sut.Detach();
  }

  [TestMethod]
  public void Attach_StopShortcut_InvokesOrchestratorStop()
  {
    var orchestrator = new Mock<IGlobalTransportOrchestrator>();
    var shortcuts = new KeyboardShortcutService();
    var sut = new TransportShortcutCoordinator(orchestrator.Object);

    sut.Attach(shortcuts);
    shortcuts.ExecuteShortcut("playback.stop");

    orchestrator.Verify(o => o.StopPlayback(), Times.Once);
    sut.Detach();
  }

  [TestMethod]
  public void Attach_RecordShortcut_InvokesOpenRecordingAction()
  {
    var orchestrator = new Mock<IGlobalTransportOrchestrator>();
    var shortcuts = new KeyboardShortcutService();
    var sut = new TransportShortcutCoordinator(orchestrator.Object);
    var recordCalls = 0;

    sut.Attach(shortcuts, () => recordCalls++);
    shortcuts.ExecuteShortcut("playback.record");

    Assert.AreEqual(1, recordCalls);
    sut.Detach();
  }
}
