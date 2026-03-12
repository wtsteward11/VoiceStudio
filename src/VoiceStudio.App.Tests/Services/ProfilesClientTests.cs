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
  /// Unit tests for ProfilesClient as the authoritative profiles transport boundary.
  /// Verifies delegation, invalidation ownership, and that consumers target ProfilesClient as the main seam.
  /// </summary>
  [TestClass]
  public class ProfilesClientTests
  {
    private Mock<IBackendClient> _mockBackend = null!;
    private Mock<IRequestCoordinator> _mockCoordinator = null!;
    private ProfilesClient _sut = null!;

    [TestInitialize]
    public void Setup()
    {
      _mockBackend = new Mock<IBackendClient>();
      _mockCoordinator = new Mock<IRequestCoordinator>();
      _sut = new ProfilesClient(_mockBackend.Object, _mockCoordinator.Object);
    }

    [TestMethod]
    public void Constructor_WithNullBackend_ThrowsArgumentNullException()
    {
      Assert.ThrowsException<ArgumentNullException>(() =>
        new ProfilesClient(null!, _mockCoordinator.Object));
    }

    [TestMethod]
    public void Constructor_WithNullCoordinator_ThrowsArgumentNullException()
    {
      Assert.ThrowsException<ArgumentNullException>(() =>
        new ProfilesClient(_mockBackend.Object, null!));
    }

    [TestMethod]
    public async Task GetProfilesAsync_DelegatesToBackend()
    {
      var expected = new List<VoiceProfile> { new VoiceProfile { Id = "p1", Name = "Profile 1" } };
      _mockBackend
        .Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(expected);

      var result = await _sut.GetProfilesAsync();

      Assert.AreEqual(1, result.Count);
      Assert.AreEqual("p1", result[0].Id);
      _mockBackend.Verify(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task GetProfileAsync_DelegatesToBackend()
    {
      var expected = new VoiceProfile { Id = "p1", Name = "Profile 1" };
      _mockBackend
        .Setup(x => x.GetProfileAsync("p1", It.IsAny<CancellationToken>()))
        .ReturnsAsync(expected);

      var result = await _sut.GetProfileAsync("p1");

      Assert.AreEqual("p1", result.Id);
      _mockBackend.Verify(x => x.GetProfileAsync("p1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CreateProfileAsync_DelegatesAndInvalidatesCache()
    {
      var created = new VoiceProfile { Id = "new-1", Name = "New Profile" };
      _mockBackend
        .Setup(x => x.CreateProfileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(created);

      var result = await _sut.CreateProfileAsync("New Profile");

      Assert.AreEqual("new-1", result.Id);
      _mockCoordinator.Verify(x => x.Invalidate(ProfilesClient.ProfilesListKey), Times.Once);
    }

    [TestMethod]
    public async Task UpdateProfileAsync_DelegatesAndInvalidatesCache()
    {
      var updated = new VoiceProfile { Id = "p1", Name = "Updated" };
      _mockBackend
        .Setup(x => x.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(updated);

      var result = await _sut.UpdateProfileAsync("p1", "Updated");

      Assert.AreEqual("Updated", result.Name);
      _mockCoordinator.Verify(x => x.Invalidate(ProfilesClient.ProfilesListKey), Times.Once);
    }

    [TestMethod]
    public async Task DeleteProfileAsync_WhenSuccess_InvalidatesCache()
    {
      _mockBackend
        .Setup(x => x.DeleteProfileAsync("p1", It.IsAny<CancellationToken>()))
        .ReturnsAsync(true);

      var result = await _sut.DeleteProfileAsync("p1");

      Assert.IsTrue(result);
      _mockCoordinator.Verify(x => x.Invalidate(ProfilesClient.ProfilesListKey), Times.Once);
    }

    [TestMethod]
    public async Task DeleteProfileAsync_WhenFailure_DoesNotInvalidate()
    {
      _mockBackend
        .Setup(x => x.DeleteProfileAsync("p1", It.IsAny<CancellationToken>()))
        .ReturnsAsync(false);

      var result = await _sut.DeleteProfileAsync("p1");

      Assert.IsFalse(result);
      _mockCoordinator.Verify(x => x.Invalidate(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public void InvalidateProfilesCache_CallsCoordinatorWithCorrectKey()
    {
      _sut.InvalidateProfilesCache();

      _mockCoordinator.Verify(x => x.Invalidate(ProfilesClient.ProfilesListKey), Times.Once);
    }

    [TestMethod]
    public void ProfilesListKey_IsProfilesList()
    {
      Assert.AreEqual("profiles:list", ProfilesClient.ProfilesListKey);
    }
  }
}
