using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for TextBasedSpeechEditorViewModel.
  /// Instantiates ViewModel with mocked ITextBasedSpeechEditorClient, IProfilesClient.
  /// Supports "TextBasedSpeechEditorViewModel migrated to ITextBasedSpeechEditorClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class TextBasedSpeechEditorViewModelSeamTests
  {
    private Mock<ITextBasedSpeechEditorClient> _mockEditorClient = null!;
    private Mock<IProfilesClient> _mockProfilesClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockEditorClient = new Mock<ITextBasedSpeechEditorClient>();
      _mockProfilesClient = new Mock<IProfilesClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockProfilesClient.Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<VoiceProfile>());
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
      _ = new TextBasedSpeechEditorViewModel(_context, _mockEditorClient.Object, _mockProfilesClient.Object);
      _mockEditorClient.Verify(x => x.GetEnginesAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockProfilesClient.Verify(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClients_CreatesInstance()
    {
      var vm = new TextBasedSpeechEditorViewModel(_context, _mockEditorClient.Object, _mockProfilesClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual("text-based-speech-editor", vm.PanelId);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullEditorClient_Throws()
    {
      _ = new TextBasedSpeechEditorViewModel(_context, null!, _mockProfilesClient.Object);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullProfilesClient_Throws()
    {
      _ = new TextBasedSpeechEditorViewModel(_context, _mockEditorClient.Object, null!);
    }
  }
}
