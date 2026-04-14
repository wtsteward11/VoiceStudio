using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for TrainingDatasetEditorViewModel.
  /// Instantiates ViewModel with mocked ITrainingClient and ITrainingDatasetEditorClient.
  /// Supports "TrainingDatasetEditorViewModel migrated to ITrainingDatasetEditorClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class TrainingDatasetEditorViewModelSeamTests
  {
    private Mock<ITrainingClient> _mockTrainingClient = null!;
    private Mock<ITrainingDatasetEditorClient> _mockDatasetEditorClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      _mockTrainingClient = new Mock<ITrainingClient>();
      _mockDatasetEditorClient = new Mock<ITrainingDatasetEditorClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockTrainingClient
          .Setup(x => x.ListDatasetsAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<VoiceStudio.Core.Models.TrainingDataset>());
    }

    [TestCleanup]
    public void Cleanup()
    {
      DispatcherQueueTestHelpers.ShutdownSyncBounded(_dispatcherController);
    }

    /// <summary>
    /// Invariant: constructor must not call any client methods before activation.
    /// Prevents constructor fire-and-forget regression (RETAINED_ASYNC_RULE, ADR-047).
    /// </summary>
    [TestMethod]
    public void Constructor_DoesNotCallClient_BeforeActivation()
    {
      _ = new TrainingDatasetEditorViewModel(_context, _mockTrainingClient.Object, _mockDatasetEditorClient.Object);
      _mockTrainingClient.Verify(x => x.ListDatasetsAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockDatasetEditorClient.Verify(x => x.GetDatasetDetailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithITrainingDatasetEditorClient_CreatesInstance()
    {
      var vm = new TrainingDatasetEditorViewModel(_context, _mockTrainingClient.Object, _mockDatasetEditorClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.TrainingDatasetEditor, vm.PanelId);
      Assert.IsNotNull(vm.LoadDatasetCommand);
      Assert.IsNotNull(vm.AddAudioCommand);
      Assert.IsNotNull(vm.UpdateAudioCommand);
      Assert.IsNotNull(vm.RemoveAudioCommand);
      Assert.IsNotNull(vm.ValidateCommand);
      Assert.IsNotNull(vm.RefreshCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullDatasetEditorClient_Throws()
    {
      _ = new TrainingDatasetEditorViewModel(_context, _mockTrainingClient.Object, null!);
    }

    [TestMethod]
    public async Task InitializeAsync_CallsITrainingClient_ListDatasetsAsync()
    {
      var vm = new TrainingDatasetEditorViewModel(_context, _mockTrainingClient.Object, _mockDatasetEditorClient.Object);
      await vm.InitializeAsync(CancellationToken.None);
      _mockTrainingClient.Verify(
          x => x.ListDatasetsAsync(It.IsAny<CancellationToken>()),
          Times.Once);
    }
  }
}
