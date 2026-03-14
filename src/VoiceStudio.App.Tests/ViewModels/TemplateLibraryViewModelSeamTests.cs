using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
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
  /// Seam-aware tests for TemplateLibraryViewModel.
  /// Instantiates ViewModel with mocked ITemplateLibraryClient.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class TemplateLibraryViewModelSeamTests
  {
    private Mock<ITemplateLibraryClient> _mockClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockClient = new Mock<ITemplateLibraryClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockClient
        .Setup(x => x.GetTemplatesAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((TemplateLibraryTemplate[]?)null);
      _mockClient
        .Setup(x => x.GetCategoriesAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync((string[]?)null);
    }

    [TestCleanup]
    public void Cleanup()
    {
      _dispatcherController?.ShutdownQueueAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Invariant: constructor must not call any client methods before activation.
    /// </summary>
    [TestMethod]
    public void Constructor_DoesNotCallClient_BeforeActivation()
    {
      _ = new TemplateLibraryViewModel(_context, _mockClient.Object);
      _mockClient.Verify(x => x.GetTemplatesAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockClient.Verify(x => x.GetCategoriesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithITemplateLibraryClient_CreatesInstance()
    {
      var vm = new TemplateLibraryViewModel(_context, _mockClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual("template_library", vm.PanelId);
      Assert.IsNotNull(vm.LoadTemplatesCommand);
      Assert.IsNotNull(vm.CreateTemplateCommand);
      Assert.IsNotNull(vm.RefreshCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullClient_Throws()
    {
      _ = new TemplateLibraryViewModel(_context, null!);
    }

    [TestMethod]
    public void ViewModel_ImplementsIPanelLifecycle()
    {
      var vm = new TemplateLibraryViewModel(_context, _mockClient.Object);
      Assert.IsTrue(vm is IPanelLifecycle);
    }

    [TestMethod]
    public async Task OnActivatedAsync_LoadsCategoriesAndTemplates()
    {
      var vm = new TemplateLibraryViewModel(_context, _mockClient.Object);
      await vm.OnActivatedAsync(CancellationToken.None);
      _mockClient.Verify(x => x.GetCategoriesAsync(It.IsAny<CancellationToken>()), Times.Once);
      _mockClient.Verify(x => x.GetTemplatesAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }
  }
}
