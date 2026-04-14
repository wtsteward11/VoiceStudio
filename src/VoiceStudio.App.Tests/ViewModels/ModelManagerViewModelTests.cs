using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
    [TestClass]
    public class ModelManagerViewModelTests
    {
        private IViewModelContext _context = null!;
        private Mock<IModelManagerClient> _mockModelManagerClient = null!;
        private DispatcherQueueController? _dispatcherController;
        private ModelManagerViewModel _viewModel = null!;

        [TestInitialize]
        public void Setup()
        {
            _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
            var dispatcher = _dispatcherController.DispatcherQueue;
            _context = new ViewModelContext(NullLogger.Instance, dispatcher);
            _mockModelManagerClient = new Mock<IModelManagerClient>();
            _viewModel = new ModelManagerViewModel(_context, _mockModelManagerClient.Object);
        }

        [TestCleanup]
        public void Cleanup()
        {
            DispatcherQueueTestHelpers.ShutdownSyncBounded(_dispatcherController);
        }

        [TestMethod]
        public void Constructor_InitializesWithDefaultValues()
        {
            Assert.IsNotNull(_viewModel);
            Assert.AreEqual(PanelIds.ModelManager, _viewModel.PanelId);
            Assert.AreEqual("Model Manager", _viewModel.DisplayName);
            Assert.IsFalse(_viewModel.IsLoading);
        }

        [TestMethod]
        public void Models_InitializesAsEmptyCollection()
        {
            Assert.IsNotNull(_viewModel.Models);
            Assert.AreEqual(0, _viewModel.Models.Count);
        }

        [TestMethod]
        public void SelectedModel_DefaultsToNull()
        {
            Assert.IsNull(_viewModel.SelectedModel);
        }

        [TestMethod]
        public void Engines_ContainsExpectedEngines()
        {
            Assert.IsNotNull(_viewModel.Engines);
            Assert.IsTrue(_viewModel.Engines.Count > 0);
            Assert.IsTrue(_viewModel.Engines.Contains("xtts_v2"));
            Assert.IsTrue(_viewModel.Engines.Contains("piper"));
        }

        [TestMethod]
        public void HasError_ReturnsFalse_WhenNoErrorMessage()
        {
            _viewModel.ErrorMessage = null;
            Assert.IsFalse(_viewModel.HasError);
        }

        [TestMethod]
        public void HasError_ReturnsTrue_WhenErrorMessageSet()
        {
            _viewModel.ErrorMessage = "Test error";
            Assert.IsTrue(_viewModel.HasError);
        }

        [TestMethod]
        public void IsVerifying_DefaultsToFalse()
        {
            Assert.IsFalse(_viewModel.IsVerifying);
        }
    }
}
