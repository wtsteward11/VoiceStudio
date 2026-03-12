using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services
{
  /// <summary>
  /// Unit tests for ProjectsClient as the authoritative projects transport boundary.
  /// </summary>
  [TestClass]
  public class ProjectsClientTests
  {
    private Mock<IBackendClient> _mockBackend = null!;
    private Mock<IRequestCoordinator> _mockCoordinator = null!;
    private ProjectsClient _sut = null!;

    [TestInitialize]
    public void Setup()
    {
      _mockBackend = new Mock<IBackendClient>();
      _mockCoordinator = new Mock<IRequestCoordinator>();
      _sut = new ProjectsClient(_mockBackend.Object, _mockCoordinator.Object);
    }

    [TestMethod]
    public async Task GetProjectsAsync_DelegatesToBackend()
    {
      var expected = new List<Project> { new Project { Id = "p1", Name = "Project 1" } };
      _mockBackend
        .Setup(x => x.GetProjectsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(expected);

      var result = await _sut.GetProjectsAsync();

      Assert.AreEqual(1, result.Count);
      Assert.AreEqual("p1", result[0].Id);
    }

    [TestMethod]
    public async Task CreateProjectAsync_DelegatesAndInvalidatesCache()
    {
      var created = new Project { Id = "new-1", Name = "New Project" };
      _mockBackend
        .Setup(x => x.CreateProjectAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(created);

      var result = await _sut.CreateProjectAsync("New Project");

      Assert.AreEqual("new-1", result.Id);
      _mockCoordinator.Verify(x => x.Invalidate(ProjectsClient.ProjectsListKey), Times.Once);
    }

    [TestMethod]
    public async Task DeleteProjectAsync_WhenSuccess_InvalidatesCache()
    {
      _mockBackend
        .Setup(x => x.DeleteProjectAsync("p1", It.IsAny<CancellationToken>()))
        .ReturnsAsync(true);

      var result = await _sut.DeleteProjectAsync("p1");

      Assert.IsTrue(result);
      _mockCoordinator.Verify(x => x.Invalidate(ProjectsClient.ProjectsListKey), Times.Once);
    }

    [TestMethod]
    public void ProjectsListKey_IsProjectsList()
    {
      Assert.AreEqual("projects:list", ProjectsClient.ProjectsListKey);
    }
  }
}
