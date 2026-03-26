using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for AdvancedSearchViewModel.
  /// Instantiates ViewModel with mocked ISearchClient.
  /// Supports "AdvancedSearchViewModel migrated to ISearchClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class AdvancedSearchViewModelSeamTests
  {
    private Mock<ISearchClient> _mockClient = null!;

    [TestInitialize]
    public void Setup()
    {
      _mockClient = new Mock<ISearchClient>();
      _mockClient
          .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new SearchResponse
          {
            Results = new List<SearchResultItem>(),
            TotalResults = 0,
            ResultsByType = new Dictionary<string, int>()
          });
    }

    /// <summary>
    /// Invariant: constructor must not call any client methods before activation.
    /// Prevents constructor fire-and-forget regression (RETAINED_ASYNC_RULE, ADR-047).
    /// </summary>
    [TestMethod]
    public void Constructor_DoesNotCallClient_BeforeActivation()
    {
      _ = new AdvancedSearchViewModel(_mockClient.Object);
      _mockClient.Verify(
          x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
          Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClient_CreatesInstance()
    {
      var vm = new AdvancedSearchViewModel(_mockClient.Object);
      Assert.IsNotNull(vm);
      Assert.IsNotNull(vm.SearchResults);
      Assert.IsNotNull(vm.QueryHistory);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullClient_Throws()
    {
      _ = new AdvancedSearchViewModel(null!);
    }

    [TestMethod]
    public async Task PerformSearchAsync_ValidQuery_CallsISearchClient_SearchAsync()
    {
      _mockClient
          .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new SearchResponse
          {
            Results = new List<SearchResultItem> { new SearchResultItem { Id = "1", Title = "Test", Type = "profile" } },
            TotalResults = 1,
            ResultsByType = new Dictionary<string, int> { { "profile", 1 } }
          });

      var vm = new AdvancedSearchViewModel(_mockClient.Object);
      await vm.PerformSearchAsync("test");

      _mockClient.Verify(
          x => x.SearchAsync("test", null, 50, It.IsAny<CancellationToken>()),
          Times.Once);
    }
  }
}
