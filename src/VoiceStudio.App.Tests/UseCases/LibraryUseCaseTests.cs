using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.UseCases;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.UseCases
{
  [TestClass]
  public class LibraryUseCaseTests
  {
    private Mock<IBackendClient> _mockBackendClient = null!;
    private Mock<IContextManager> _mockContext = null!;
    private Mock<IProjectAudioClient> _mockProjectAudio = null!;
    private LibraryUseCase _useCase = null!;

    [TestInitialize]
    public void Setup()
    {
      _mockBackendClient = new Mock<IBackendClient>();
      _mockContext = new Mock<IContextManager>();
      _mockProjectAudio = new Mock<IProjectAudioClient>();
      _useCase = new LibraryUseCase(_mockBackendClient.Object, _mockContext.Object, _mockProjectAudio.Object);
    }

    [TestMethod]
    public async Task ListFoldersAsync_ReturnsEmptyList_WhenBackendReturnsNull()
    {
      // Arrange
      _mockBackendClient
          .Setup(x => x.GetAsync<LibraryUseCase.LibraryFoldersResponse>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync((LibraryUseCase.LibraryFoldersResponse?)null);

      // Act
      var result = await _useCase.ListFoldersAsync();

      // Assert
      Assert.IsNotNull(result);
      Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public async Task CreateFolderAsync_ThrowsArgumentException_WhenNameIsEmpty()
    {
      // Act & Assert
      await Assert.ThrowsExceptionAsync<System.ArgumentException>(
          () => _useCase.CreateFolderAsync(""));
    }

    [TestMethod]
    public async Task CreateFolderAsync_ThrowsArgumentException_WhenNameIsNull()
    {
      // Act & Assert
      await Assert.ThrowsExceptionAsync<System.ArgumentNullException>(
          () => _useCase.CreateFolderAsync(null!));
    }

    [TestMethod]
    public async Task GetFolderContentsAsync_ThrowsArgumentException_WhenFolderIdIsEmpty()
    {
      // Act & Assert
      await Assert.ThrowsExceptionAsync<System.ArgumentException>(
          () => _useCase.GetFolderContentsAsync(""));
    }

    [TestMethod]
    public async Task RenameFolderAsync_ThrowsArgumentException_WhenFolderIdIsEmpty()
    {
      // Act & Assert
      await Assert.ThrowsExceptionAsync<System.ArgumentException>(
          () => _useCase.RenameFolderAsync("", "newName"));
    }

    [TestMethod]
    public async Task RenameFolderAsync_ThrowsArgumentException_WhenNewNameIsEmpty()
    {
      // Act & Assert
      await Assert.ThrowsExceptionAsync<System.ArgumentException>(
          () => _useCase.RenameFolderAsync("folderId", ""));
    }

    [TestMethod]
    public async Task DeleteFolderAsync_ThrowsArgumentException_WhenFolderIdIsEmpty()
    {
      // Act & Assert
      await Assert.ThrowsExceptionAsync<System.ArgumentException>(
          () => _useCase.DeleteFolderAsync(""));
    }

    [TestMethod]
    public async Task ImportFilesAsync_ReturnsEmptyList_WhenNoFilesProvided()
    {
      // Act
      var result = await _useCase.ImportFilesAsync(new List<string>());

      // Assert
      Assert.IsNotNull(result);
      Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public async Task SearchAsync_CallsBackendWithCorrectEndpoint()
    {
      // Arrange
      var query = "test query";
      string? capturedEndpoint = null;
      _mockBackendClient
          .Setup(x => x.GetAsync<LibraryUseCase.LibrarySearchResponse>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .Callback<string, CancellationToken>((endpoint, ct) => capturedEndpoint = endpoint)
          .ReturnsAsync((LibraryUseCase.LibrarySearchResponse?)null);

      // Act
      await _useCase.SearchAsync(query);

      // Assert
      Assert.IsNotNull(capturedEndpoint);
      Assert.IsTrue(capturedEndpoint.Contains(System.Uri.EscapeDataString(query)));
    }

    [TestMethod]
    public async Task SearchAsync_IncludesOptionsInEndpoint()
    {
      // Arrange
      var query = "test";
      var options = new LibrarySearchOptions
      {
        FolderId = "folder123",
        MaxResults = 50,
        SortBy = "name",
        Descending = true
      };
      string? capturedEndpoint = null;
      _mockBackendClient
          .Setup(x => x.GetAsync<LibraryUseCase.LibrarySearchResponse>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .Callback<string, CancellationToken>((endpoint, ct) => capturedEndpoint = endpoint)
          .ReturnsAsync((LibraryUseCase.LibrarySearchResponse?)null);

      // Act
      await _useCase.SearchAsync(query, options);

      // Assert
      Assert.IsNotNull(capturedEndpoint);
      Assert.IsTrue(capturedEndpoint.Contains("folderId=folder123"));
      Assert.IsTrue(capturedEndpoint.Contains("maxResults=50"));
      Assert.IsTrue(capturedEndpoint.Contains("sortBy=name"));
    }

    [TestMethod]
    public async Task DeleteItemsAsync_ReturnsZero_WhenNoItemsProvided()
    {
      // Act
      var result = await _useCase.DeleteItemsAsync(new List<string>());

      // Assert
      Assert.AreEqual(0, result);
    }

    [TestMethod]
    public async Task MoveItemsAsync_ReturnsZero_WhenNoItemsProvided()
    {
      // Act
      var result = await _useCase.MoveItemsAsync(new List<string>(), "targetFolder");

      // Assert
      Assert.AreEqual(0, result);
    }

    [TestMethod]
    public async Task ExportItemsAsync_ReturnsInputPath_WhenNoItemsProvided()
    {
      // Arrange
      var exportPath = "/test/path";

      // Act
      var result = await _useCase.ExportItemsAsync(new List<string>(), exportPath);

      // Assert
      Assert.AreEqual(exportPath, result);
    }
  }
}
