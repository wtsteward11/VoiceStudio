using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowProjectWorkflowBridgeTests
{
    [TestMethod]
    public async Task SaveProjectAsync_invokes_coordinator_when_present()
    {
        var mock = new Mock<IProjectWorkflowCoordinator>(MockBehavior.Strict);
        mock.Setup(c => c.SaveProjectAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask).Verifiable();
        var bridge = new MainWindowProjectWorkflowBridge(() => mock.Object);

        await bridge.SaveProjectAsync().ConfigureAwait(false);

        mock.Verify(c => c.SaveProjectAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task SaveProjectAsync_noop_when_coordinator_null()
    {
        var bridge = new MainWindowProjectWorkflowBridge(() => null);
        await bridge.SaveProjectAsync().ConfigureAwait(false);
    }

    [TestMethod]
    public async Task CreateNewProjectAsync_invokes_coordinator_when_present()
    {
        var mock = new Mock<IProjectWorkflowCoordinator>(MockBehavior.Strict);
        mock.Setup(c => c.CreateNewProjectAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask).Verifiable();
        var bridge = new MainWindowProjectWorkflowBridge(() => mock.Object);

        await bridge.CreateNewProjectAsync().ConfigureAwait(false);

        mock.Verify(c => c.CreateNewProjectAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task OpenProjectAsync_invokes_coordinator_when_present()
    {
        var mock = new Mock<IProjectWorkflowCoordinator>(MockBehavior.Strict);
        mock.Setup(c => c.OpenProjectAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask).Verifiable();
        var bridge = new MainWindowProjectWorkflowBridge(() => mock.Object);

        await bridge.OpenProjectAsync().ConfigureAwait(false);

        mock.Verify(c => c.OpenProjectAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task OpenRecentProjectAsync_passes_ids_to_coordinator()
    {
        var mock = new Mock<IProjectWorkflowCoordinator>(MockBehavior.Strict);
        mock.Setup(c => c.OpenRecentProjectAsync("p1", "Name1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();
        var bridge = new MainWindowProjectWorkflowBridge(() => mock.Object);

        await bridge.OpenRecentProjectAsync("p1", "Name1").ConfigureAwait(false);

        mock.Verify(c => c.OpenRecentProjectAsync("p1", "Name1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task OpenRecentProjectAsync_noop_when_coordinator_null()
    {
        var bridge = new MainWindowProjectWorkflowBridge(() => null);
        await bridge.OpenRecentProjectAsync("x", "y").ConfigureAwait(false);
    }
}
