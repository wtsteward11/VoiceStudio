using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for TranscribeViewModel.
  /// Instantiates TranscribeViewModel with mocked ITranscriptionClient.
  /// Supports "TranscribeViewModel migrated to ITranscriptionClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class TranscribeViewModelSeamTests
  {
    private Mock<ITranscriptionClient> _mockTranscriptionClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      _mockTranscriptionClient = new Mock<ITranscriptionClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      var services = new ServiceCollection();
      services.AddSingleton<MultiSelectService>();
      AppServices.Initialize(services.BuildServiceProvider());

      _mockTranscriptionClient
          .Setup(x => x.GetSupportedLanguagesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<SupportedLanguage>());
      _mockTranscriptionClient
          .Setup(x => x.GetTranscriptionEnginesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TranscriptionEngine>());
    }

    [TestCleanup]
    public void Cleanup()
    {
      _dispatcherController?.ShutdownQueueAsync().AsTask().GetAwaiter().GetResult();
    }

    [TestMethod]
    public void Constructor_WithITranscriptionClient_CreatesInstance()
    {
      var vm = new TranscribeViewModel(_context, _mockTranscriptionClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual("transcribe", vm.PanelId);
      Assert.IsNotNull(vm.TranscribeCommand);
      Assert.IsNotNull(vm.LoadTranscriptionsCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullTranscriptionClient_Throws()
    {
      _ = new TranscribeViewModel(_context, null!);
    }

    [TestMethod]
    public async Task InitializeAsync_CallsITranscriptionClient_GetTranscriptionEnginesAsync()
    {
      var vm = new TranscribeViewModel(_context, _mockTranscriptionClient.Object);
      await vm.InitializeAsync(CancellationToken.None);
      _mockTranscriptionClient.Verify(
          x => x.GetTranscriptionEnginesAsync(It.IsAny<CancellationToken>()),
          Times.Once);
    }

    [TestMethod]
    public async Task InitializeAsync_CallsITranscriptionClient_GetSupportedLanguagesAsync()
    {
      var vm = new TranscribeViewModel(_context, _mockTranscriptionClient.Object);
      await vm.InitializeAsync(CancellationToken.None);
      _mockTranscriptionClient.Verify(
          x => x.GetSupportedLanguagesAsync(It.IsAny<CancellationToken>()),
          Times.Once);
    }
  }
}
