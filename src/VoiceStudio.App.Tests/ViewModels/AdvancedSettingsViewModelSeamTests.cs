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
  /// Seam-aware tests for AdvancedSettingsViewModel.
  /// Instantiates ViewModel with mocked IAdvancedSettingsClient.
  /// Supports "AdvancedSettingsViewModel migrated to IAdvancedSettingsClient" claims.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class AdvancedSettingsViewModelSeamTests
  {
    private Mock<IAdvancedSettingsClient> _mockClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockClient = new Mock<IAdvancedSettingsClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockClient
        .Setup(x => x.GetSettingsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync((AdvancedSettingsData?)null);
      _mockClient
        .Setup(x => x.GetGpuDevicesAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<GpuDeviceInfo>());
    }

    [TestCleanup]
    public void Cleanup()
    {
      _dispatcherController?.ShutdownQueueAsync().AsTask().GetAwaiter().GetResult();
    }

    [TestMethod]
    public void Constructor_WithIAdvancedSettingsClient_CreatesInstance()
    {
      var vm = new AdvancedSettingsViewModel(_context, _mockClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual("advanced-settings", vm.PanelId);
      Assert.IsNotNull(vm.LoadSettingsCommand);
      Assert.IsNotNull(vm.SaveSettingsCommand);
      Assert.IsNotNull(vm.ResetSettingsCommand);
      Assert.IsNotNull(vm.RefreshCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullClient_Throws()
    {
      _ = new AdvancedSettingsViewModel(_context, null!);
    }

    [TestMethod]
    public void ViewModel_ImplementsIPanelLifecycle()
    {
      var vm = new AdvancedSettingsViewModel(_context, _mockClient.Object);
      Assert.IsTrue(vm is IPanelLifecycle);
    }
  }
}
