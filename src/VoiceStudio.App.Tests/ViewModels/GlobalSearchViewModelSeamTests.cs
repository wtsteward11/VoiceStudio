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
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for GlobalSearchViewModel.
  /// Instantiates ViewModel with mocked ISearchClient.
  /// Supports "GlobalSearchViewModel migrated to ISearchClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class GlobalSearchViewModelSeamTests
  {
    private Mock<ISearchClient> _mockClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockClient = new Mock<ISearchClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockClient
          .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new SearchResponse
          {
            Results = new List<SearchResultItem>(),
            TotalResults = 0,
            ResultsByType = new Dictionary<string, int>()
          });
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
      _ = new GlobalSearchViewModel(_mockClient.Object);
      _mockClient.Verify(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithISearchClient_CreatesInstance()
    {
      var vm = new GlobalSearchViewModel(_mockClient.Object);
      Assert.IsNotNull(vm);
      Assert.IsNotNull(vm.SearchCommand);
      Assert.IsNotNull(vm.Results);
      Assert.IsNotNull(vm.FilteredResults);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullClient_Throws()
    {
      _ = new GlobalSearchViewModel((ISearchClient)null!);
    }

    [TestMethod]
    public async Task SearchAsync_ValidQuery_CallsISearchClient_SearchAsync()
    {
      _mockClient
          .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new SearchResponse
          {
            Results = new List<SearchResultItem> { new SearchResultItem { Id = "1", Title = "Test", Type = "profile" } },
            TotalResults = 1,
            ResultsByType = new Dictionary<string, int> { { "profile", 1 } }
          });

      var vm = new GlobalSearchViewModel(_mockClient.Object);
      vm.SearchQuery = "test";
      await vm.SearchAsync();

      _mockClient.Verify(
          x => x.SearchAsync("test", null, 50, It.IsAny<CancellationToken>()),
          Times.Once);
    }

    [TestMethod]
    public async Task SearchAsync_EmptyQuery_DoesNotCallSearchAsync()
    {
      var vm = new GlobalSearchViewModel(_mockClient.Object);
      vm.SearchQuery = string.Empty;
      await vm.SearchAsync();

      _mockClient.Verify(
          x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
          Times.Never);
    }
  }
}
