using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Integration tests for GlobalSearchViewModel (IDEA 5: Global Search).
  /// Tests search functionality with mocked BackendClient.
  /// </summary>
  [TestClass]
  public class GlobalSearchViewModelTests : TestBase
  {
    private MockBackendClient? _mockBackendClient;
    private GlobalSearchViewModel? _viewModel;

    [TestInitialize]
    public override void TestInitialize()
    {
      base.TestInitialize();
      _mockBackendClient = new MockBackendClient();
      _viewModel = new GlobalSearchViewModel(_mockBackendClient);
    }

    [TestCleanup]
    public override void TestCleanup()
    {
      _viewModel = null;
      _mockBackendClient = null;
      base.TestCleanup();
    }

    [TestMethod]
    public void SearchQuery_Empty_DoesNotSearch()
    {
      // Arrange
      _viewModel!.SearchQuery = string.Empty;

      // Act - Wait for async operation
      Task.Delay(100).Wait();

      // Assert
      Assert.AreEqual(0, _viewModel.Results.Count);
      Assert.AreEqual(0, _viewModel.TotalResults);
      Assert.AreEqual(0, _mockBackendClient!.SearchCallCount);
    }

    [TestMethod]
    public void SearchQuery_TooShort_DoesNotSearch()
    {
      // Arrange
      _viewModel!.SearchQuery = "a";

      // Act - Wait for async operation
      Task.Delay(100).Wait();

      // Assert
      Assert.AreEqual(0, _viewModel.Results.Count);
      Assert.AreEqual(0, _viewModel.TotalResults);
      Assert.AreEqual(0, _mockBackendClient!.SearchCallCount);
    }

    [TestMethod]
    public async Task SearchAsync_ValidQuery_SearchesBackend()
    {
      // Arrange
      _mockBackendClient!.SearchResponse = new SearchResponse
      {
        Results = new List<SearchResultItem>
                {
                    new SearchResultItem { Id = "1", Title = "Test Result", Type = "profile" }
                },
        TotalResults = 1,
        ResultsByType = new Dictionary<string, int> { { "profile", 1 } }
      };
      _viewModel!.SearchQuery = "test";

      // Act
      await _viewModel.SearchAsync();

      // Assert - explicit SearchAsync() yields 1 backend call; debounce would add another if Dispatcher ran
      Assert.AreEqual(1, _viewModel.Results.Count);
      Assert.AreEqual(1, _viewModel.TotalResults);
      Assert.AreEqual(1, _mockBackendClient.SearchCallCount);
      Assert.AreEqual("test", _mockBackendClient.LastSearchQuery);
    }

    [TestMethod]
    public async Task SearchAsync_Success_UpdatesResults()
    {
      // Arrange
      var results = new List<SearchResultItem>
            {
                new SearchResultItem { Id = "1", Title = "Result 1", Type = "profile" },
                new SearchResultItem { Id = "2", Title = "Result 2", Type = "project" }
            };
      _mockBackendClient!.SearchResponse = new SearchResponse
      {
        Results = results,
        TotalResults = 2,
        ResultsByType = new Dictionary<string, int> { { "profile", 1 }, { "project", 1 } }
      };
      _viewModel!.SearchQuery = "test";

      // Act
      await _viewModel.SearchAsync();

      // Assert
      Assert.AreEqual(2, _viewModel.Results.Count);
      Assert.AreEqual(2, _viewModel.FilteredResults.Count);
      Assert.AreEqual(2, _viewModel.TotalResults);
      Assert.AreEqual(2, _viewModel.ResultsByType.Count);
      Assert.IsNotNull(_viewModel.SelectedResult);
    }

    [TestMethod]
    public async Task SearchAsync_Success_SelectsFirstResult()
    {
      // Arrange
      var firstResult = new SearchResultItem { Id = "1", Title = "First Result", Type = "profile" };
      _mockBackendClient!.SearchResponse = new SearchResponse
      {
        Results = new List<SearchResultItem> { firstResult },
        TotalResults = 1,
        ResultsByType = new Dictionary<string, int> { { "profile", 1 } }
      };
      _viewModel!.SearchQuery = "test";

      // Act
      await _viewModel.SearchAsync();

      // Assert
      Assert.IsNotNull(_viewModel.SelectedResult);
      Assert.AreEqual("1", _viewModel.SelectedResult.Id);
      Assert.AreEqual("First Result", _viewModel.SelectedResult.Title);
    }

    [TestMethod]
    public async Task SearchAsync_Error_SetsErrorMessage()
    {
      // Arrange
      _mockBackendClient!.SearchException = new Exception("Backend error");
      _viewModel!.SearchQuery = "test";

      // Act
      await _viewModel.SearchAsync();

      // Assert
      Assert.IsNotNull(_viewModel.ErrorMessage);
      Assert.IsTrue(_viewModel.ErrorMessage.Contains("Search failed"));
      Assert.AreEqual(0, _viewModel.Results.Count);
      Assert.AreEqual(0, _viewModel.TotalResults);
    }

    [TestMethod]
    public async Task SearchAsync_Error_ClearsResults()
    {
      // Arrange
      // First, do a successful search
      _mockBackendClient!.SearchResponse = new SearchResponse
      {
        Results = new List<SearchResultItem> { new SearchResultItem { Id = "1", Title = "Result", Type = "profile" } },
        TotalResults = 1,
        ResultsByType = new Dictionary<string, int> { { "profile", 1 } }
      };
      _viewModel!.SearchQuery = "test";
      await _viewModel.SearchAsync();

      // Now cause an error
      _mockBackendClient.SearchException = new Exception("Backend error");
      _viewModel.SearchQuery = "error";

      // Act
      await _viewModel.SearchAsync();

      // Assert
      Assert.AreEqual(0, _viewModel.Results.Count);
      Assert.AreEqual(0, _viewModel.FilteredResults.Count);
      Assert.AreEqual(0, _viewModel.TotalResults);
      Assert.AreEqual(0, _viewModel.ResultsByType.Count);
    }

    [TestMethod]
    public async Task SearchAsync_SetsIsLoading()
    {
      // Arrange
      var blocker = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      _mockBackendClient!.SearchBlocker = blocker;
      _mockBackendClient!.SearchResponse = new SearchResponse
      {
        Results = new List<SearchResultItem>(),
        TotalResults = 0,
        ResultsByType = new Dictionary<string, int>()
      };
      _viewModel!.SearchQuery = "test"; // triggers SearchAsync via OnSearchQueryChanged

      // Wait briefly for the async search to start and become observable.
      for (var i = 0; i < 50 && !_viewModel.IsLoading; i++)
      {
        await Task.Delay(5);
      }

      // Assert - Should be loading while backend is blocked
      Assert.IsTrue(_viewModel.IsLoading);

      // Release backend and allow search to complete
      blocker.SetResult(true);

      // Wait for completion
      for (var i = 0; i < 50 && _viewModel.IsLoading; i++)
      {
        await Task.Delay(5);
      }

      // Assert - Should not be loading after completion
      Assert.IsFalse(_viewModel.IsLoading);
    }

    [TestMethod]
    public async Task OnSearchQueryChanged_DebouncesSearch()
    {
      // Arrange
      _mockBackendClient!.SearchResponse = new SearchResponse
      {
        Results = new List<SearchResultItem>(),
        TotalResults = 0,
        ResultsByType = new Dictionary<string, int>()
      };

      // Act: set SearchQuery (triggers 300ms debounce)
      _viewModel!.SearchQuery = "test";

      // Assert: within 100ms, debounce has not fired yet (no backend call)
      await Task.Delay(100);
      Assert.AreEqual(0, _mockBackendClient.SearchCallCount, "Debounce should delay search; no call within 100ms");
    }

  }
}