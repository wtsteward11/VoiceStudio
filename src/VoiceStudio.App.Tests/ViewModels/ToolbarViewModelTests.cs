using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Core.Commands;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels;

[TestClass]
public class ToolbarViewModelTests
{
    private ToolbarConfigurationService _toolbarConfigurationService = null!;
    private Mock<IUnifiedCommandRegistry> _commandRegistry = null!;
    private Mock<IUnifiedWorkspaceService> _workspaceService = null!;
    private Mock<IAudioPlayerService> _audioPlayerService = null!;
    private Mock<IToastNotificationService> _toastService = null!;

    [TestInitialize]
    public void SetUp()
    {
        _toolbarConfigurationService = new ToolbarConfigurationService();
        _commandRegistry = new Mock<IUnifiedCommandRegistry>();
        _workspaceService = new Mock<IUnifiedWorkspaceService>();
        _audioPlayerService = new Mock<IAudioPlayerService>();
        _audioPlayerService.SetupProperty(x => x.IsLooping, false);
        _toastService = new Mock<IToastNotificationService>();
    }

    [TestMethod]
    public async Task ExecuteToolbarActionAsync_Play_ExecutesRegisteredCommand()
    {
        _commandRegistry.Setup(x => x.IsRegistered("playback.play")).Returns(true);
        _commandRegistry
            .Setup(x => x.ExecuteAsync("playback.play", null, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var viewModel = CreateViewModel();

        await viewModel.ExecuteToolbarActionAsync("play");

        _commandRegistry.Verify(
            x => x.ExecuteAsync("playback.play", null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task ExecuteToolbarActionAsync_Loop_TogglesAudioLooping()
    {
        var viewModel = CreateViewModel();

        await viewModel.ExecuteToolbarActionAsync("loop");

        Assert.IsTrue(_audioPlayerService.Object.IsLooping);
        _toastService.Verify(
            x => x.ShowInfo(It.IsAny<string>(), "Loop"),
            Times.Once);
    }

    [TestMethod]
    public async Task SwitchWorkspaceAsync_WhenSucceeded_ReturnsTrue()
    {
        _workspaceService
            .Setup(x => x.SwitchWorkspaceProfileAsync("studio"))
            .ReturnsAsync(true);
        var viewModel = CreateViewModel();

        var result = await viewModel.SwitchWorkspaceAsync("studio");

        Assert.IsTrue(result);
        _toastService.Verify(
            x => x.ShowSuccess("Switched to: studio", "Workspace"),
            Times.Once);
    }

    [TestMethod]
    public void GetVisibleItems_ReturnsOrderedVisibleItems()
    {
        var viewModel = CreateViewModel();
        var items = viewModel.GetVisibleItems();

        Assert.IsTrue(items.Count > 0);
        Assert.IsTrue(items.SequenceEqual(items.OrderBy(i => i.Order)));
        Assert.IsTrue(items.All(i => i.IsVisible));
    }

    private ToolbarViewModel CreateViewModel()
    {
        return new ToolbarViewModel(
            _toolbarConfigurationService,
            _commandRegistry.Object,
            _workspaceService.Object,
            _audioPlayerService.Object,
            _toastService.Object);
    }
}
