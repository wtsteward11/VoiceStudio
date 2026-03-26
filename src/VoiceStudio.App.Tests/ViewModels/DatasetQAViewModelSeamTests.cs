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
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for DatasetQAViewModel.
  /// Instantiates ViewModel with mocked IDatasetQAClient.
  /// Supports "DatasetQAViewModel migrated to IDatasetQAClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class DatasetQAViewModelSeamTests
  {
    private Mock<IDatasetQAClient> _mockDatasetQAClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockDatasetQAClient = new Mock<IDatasetQAClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockDatasetQAClient
        .Setup(x => x.GetTrainingDatasetsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<TrainingDataset>());
    }

    [TestCleanup]
    public void Cleanup()
    {
      _dispatcherController?.ShutdownQueueAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Invariant: constructor must not call any client methods before activation.
    /// Prevents constructor fire-and-forget regression (RETAINED_ASYNC_RULE, ADR-047).
    /// </summary>
    [TestMethod]
    public void Constructor_DoesNotCallClient_BeforeActivation()
    {
      _ = new DatasetQAViewModel(_context, _mockDatasetQAClient.Object);
      _mockDatasetQAClient.Verify(x => x.GetTrainingDatasetsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClients_CreatesInstance()
    {
      var vm = new DatasetQAViewModel(_context, _mockDatasetQAClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.DatasetQA, vm.PanelId);
      Assert.IsNotNull(vm.LoadDatasetsCommand);
      Assert.IsNotNull(vm.RunQACommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullDatasetQAClient_Throws()
    {
      _ = new DatasetQAViewModel(_context, null!);
    }

    [TestMethod]
    public void ViewModel_ImplementsIPanelLifecycle()
    {
      var vm = new DatasetQAViewModel(_context, _mockDatasetQAClient.Object);
      Assert.IsTrue(vm is IPanelLifecycle);
    }
  }
}
