using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
    [TestClass]
    public class MacroViewModelTests
    {
    private IViewModelContext _context = null!;
    private Mock<IMacroClient> _mockMacroClient = null!;
    private Mock<IDialogService> _mockDialogService = null!;
    private DispatcherQueueController? _dispatcherController;
    private MacroViewModel _viewModel = null!;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);
      _mockMacroClient = new Mock<IMacroClient>();
      _mockDialogService = new Mock<IDialogService>();
      _mockDialogService
          .Setup(x => x.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
          .ReturnsAsync(true);
      _viewModel = new MacroViewModel(_context, _mockMacroClient.Object, _mockDialogService.Object);
    }

        [TestCleanup]
        public void Cleanup()
        {
            _dispatcherController?.ShutdownQueueAsync().AsTask().GetAwaiter().GetResult();
        }

        [TestMethod]
        public void Constructor_InitializesWithDefaultValues()
        {
            Assert.IsNotNull(_viewModel);
            Assert.AreEqual(PanelIds.Macro, _viewModel.PanelId);
            Assert.AreEqual("Macros", _viewModel.DisplayName);
            Assert.IsFalse(_viewModel.IsLoading);
        }

        [TestMethod]
        public void Macros_InitializesAsEmptyCollection()
        {
            Assert.IsNotNull(_viewModel.Macros);
            Assert.AreEqual(0, _viewModel.Macros.Count);
        }

        [TestMethod]
        public void SelectedMacro_DefaultsToNull()
        {
            Assert.IsNull(_viewModel.SelectedMacro);
        }

        [TestMethod]
        public void ShowMacrosView_DefaultsToTrue()
        {
            Assert.IsTrue(_viewModel.ShowMacrosView);
        }

        [TestMethod]
        public void AutomationCurves_InitializesAsEmptyCollection()
        {
            Assert.IsNotNull(_viewModel.AutomationCurves);
            Assert.AreEqual(0, _viewModel.AutomationCurves.Count);
        }

        [TestMethod]
        public void SelectedMacroCount_DefaultsToZero()
        {
            Assert.AreEqual(0, _viewModel.SelectedMacroCount);
        }

        [TestMethod]
        public void HasMultipleMacroSelection_DefaultsToFalse()
        {
            Assert.IsFalse(_viewModel.HasMultipleMacroSelection);
        }
    }
}
